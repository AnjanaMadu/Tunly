using System;
using System.IO;
using System.Text.Json;
using Tunly.Models;

namespace Tunly.Services;

public sealed class SettingsService
{
    readonly JsonSerializerOptions _json = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };

    public AppSettings Settings { get; } = new();

    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tunly");

    public string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), _json);
                if (loaded is not null) Settings.CopyFrom(loaded);
            }
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults.
        }

        Settings.PropertyChanged += (_, _) => Save();    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, _json));
        }
        catch
        {
            // Persisting preferences must never crash the app.
        }
    }

    public void Reset()
    {
        Settings.CopyFrom(new AppSettings());
        Save();
    }
}
