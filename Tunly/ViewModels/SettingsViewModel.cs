using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tunly.Core;
using Tunly.Models;
using Tunly.Services;

namespace Tunly.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    string _statusText = "Settings are saved automatically.";

    public SettingsViewModel()
    {
        AppServices.Connection.PropertyChanged += OnConnectionChanged;
    }

    public AppSettings Settings => AppServices.Settings;

    public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];
    public string[] BackdropOptions { get; } = ["Mica", "Acrylic", "Solid"];
    public string[] TunStackOptions { get; } = ["System", "gVisor", "Mixed"];
    public string[] DnsModeOptions { get; } = ["System", "DoH", "Custom"];
    public string[] LogLevelOptions { get; } = ["Trace", "Debug", "Info", "Warn", "Error"];

    public string VersionText { get; } = $"Version {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
    public string RuntimeText { get; } = $".NET {Environment.Version}";
    public string OsText { get; } = Environment.OSVersion.VersionString;

    public string SettingsPath => AppServices.SettingsService.SettingsPath;
    public string AppDataPath => AppServices.SettingsService.AppDataDirectory;

    public string CoreStatusText => AppServices.Connection.CoreStatusText;
    public string ResolvedCorePath => AppServices.Connection.CorePath ?? "Not found";
    public string CoreVersionText => AppServices.Connection.CoreVersion?.ToString() ?? "—";
    public string ClashController => AppServices.Connection.ClashController ?? "—";
    public string ConfigPath => AppPaths.ResolveConfigFile(Settings.ConfigDirectory);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void NoteSaved(string message = "Settings saved.") => StatusText = message;

    public void OpenAppData() => SystemHelper.OpenFolder(AppServices.SettingsService.AppDataDirectory);

    public void OpenConfigFolder()
    {
        var directory = AppPaths.ResolveConfigDirectory(Settings.ConfigDirectory);
        Directory.CreateDirectory(directory);
        SystemHelper.OpenFolder(directory);
    }

    public void RevealConfig()
    {
        var directory = AppPaths.ResolveConfigDirectory(Settings.ConfigDirectory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "config.json");
        if (File.Exists(path))
            SystemHelper.OpenFile(Path.GetDirectoryName(path)!);
        else
            SystemHelper.OpenFolder(directory);
    }

    public void OpenLogFile()
    {
        Directory.CreateDirectory(AppPaths.AppDataDirectory);
        if (!File.Exists(AppPaths.LogFile)) File.WriteAllText(AppPaths.LogFile, "");
        SystemHelper.OpenFile(AppPaths.LogFile);
    }

    public string BuildConfigPreview()
    {
        if (AppServices.Profiles.Items.Count == 0)
            return "// Add a profile to generate a sing-box configuration.";

        var profile = AppServices.Connection.ActiveProfile
                      ?? AppServices.Profiles.Items[0];

        return ConfigBuilder.Build(profile, Settings);
    }

    public async Task RefreshCoreAsync()
    {
        StatusText = "Inspecting the core…";

        try
        {
            var location = CoreLocator.Locate(Settings.CorePath);
            if (location is null)
            {
                StatusText = "sing-box was not found. Set the path or download a build.";
                return;
            }

            var version = await CoreLocator.ReadVersionAsync(location.ExecutablePath);
            StatusText = $"sing-box {version} · {location.ExecutablePath}";
            AppServices.Logs.Info($"Core test: sing-box {version} ({location.ExecutablePath}).");
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            AppServices.Logs.Warn($"Core test failed: {ex.Message}");
        }
    }

    public async Task DownloadCoreAsync(IProgress<double>? progress = null)
    {
        StatusText = "Downloading the latest sing-box…";

        var reporter = new Progress<CoreInstallProgress>(update =>
        {
            progress?.Report(update.Fraction * 100);
            StatusText = $"{update.Phase}… {update.Fraction * 100:0}%";
        });

        var installer = new CoreInstaller();
        var result = await installer.InstallLatestAsync(AppPaths.CoreDirectory, reporter);

        Settings.CorePath = result.ExecutablePath;
        StatusText = $"Installed sing-box {result.Version}.";
        AppServices.Logs.Info($"Downloaded sing-box {result.Version} to {result.ExecutablePath}.");
    }

    public void CopyDiagnostics()
    {
        StatusText = $"Tunly {VersionText} · {RuntimeText} · {OsText}";
    }

    void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionService.CoreStatusText)
            or nameof(ConnectionService.CorePath)
            or nameof(ConnectionService.CoreVersion)
            or nameof(ConnectionService.ConfigPath)
            or nameof(ConnectionService.ClashController))
        {
            OnPropertyChanged(nameof(CoreStatusText));
            OnPropertyChanged(nameof(ResolvedCorePath));
            OnPropertyChanged(nameof(CoreVersionText));
            OnPropertyChanged(nameof(ClashController));
        }
    }
}
