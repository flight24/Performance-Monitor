using System.Diagnostics;
using System.Runtime.InteropServices;

string exe = FindExe();

static string FindExe()
{
    // 从当前目录向上查找 wpf\dist\PerformanceMonitor.exe（兼容仓库内任意位置运行）
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var cand = Path.Combine(dir.FullName, "wpf", "dist", "PerformanceMonitor.exe");
        if (File.Exists(cand)) return cand;
        cand = Path.Combine(dir.FullName, "dist", "PerformanceMonitor.exe");
        if (File.Exists(cand)) return cand;
        dir = dir.Parent;
    }
    Console.WriteLine("未找到 PerformanceMonitor.exe，请先运行 wpf\\build-wpf.ps1 编译");
    Environment.Exit(1);
    return "";
}

var white = new System.Windows.Forms.Form
{
    BackColor = System.Drawing.Color.White,
    StartPosition = System.Windows.Forms.FormStartPosition.Manual,
    Location = new System.Drawing.Point(100, 100),
    Size = new System.Drawing.Size(800, 600),
    TopMost = false
};
white.Show();
Pump();

var proc = Process.Start(exe);
Thread.Sleep(5000);
Pump();

IntPtr h = Native.FindWindowByPid((uint)proc.Id);
if (h == IntPtr.Zero)
{
    Console.WriteLine("widget not found");
    proc.Kill();
    return;
}

Native.SetWindowPos(h, new IntPtr(-1), 360, 235, 0, 0, 0x0003);
Thread.Sleep(1000);
Pump();
Native.SetWindowPos(h, new IntPtr(-1), 360, 235, 0, 0, 0x0003);
Thread.Sleep(500);
Pump();

Native.GetWindowRect(h, out Native.RECT r);
Console.WriteLine($"widget rect: {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}");

using var bmp = new System.Drawing.Bitmap(280, 330);
using (var g = System.Drawing.Graphics.FromImage(bmp))
    g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);

int[] xs = { 20, 250, 20, 250, 140, 8 };
int[] ys = { 60, 60, 300, 300, 20, 165 };
double sum = 0;
for (int i = 0; i < xs.Length; i++)
{
    var c = bmp.GetPixel(xs[i], ys[i]);
    double lum = (c.R + c.G + c.B) / 3.0;
    sum += lum;
    Console.WriteLine($"pixel({xs[i]},{ys[i]}) R={c.R} G={c.G} B={c.B} lum={lum:F0}");
}
Console.WriteLine($"AVG luminance: {sum / xs.Length:F0}  (opaque dark-blue ~25-45 | translucent over white ~90-150)");
bmp.Save(Path.Combine(Path.GetTempPath(), "widget-glass-test.png"), System.Drawing.Imaging.ImageFormat.Png);

try { proc.Kill(); } catch { }
white.Close();
Console.WriteLine("done");

static void Pump()
{
    System.Windows.Forms.Application.DoEvents();
    Thread.Sleep(50);
}

static class Native
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc cb, IntPtr lparam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr h, out RECT r);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);

    public static IntPtr FindWindowByPid(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == pid && IsWindowVisible(h) &&
                GetWindowRect(h, out RECT r) &&
                r.Right - r.Left > 100 && r.Bottom - r.Top > 100)
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
