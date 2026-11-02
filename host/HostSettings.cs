using System.Text.Json;

namespace CemuPhoneGamePad;

internal sealed class HostSettings
{
    public bool Audio { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStart { get; set; }
    public int FramesPerSecond { get; set; } = 30;
    public int JpegQuality { get; set; } = 70;
    public string ProfileFolder { get; set; } = "";
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CemuPhoneGamePad");
    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static HostSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(FilePath)) ?? new();
            settings.FramesPerSecond = settings.FramesPerSecond == 15 ? 15 : 30;
            settings.JpegQuality = Math.Clamp(settings.JpegQuality, 40, 90);
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
