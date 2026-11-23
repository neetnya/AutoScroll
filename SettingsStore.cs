using System.IO;
using System.Text.Json;

namespace AutoScroll;

internal sealed class AppSettings
{
    public int Speed { get; set; } = ScrollService.DefaultSpeed;
    public int Amplitude { get; set; } = ScrollService.DefaultAmplitude;
    public int Direction { get; set; } = ScrollService.DefaultDirection;
    public double Left { get; set; }
    public double Top { get; set; }
    public bool HasPosition { get; set; }
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoScroll",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(FilePath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            if (!double.IsFinite(settings.Left) || !double.IsFinite(settings.Top))
            {
                settings.HasPosition = false;
                settings.Left = 0;
                settings.Top = 0;
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            if (!double.IsFinite(settings.Left) || !double.IsFinite(settings.Top))
            {
                settings.HasPosition = false;
                settings.Left = 0;
                settings.Top = 0;
            }

            string? dir = Path.GetDirectoryName(FilePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = FilePath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmpPath, FilePath, overwrite: true);
        }
        catch
        {
            // 设置写失败不影响滚动。
        }
    }
}
