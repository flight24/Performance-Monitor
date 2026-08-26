using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PerformanceMonitor.Wpf.Services;

namespace PerformanceMonitor.Wpf;

public partial class MainWindow : Window
{
    // ---------- Win32：毛玻璃 / 圆角 ----------
    // 方案与 Weather-clock 天气插件一致：BLURBEHIND + 近透明渐变色，色调由 WPF 层自绘
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll")]
    private static extern bool SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    // ---------- 状态 ----------
    private readonly HardwareMonitorService _service = new();
    private bool _autoStartEnabled;
    private bool _pinEnabled;
    private bool _closing;
    private DispatcherTimer _saveTimer;

    public MainWindow()
    {
        InitializeComponent();

        var cfg = ConfigStore.Load();
        var work = SystemParameters.WorkArea;
        Left = cfg.X ?? work.Width - 300;
        Top = cfg.Y ?? 50;
        _pinEnabled = cfg.AlwaysOnTop ?? false;
        Topmost = _pinEnabled;

        LocationChanged += (_, _) => RestartSaveTimer();
        Loaded += OnLoaded;
        Closing += (_, _) => _closing = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyGlass(hwnd);
        SizeChanged += (_, _) => ApplyRoundCorners(hwnd);

        RefreshButtonVisuals();
        WireHoverEffects();
        RefreshAutoStartState();

        StartDataLoop();
    }

    // ================= 毛玻璃 =================

    /// <summary>
    /// 与 Weather-clock 天气插件相同的验证方案：
    /// DWM BLURBEHIND 负责模糊，深色调由 Root Border 自绘 rgba(17,17,34,.45)（对应原版 CSS）。
    /// </summary>
    private void ApplyGlass(IntPtr hwnd)
    {
        // 关键：让 WPF 渲染表面保留 alpha 通道。
        // 缺了这行 WPF 会把"透明"画成不透明黑色，模糊和半透明都会失效（只剩实心色块）。
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;

        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentEnableBlurBehind,
                AccentFlags = 0,
                GradientColor = 0x01000000,
                AnimationId = 0
            };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = ptr,
                    SizeOfData = Marshal.SizeOf<AccentPolicy>()
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            // 模糊不可用时仅剩半透明深色底（仍可读）
        }

        // DWM 原生圆角
        try
        {
            int round = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        }
        catch
        {
        }

        ApplyRoundCorners(hwnd);
    }

    private void ApplyRoundCorners(IntPtr hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out Rect r)) return;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int radius = Math.Max(1, (int)Math.Round(16 * scale));
            var region = CreateRoundRectRgn(0, 0, r.Right - r.Left + 1, r.Bottom - r.Top + 1, radius, radius);
            SetWindowRgn(hwnd, region, true);
        }
        catch
        {
        }
    }

    // ================= 数据循环 =================

    private async void StartDataLoop()
    {
        await Task.Run(() =>
        {
            try { _service.Initialize(); } catch { }
        });

        while (!_closing)
        {
            MonitorData d = null;
            try { d = _service.Collect(); } catch { }
            if (d != null)
            {
                var snapshot = d;
                await Dispatcher.InvokeAsync(() => Apply(snapshot), DispatcherPriority.Background);
                if (_closing) break;
            }
            await Task.Delay(1000);
        }
    }

    /// <summary>格式与原 index.html 渲染器逐项一致。</summary>
    private void Apply(MonitorData d)
    {
        CpuRing.Percent = d.Cpu;
        CpuRing.Title = double.IsNaN(d.CpuTemp) ? "CPU" : $"CPU {d.CpuTemp:F0}°C";
        string cpuSub = "";
        if (!double.IsNaN(d.CpuVoltage)) cpuSub += $"{d.CpuVoltage:F2}V ";
        if (d.CpuFreq > 0) cpuSub += $"{d.CpuFreq:F1}GHz";
        CpuRing.Sub = cpuSub.TrimEnd();

        GpuRing.Percent = d.Gpu;
        GpuRing.Title = double.IsNaN(d.GpuTemp) ? "GPU" : $"GPU {d.GpuTemp:F0}°C";
        GpuRing.Sub = !double.IsNaN(d.GpuMemUsed)
            ? (double.IsNaN(d.GpuMemTotal)
                ? $"{d.GpuMemUsed:F0}MB"
                : $"{d.GpuMemUsed:F0}/{d.GpuMemTotal:F0}MB")
            : "";

        MemRing.Percent = d.Mem;

        DiskRing.Percent = d.Disk;
        DiskRing.Sub = $"R:{d.DiskReadMb:F1} W:{d.DiskWriteMb:F1} MB/s";

        NetRing.Percent = d.NetPct;
        NetRing.Sub = $"↓{d.NetDownMbps:F1} ↑{d.NetUpMbps:F1} Mbps";
    }

    // ================= 按钮 =================

    private static readonly Brush IdleBg = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HoverBg = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ActiveBg = new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0xD2, 0xFF));
    private static readonly Brush IdleFg = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
    private static readonly Brush WhiteFg = Brushes.White;
    private static readonly Brush AccentFg = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));

    private void OnDragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnAutoStartClick(object sender, RoutedEventArgs e)
    {
        bool target = !_autoStartEnabled;
        bool ok = AutoStartService.Set(target);
        _autoStartEnabled = ok && target;
        RefreshButtonVisuals();
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _pinEnabled = !_pinEnabled;
        Topmost = _pinEnabled;
        ConfigStore.Patch(c => c.AlwaysOnTop = _pinEnabled);
        RefreshButtonVisuals();
    }

    private void RefreshAutoStartState()
    {
        Task.Run(() => AutoStartService.Get()).ContinueWith(t =>
        {
            if (t.Result == _autoStartEnabled) return;
            Dispatcher.Invoke(() =>
            {
                _autoStartEnabled = t.Result;
                RefreshButtonVisuals();
            });
        });
    }

    private void RefreshButtonVisuals()
    {
        // 关闭按钮悬停变红由 MouseEnter/Leave 处理；此处维护自启/置顶的激活态
        SetToggleVisual(AutoBtn, _autoStartEnabled);
        SetToggleVisual(PinBtn, _pinEnabled);
    }

    private static void SetToggleVisual(Button btn, bool active)
    {
        btn.Background = active ? ActiveBg : IdleBg;
        btn.Foreground = active ? AccentFg : IdleFg;
        btn.Tag = active;
    }

    private void WireHoverEffects()
    {
        // 关闭按钮：悬停变红
        CloseBtn.MouseEnter += (_, _) =>
        {
            CloseBtn.Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0x50, 0x50)); // rgba(255,80,80,.6)
            CloseBtn.Foreground = WhiteFg;
        };
        CloseBtn.MouseLeave += (_, _) =>
        {
            CloseBtn.Background = IdleBg;
            CloseBtn.Foreground = IdleFg;
        };

        // 自启 / 置顶：未激活时悬停提亮
        foreach (var btn in new[] { AutoBtn, PinBtn })
        {
            var b = btn;
            b.MouseEnter += (_, _) =>
            {
                if (!(b.Tag is true))
                {
                    b.Background = HoverBg;
                    b.Foreground = IdleFg;
                }
            };
            b.MouseLeave += (_, _) => SetToggleVisual(b, b.Tag is true);
        }
    }

    // ================= 配置持久化 =================

    private void RestartSaveTimer()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += SavePositionOnce;
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SavePositionOnce(object sender, EventArgs e)
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= SavePositionOnce;
        double x = Left, y = Top;
        ConfigStore.Patch(c => { c.X = x; c.Y = y; });
    }
}
