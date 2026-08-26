using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PerformanceMonitor.Wpf.Controls;

public partial class GaugeRing : UserControl
{
    // 2 * PI * r(42px) = 263.894 ; dash 单位是 StrokeThickness(7) 的倍数 → /7
    private const double CircUnits = 37.699;

    public static readonly DependencyProperty PercentProperty =
        DependencyProperty.Register(nameof(Percent), typeof(double), typeof(GaugeRing),
            new PropertyMetadata(0.0, OnPercentChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(GaugeRing),
            new PropertyMetadata("", OnTitleChanged));

    public static readonly DependencyProperty SubProperty =
        DependencyProperty.Register(nameof(Sub), typeof(string), typeof(GaugeRing),
            new PropertyMetadata("", OnSubChanged));

    public static readonly DependencyProperty RingStrokeProperty =
        DependencyProperty.Register(nameof(RingStroke), typeof(Brush), typeof(GaugeRing),
            new PropertyMetadata(Brushes.White, OnRingStrokeChanged));

    public static readonly DependencyProperty BgStrokeProperty =
        DependencyProperty.Register(nameof(BgStroke), typeof(Brush), typeof(GaugeRing),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))));

    public static readonly DependencyProperty ValueFontSizeProperty =
        DependencyProperty.Register(nameof(ValueFontSize), typeof(double), typeof(GaugeRing),
            new PropertyMetadata(22.0, (o, a) => ((GaugeRing)o).PctText.FontSize = (double)a.NewValue));

    public static readonly DependencyProperty TitleFontSizeProperty =
        DependencyProperty.Register(nameof(TitleFontSize), typeof(double), typeof(GaugeRing),
            new PropertyMetadata(10.0, (o, a) => ((GaugeRing)o).TitleText.FontSize = (double)a.NewValue));

    public static readonly DependencyProperty SubFontSizeProperty =
        DependencyProperty.Register(nameof(SubFontSize), typeof(double), typeof(GaugeRing),
            new PropertyMetadata(9.0, (o, a) => ((GaugeRing)o).SubText.FontSize = (double)a.NewValue));

    public static readonly DependencyProperty RingSizeProperty =
        DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(GaugeRing),
            new PropertyMetadata(100.0, OnRingSizeChanged));

    public GaugeRing()
    {
        InitializeComponent();
        Fg.Stroke = RingStroke;
        Bg.Stroke = BgStroke;
        PctText.FontSize = ValueFontSize;
        PctText.Foreground = RingStroke;
        TitleText.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        SubText.Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
        ApplyRingSize(RingSize);
    }

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Sub
    {
        get => (string)GetValue(SubProperty);
        set => SetValue(SubProperty, value);
    }

    public Brush RingStroke
    {
        get => (Brush)GetValue(RingStrokeProperty);
        set => SetValue(RingStrokeProperty, value);
    }

    public Brush BgStroke
    {
        get => (Brush)GetValue(BgStrokeProperty);
        set => SetValue(BgStrokeProperty, value);
    }

    public double ValueFontSize
    {
        get => (double)GetValue(ValueFontSizeProperty);
        set => SetValue(ValueFontSizeProperty, value);
    }

    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    public double SubFontSize
    {
        get => (double)GetValue(SubFontSizeProperty);
        set => SetValue(SubFontSizeProperty, value);
    }

    /// <summary>表盘整体尺寸（基准 100，矢量缩放，文字保持清晰）。</summary>
    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    private static void OnRingSizeChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        ((GaugeRing)o).ApplyRingSize((double)e.NewValue);
    }

    private void ApplyRingSize(double size)
    {
        if (size <= 0 || Math.Abs(size - 100) < 0.1)
        {
            RingGrid.LayoutTransform = Transform.Identity;
            RootGrid.Width = RootGrid.Height = 100;
            return;
        }
        double s = size / 100.0;
        RingGrid.LayoutTransform = new ScaleTransform(s, s);   // 只缩放圆环图形
        RootGrid.Width = RootGrid.Height = size;               // 布局占位按目标尺寸
    }

    private static void OnPercentChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        var ring = (GaugeRing)o;
        double pct = Math.Clamp((double)e.NewValue, 0, 100);
        double target = CircUnits * (1 - pct / 100);

        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ring.Fg.BeginAnimation(Ellipse.StrokeDashOffsetProperty, anim);
        ring.PctText.Text = Math.Round(pct) + "%";
    }

    private static void OnTitleChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        ((GaugeRing)o).TitleText.Text = (string)e.NewValue ?? "";
    }

    private static void OnSubChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        var ring = (GaugeRing)o;
        string text = (string)e.NewValue ?? "";
        ring.SubText.Text = text;
        ring.SubText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnRingStrokeChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        var ring = (GaugeRing)o;
        ring.Fg.Stroke = (Brush)e.NewValue;
        // 与原版 CSS 一致：百分比数字继承环形颜色
        ring.PctText.Foreground = (Brush)e.NewValue;
    }
}
