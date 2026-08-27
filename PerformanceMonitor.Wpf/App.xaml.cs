using System.Threading;
using System.Windows;

namespace PerformanceMonitor.Wpf;

public partial class App : Application
{
    private static Mutex _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\SystemMonitor.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("程序已在运行中", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (s, args) =>
        {
            args.Handled = true;
        };

        base.OnStartup(e);
        new MainWindow().Show();
    }
}
