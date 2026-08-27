using System.IO;
using System.Text.Json;

namespace PerformanceMonitor.Wpf.Services;

public class MonitorConfig
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool? AlwaysOnTop { get; set; }
}

/// <summary>配置持久化：%LocalAppData%\SystemMonitor\monitor-config.json</summary>
public static class ConfigStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemMonitor");

    private static readonly string FilePath = Path.Combine(Dir, "monitor-config.json");

    public static MonitorConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<MonitorConfig>(File.ReadAllText(FilePath)) ?? new MonitorConfig();
        }
        catch
        {
        }
        return new MonitorConfig();
    }

    public static void Save(MonitorConfig config)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config));
        }
        catch
        {
        }
    }

    public static void Patch(Action<MonitorConfig> patch)
    {
        var cfg = Load();
        patch(cfg);
        Save(cfg);
    }
}
