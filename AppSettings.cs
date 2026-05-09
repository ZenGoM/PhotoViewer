using System.Text.Json;

namespace PhotoViewer;

internal class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoViewer", "settings.json");

    public int SplitterDistance { get; set; } = 200;
    public int WindowWidth { get; set; } = 1100;
    public int WindowHeight { get; set; } = 700;
    public int WindowLeft { get; set; } = -1;
    public int WindowTop { get; set; } = -1;
    public int WindowState { get; set; } = (int)FormWindowState.Normal;
    public string SelectedFolder { get; set; } = string.Empty;
    public List<string> SearchFolders { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
