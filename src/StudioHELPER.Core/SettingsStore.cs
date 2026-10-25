using System.Text.Json;

namespace StudioHELPER.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();

    public SettingsStore()
    {
        Settings = Load();
        Localization.SetLanguage(Settings.Language);
    }

    public AppSettings Settings { get; }

    public static string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudioHELPER");

    public static string SettingsPath { get; } = Path.Combine(SettingsDirectory, "settings.json");

    public void Save()
    {
        lock (_sync)
        {
            Directory.CreateDirectory(SettingsDirectory);
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();

            string legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudioUSB", "settings.json");
            if (!File.Exists(legacyPath)) return new AppSettings();

            string legacyJson = File.ReadAllText(legacyPath);
            var migrated = new AppSettings
            {
                StudioUsb = JsonSerializer.Deserialize<StudioUsbSettings>(legacyJson) ?? new StudioUsbSettings()
            };
            using JsonDocument document = JsonDocument.Parse(legacyJson);
            if (document.RootElement.TryGetProperty("Language", out JsonElement language))
                migrated.Language = language.GetString() ?? "system";
            return migrated;
        }
        catch
        {
            return new AppSettings();
        }
    }
}
