using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
    // 网络流量：改用 NetworkInterface 差分法，不依赖 PDH 计数器名称/类别，
    // 兼容各类硬件与本地化环境（任意网卡名称、中文/英文 Windows、计数器被禁用等）。
    private long _netLastDownBytes = -1, _netLastUpBytes = -1;
    private long _netLastTimestamp = -1;

    private int _tick;

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

        // ---- PDH（CPU / GPU / 磁盘；网络已改用 NetworkInterface，见下方基线）----
        // 各模块独立 try/catch，避免单点失败连累其他模块（兼容各类硬件实例命名差异）。
        try
        {
            _pdh = new PdhInterop.Query();
            if (_pdh.Open())
            {
                // CPU 使用率：优先 Processor Information，失败回退 Processor
                try
                {
                    _cpuUtilHandle = _pdh.AddEnglish(@"\Processor Information(_Total)\% Processor Utility");
                    if (_cpuUtilHandle == IntPtr.Zero)
                        _cpuUtilHandle = _pdh.AddEnglish(@"\Processor(_Total)\% Processor Time");
                }
                catch (Exception ex) { Debug.WriteLine($"cpu util counter: {ex.Message}"); }

                // CPU 性能百分比（用于估算实时频率）：实例名跨硬件可能不同，失败不影响其他模块
                try
                {
                    _cpuPerfHandle = _pdh.AddEnglish(@"\Processor Information(0,0)\% Processor Performance");
                }
                catch (Exception ex) { Debug.WriteLine($"cpu perf counter: {ex.Message}"); }
                _cpuBaseMhz = GetCpuBaseMHz();

                // GPU Engine 利用率（实例随进程动态增减，运行期会周期性刷新）
                try { RefreshGpuEngineHandles(); }
                catch (Exception ex) { Debug.WriteLine($"gpu engine: {ex.Message}"); }

                // 磁盘 IO
                try
                {
                    _diskReadHandle = _pdh.AddEnglish(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
                    _diskWriteHandle = _pdh.AddEnglish(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
                }
                catch (Exception ex) { Debug.WriteLine($"disk counter: {ex.Message}"); }

                _pdh.Collect(); // 预热，首个采样值有效
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"pdh init: {ex.Message}");
        }

        // ---- 网络基线（差分法需一次初始读数，避免首帧速率异常）----
        CollectNet();
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
            // 先尝试枚举新句柄；若通配展开返回空（驱动响应慢/实例名变化）则保留旧句柄，
            // 避免 ultra5 等硬件上 GPU 负载因一次枚举失败就掉零。
            var newHandles = new List<IntPtr>();
            foreach (var p in PdhInterop.ExpandWildCard(@"\GPU Engine(*)\Utilization Percentage"))
            {
                var h = _pdh.AddEnglish(p);
                if (h != IntPtr.Zero) newHandles.Add(h);
            }
            if (newHandles.Count > 0)
            {
                // 枚举成功，替换旧句柄
                foreach (var h in _gpuEngineHandles) _pdh.Remove(h);
                _gpuEngineHandles.Clear();
                _gpuEngineHandles.AddRange(newHandles);
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

        // 网络（NetworkInterface 差分法，兼容所有硬件/本地化环境）
        var (downBps, upBps) = CollectNet();
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

        // GPU 负载：LHM → PDH → WMI 逐级降级。
        // LHM 直接读硬件寄存器最准确可靠；PDH 读 OS 计数器可能卡脏数据。
        double gpuLoad = 0;

        // 1) LHM GPU Core Load（硬件直读，只取 Core 排除 Memory 等非核心传感器）
        if (_gpu != null)
        {
            foreach (var s in EnumSensors(_gpu))
            {
                if (s.SensorType == SensorType.Load && s.Value.HasValue && s.Value.Value > 0
                    && s.Name is string n && n.Contains("Core"))
                {
                    gpuLoad = Math.Max(gpuLoad, s.Value.Value);
                }
            }
        }

        // 2) PDH GPU Engine（LHM 不认 GPU 时兜底，如 Intel Arc 130T）
        if (gpuLoad <= 0)
        {
            double pdhGpu = 0;
            foreach (var h in _gpuEngineHandles)
            {
                double g = SafeRead(h);
                if (!double.IsNaN(g)) pdhGpu = Math.Max(pdhGpu, g);
            }
            gpuLoad = pdhGpu;
        }

        // 3) WMI GPU Engine（PDH 也失败时兜底）
        if (gpuLoad <= 0)
        {
            double wmiGpu = GetGpuViaWmi();
            if (wmiGpu > 0) gpuLoad = wmiGpu;
        }

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

    /// <summary>
    /// 使用 NetworkInterface 差分法计算网络上下行速率（bytes/s）。
    /// 不依赖 PDH 计数器名称/类别，兼容各类硬件、网卡命名与本地化环境。
    /// 采样间隔由调用方（Collect 的周期）决定，本方法按真实经过时间换算。
    /// </summary>
    private (double downBps, double upBps) CollectNet()
    {
        long down = 0, up = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // 仅统计活动且非环回的网卡；不按名称过滤，保证跨硬件兼容
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    var s = nic.GetIPStatistics();
                    if (s != null)
                    {
                        down += s.BytesReceived;
                        up += s.BytesSent;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"nic stats({nic.Name}): {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"net enum: {ex.Message}");
        }

        long now = Stopwatch.GetTimestamp();
        double downBps = 0, upBps = 0;
        if (_netLastTimestamp >= 0)
        {
            double elapsed = (now - _netLastTimestamp) / (double)Stopwatch.Frequency;
            if (elapsed > 0)
            {
                double d = (down - _netLastDownBytes) / elapsed;
                double u = (up - _netLastUpBytes) / elapsed;
                // 计数器回绕 / 网卡重置 / 首帧：钳到非负
                downBps = d < 0 ? 0 : d;
                upBps = u < 0 ? 0 : u;
            }
        }
        _netLastDownBytes = down;
        _netLastUpBytes = up;
        _netLastTimestamp = now;
        return (downBps, upBps);
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
