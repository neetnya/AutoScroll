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
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
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
            string? dir = Path.GetDirectoryName(FilePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 设置写失败不影响滚动。
        }
    }
}
