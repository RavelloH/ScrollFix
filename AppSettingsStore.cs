using System.Text.Json;

namespace ScrollFix;

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsStore(string appName)
    {
        string appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);

        _settingsPath = Path.Combine(appDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(_settingsPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
