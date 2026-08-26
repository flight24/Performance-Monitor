using PerformanceMonitor.Wpf.Services;

var svc = new HardwareMonitorService();
Console.WriteLine("== Initialize ==");
try { svc.Initialize(); Console.WriteLine("init ok"); }
catch (Exception ex) { Console.WriteLine("init EX: " + ex); }

for (int i = 0; i < 3; i++)
{
    Thread.Sleep(1200);
    try
    {
        var d = svc.Collect();
        Console.WriteLine(
            $"[{i}] cpu={d.Cpu:F1} freq={d.CpuFreq:F2}GHz temp={(double.IsNaN(d.CpuTemp) ? "-" : d.CpuTemp.ToString("F0"))}C " +
            $"volt={(double.IsNaN(d.CpuVoltage) ? "-" : d.CpuVoltage.ToString("F2"))} gpu={d.Gpu:F1} " +
            $"gpuT={(double.IsNaN(d.GpuTemp) ? "-" : d.GpuTemp.ToString("F0"))} " +
            $"gpuMem={(double.IsNaN(d.GpuMemUsed) ? "-" : d.GpuMemUsed.ToString("F0"))}/{(double.IsNaN(d.GpuMemTotal) ? "-" : d.GpuMemTotal.ToString("F0"))}MB " +
            $"mem={d.Mem:F1}% disk={d.Disk:F1}% r={d.DiskReadMb:F2} w={d.DiskWriteMb:F2}MB/s " +
            $"net↓={d.NetDownMbps:F2} ↑={d.NetUpMbps:F2} pct={d.NetPct:F1}%");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"collect [{i}] EX: {ex.Message}\n{ex.StackTrace}");
    }
}
