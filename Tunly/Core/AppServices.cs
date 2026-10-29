using Tunly.Models;
using Tunly.Services;

namespace Tunly.Core;

/// <summary>Composition root for the app's long-lived services.</summary>
public static class AppServices
{
    public static SettingsService SettingsService { get; } = new();
    public static ProfileService Profiles { get; } = new();
    public static LogService Logs { get; } = new();
    public static ConnectionService Connection { get; } = new();

    public static AppSettings Settings => SettingsService.Settings;

    static AppServices()
    {
        SettingsService.Load();
        Profiles.Load();
    }

    public static void EnsureInitialized() { }
}
