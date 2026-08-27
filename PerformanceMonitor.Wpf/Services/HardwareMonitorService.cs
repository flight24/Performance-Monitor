using System.IO;
using System.Runtime.InteropServices;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace PerformanceMonitor.Wpf.Services;

public class MonitorData
{
    // 缺失的传感器值用 double.NaN 表示
    public double Cpu { get; set; }
    public double CpuFreq { get; set; }
    public double CpuTemp { get; set; } = double.NaN;
    public double CpuVoltage { get; set; } = double.NaN;
    public double Gpu { get; set; }
    public double GpuTemp { get; set; } = double.NaN;
    public double GpuMemUsed { get; set; } = double.NaN;
    public double GpuMemTotal { get; set; } = double.NaN;
    public double Mem { get; set; }
    public double Disk { get; set; }
    public double DiskReadMb { get; set; }
    public double DiskWriteMb { get; set; }
    public double NetPct { get; set; }
    public double NetDownMbps { get; set; }
    public double NetUpMbps { get; set; }
}

/// <summary>
/// 数据采集后端（C# 原生重写，替代原 Python monitor.py）。
/// 降级链与原实现一致：GPU 负载 PDH → LHM → WMI；CPU 频率 PDH → LHM → WMI。
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private Computer _computer;
    private IHardware _cpu, _gpu;

    private PdhInterop.Query _pdh;
    private IntPtr _cpuUtilHandle;
    private IntPtr _cpuPerfHandle;
    private double _cpuBaseMhz;
    private readonly List<IntPtr> _gpuEngineHandles = new();
    private IntPtr _diskReadHandle, _diskWriteHandle;
    private readonly List<(IntPtr rx, IntPtr tx)> _netHandles = new();

    private int _tick;
    private double _lastGpuLoadWmi = -1;
    private double _lastNonZeroGpu;
    private int _zeroStreak;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    public void Initialize()
    {
        PdhInterop.Warmup();

        // ---- LibreHardwareMonitor ----
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            _computer.Open();
            foreach (var hw in _computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu && _cpu == null)
                    _cpu = hw;
                else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    _gpu = hw;
                    break;
                }
            }
            if (_gpu == null)
            {
                foreach (var hw in _computer.Hardware)
                {
                    foreach (var sub in hw.SubHardware)
                    {
                        if (sub.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                        {
                            _gpu = sub;
                            break;
                        }
                    }
                    if (_gpu != null) break;
                }
            }
        }
        catch
        {
        }

        // ---- PDH ----
        try
        {
            _pdh = new PdhInterop.Query();
            if (_pdh.Open())
            {
                _cpuUtilHandle = _pdh.AddEnglish(@"\Processor Information(_Total)\% Processor Utility");
                if (_cpuUtilHandle == IntPtr.Zero)
                    _cpuUtilHandle = _pdh.AddEnglish(@"\Processor(_Total)\% Processor Time");

                _cpuPerfHandle = _pdh.AddEnglish(@"\Processor Information(0,0)\% Processor Performance");
                _cpuBaseMhz = GetCpuBaseMHz();

                // GPU Engine 利用率（实例随进程动态增减，运行期会周期性刷新）
                RefreshGpuEngineHandles();

                _diskReadHandle = _pdh.AddEnglish(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
                _diskWriteHandle = _pdh.AddEnglish(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");

                // 网卡：按实例成对添加，保证收发对齐
                var paths = PdhInterop.ExpandWildCard(@"\Network Interface(*)\Bytes Received/sec");
                foreach (var p in paths)
                {
                    string inst = PdhInterop.ExtractInstance(p);
                    if (inst.Length == 0) continue;
                    var rx = _pdh.AddEnglish($@"\Network Interface({inst})\Bytes Received/sec");
                    var tx = _pdh.AddEnglish($@"\Network Interface({inst})\Bytes Sent/sec");
                    if (rx != IntPtr.Zero || tx != IntPtr.Zero)
                        _netHandles.Add((rx, tx));
                }

                _pdh.Collect(); // 预热，首个采样值有效
            }
        }
        catch
        {
        }
    }

    private static double GetCpuBaseMHz()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
            foreach (var o in searcher.Get())
            {
                uint v = (uint)(o["MaxClockSpeed"] ?? 0u);
                if (v > 0) return v;
            }
        }
        catch
        {
        }
        return 0;
    }

    /// <summary>重新枚举 GPU Engine 实例（pid 级实例随进程启停变化）。</summary>
    private void RefreshGpuEngineHandles()
    {
        if (_pdh == null || !_pdh.Ok) return;
        try
        {
            foreach (var h in _gpuEngineHandles) _pdh.Remove(h);
            _gpuEngineHandles.Clear();

            foreach (var p in PdhInterop.ExpandWildCard(@"\GPU Engine(*)\Utilization Percentage"))
            {
                var h = _pdh.AddEnglish(p);
                if (h != IntPtr.Zero) _gpuEngineHandles.Add(h);
            }
        }
        catch
        {
        }
    }

    /// <summary>采集一帧数据（约每秒调用一次）。</summary>
    public MonitorData Collect()
    {
        _tick++;
        if (_tick % 10 == 0) RefreshGpuEngineHandles();
        var d = new MonitorData();

        // ---- LHM 更新 ----
        try
        {
            if (_cpu != null) _cpu.Update();
            if (_gpu != null) _gpu.Update();
        }
        catch
        {
        }

        // ---- PDH 采样 ----
        try { _pdh?.Collect(); } catch { }

        // CPU 使用率
        double v = SafeRead(_cpuUtilHandle);
        d.Cpu = double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 100);

        // 内存
        d.Mem = GetMemoryPercent();

        // 磁盘占用率（系统盘）
        d.Disk = GetSystemDriveUsedPercent();

        // 磁盘 IO (MB/s)
        double r = SafeRead(_diskReadHandle), w = SafeRead(_diskWriteHandle);
        d.DiskReadMb = double.IsNaN(r) ? 0 : r / (1024 * 1024);
        d.DiskWriteMb = double.IsNaN(w) ? 0 : w / (1024 * 1024);

        // 网络
        double downBps = 0, upBps = 0;
        foreach (var (rx, tx) in _netHandles)
        {
            downBps += Math.Max(0, SafeRead(rx));
            upBps += Math.Max(0, SafeRead(tx));
        }
        d.NetDownMbps = downBps / 125000.0; // bytes/s -> Mbps (/1e6*8)
        d.NetUpMbps = upBps / 125000.0;
        d.NetPct = Math.Min((d.NetDownMbps + d.NetUpMbps) / 10.0, 100);

        // CPU 频率：PDH 性能百分比 × 基频 → LHM 最大时钟 → WMI
        double freq = 0;
        if (_cpuPerfHandle != IntPtr.Zero && _cpuBaseMhz > 0)
        {
            double perf = SafeRead(_cpuPerfHandle);
            if (!double.IsNaN(perf) && perf > 0)
                freq = perf / 100.0 * _cpuBaseMhz / 1000.0;
        }
        if (freq <= 0 && _cpu != null)
        {
            double best = 0;
            foreach (var s in EnumSensors(_cpu))
            {
                if (s.SensorType == SensorType.Clock && s.Value.HasValue &&
                    !s.Name.Contains("Bus Speed", StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, s.Value.Value);
                }
            }
            if (best > 0) freq = best / 1000.0;
        }
        if (freq <= 0) freq = GetFreqViaWmi();
        d.CpuFreq = freq;

        // CPU 温度 / 电压（LHM）
        d.CpuTemp = GetCpuTemperature();
        d.CpuVoltage = GetCpuVoltage();

        // GPU 温度 / 显存（LHM）
        d.GpuTemp = GetGpuTemperature();
        (d.GpuMemUsed, d.GpuMemTotal) = GetGpuMemory();

        // GPU 负载：PDH → LHM → WMI（每 3 次回退一次）
        double gpuLoad = 0;
        foreach (var h in _gpuEngineHandles)
        {
            double g = SafeRead(h);
            if (!double.IsNaN(g)) gpuLoad = Math.Max(gpuLoad, g);
        }

        if (gpuLoad <= 0 && _gpu != null)
        {
            double best = 0;
            foreach (var s in EnumSensors(_gpu))
            {
                if (s.SensorType == SensorType.Load && s.Value.HasValue && s.Value.Value > 0)
                    best = Math.Max(best, s.Value.Value);
            }
            gpuLoad = best;
        }

        if (gpuLoad <= 0 && _tick % 3 == 0)
            _lastGpuLoadWmi = GetGpuViaWmi();
        if (gpuLoad <= 0 && _lastGpuLoadWmi > 0)
            gpuLoad = _lastGpuLoadWmi;

        gpuLoad = Math.Clamp(gpuLoad, 0, 100);

        // 与 Python 版一致：短暂归零时保持上次读数，避免闪烁（此处最多保持 3 个周期）
        if (gpuLoad > 0)
        {
            _lastNonZeroGpu = gpuLoad;
            _zeroStreak = 0;
        }
        else
        {
            _zeroStreak++;
            if (_zeroStreak <= 3) gpuLoad = _lastNonZeroGpu;
        }

        d.Gpu = gpuLoad;
        return d;
    }

    private double SafeRead(IntPtr handle)
    {
        var q = _pdh;
        if (q == null || handle == IntPtr.Zero) return double.NaN;
        try { return q.Read(handle); }
        catch { return double.NaN; }
    }

    private static double GetMemoryPercent()
    {
        try
        {
            var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref st) && st.ullTotalPhys > 0)
                return (st.ullTotalPhys - st.ullAvailPhys) * 100.0 / st.ullTotalPhys;
        }
        catch
        {
        }
        return 0;
    }

    private static double GetSystemDriveUsedPercent()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var drive = new DriveInfo(root);
            if (drive.TotalSize > 0)
                return (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize;
        }
        catch
        {
        }
        return 0;
    }

    private double GetCpuTemperature()
    {
        if (_cpu == null) return double.NaN;
        try
        {
            double any = double.NaN;
            foreach (var s in EnumSensors(_cpu))
            {
                if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                if (s.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase))
                    return s.Value.Value;
                if (double.IsNaN(any)) any = s.Value.Value;
            }
            return any;
        }
        catch
        {
        }
        return double.NaN;
    }

    private double GetCpuVoltage()
    {
        if (_cpu == null) return double.NaN;
        try
        {
            foreach (var s in EnumSensors(_cpu))
            {
                if (s.SensorType != SensorType.Voltage || !s.Value.HasValue) continue;
                string name = s.Name.ToLowerInvariant();
                if ((name.Contains("vcore") || name.Contains("v core") ||
                     name.Contains("cpu core") || name.Contains("vid")) && s.Value.Value > 0)
                    return s.Value.Value;
            }
            foreach (var s in EnumSensors(_cpu))
            {
                if (s.SensorType == SensorType.Voltage && s.Value.HasValue &&
                    s.Value.Value > 0.5 && s.Value.Value < 2.0)
                    return s.Value.Value;
            }
        }
        catch
        {
        }
        return double.NaN;
    }

    private double GetGpuTemperature()
    {
        if (_gpu == null) return double.NaN;
        try
        {
            foreach (var s in EnumSensors(_gpu))
            {
                if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                    return s.Value.Value;
            }
        }
        catch
        {
        }
        return double.NaN;
    }

    private (double used, double total) GetGpuMemory()
    {
        if (_gpu == null) return (double.NaN, double.NaN);
        try
        {
            double used = double.NaN, total = double.NaN;
            foreach (var s in EnumSensors(_gpu))
            {
                if (!s.Value.HasValue) continue;
                string name = s.Name.ToLowerInvariant();
                if (name.Contains("memory used") || name.Contains("d3d shared"))
                    used = s.Value.Value;
                else if (name.Contains("memory total"))
                    total = s.Value.Value;
            }
            return (used, total);
        }
        catch
        {
        }
        return (double.NaN, double.NaN);
    }

    private static IEnumerable<ISensor> EnumSensors(IHardware hw)
    {
        try { return hw.Sensors ?? Array.Empty<ISensor>(); }
        catch { return Array.Empty<ISensor>(); }
    }

    private static double GetFreqViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"SELECT ProcessorFrequency, PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='0,0'");
            foreach (var o in searcher.Get())
            {
                double baseFreq = Convert.ToDouble(o["ProcessorFrequency"] ?? 2000u);
                double perf = Convert.ToDouble(o["PercentProcessorPerformance"] ?? 100u);
                return baseFreq * perf / 100.0 / 1000.0;
            }
        }
        catch
        {
        }
        return 0;
    }

    private static double GetGpuViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            double best = 0;
            foreach (var o in searcher.Get())
            {
                double v = Convert.ToDouble(o["UtilizationPercentage"] ?? 0u);
                best = Math.Max(best, v);
            }
            return best;
        }
        catch
        {
        }
        return 0;
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        try { _pdh?.Dispose(); } catch { }
    }
}
