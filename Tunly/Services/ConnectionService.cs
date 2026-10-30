using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tunly.Core;
using Tunly.Core.Models;
using Tunly.Models;

namespace Tunly.Services;

/// <summary>
/// UI-facing facade over the sing-box engine. Owns the connection state, live
/// traffic figures and the core process lifecycle.
/// </summary>
public sealed class ConnectionService : ObservableObject
{
    static readonly Regex Ansi = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    readonly DispatcherQueue _dispatcher;
    readonly DispatcherQueueTimer _uptimeTimer;
    readonly DispatcherQueueTimer _restartTimer;
    readonly EngineSession _engine = new();
    readonly SystemProxyController _systemProxy = new(AppPaths.ProxyBackupFile);
    readonly Stopwatch _uptime = new();

    ConnState _state = ConnState.Disconnected;
    Profile? _active;
    long _totalBytes;
    double _downRate, _upRate;
    double _lastTrafficSecond;
    string _errorMessage = "";
    bool _errorOpen;
    bool _restarting;
    bool _shuttingDown;
    volatile LogFileWriter? _logFile;

    public ConnectionService()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _uptimeTimer = _dispatcher.CreateTimer();
        _uptimeTimer.Interval = TimeSpan.FromSeconds(1);
        _uptimeTimer.IsRepeating = true;
        _uptimeTimer.Tick += (_, _) => UpdateUptime();

        _restartTimer = _dispatcher.CreateTimer();
        _restartTimer.Interval = TimeSpan.FromMilliseconds(800);
        _restartTimer.IsRepeating = false;
        _restartTimer.Tick += (_, _) =>
        {
            if (State == ConnState.Connected) _ = RestartAsync();
        };

        _engine.LogReceived += OnEngineLog;
        _engine.TrafficUpdated += (_, sample) => Post(() => ApplyTraffic(sample));
        _engine.Exited += (_, exit) => Post(() => OnEngineExited(exit));

        AppServices.Settings.PropertyChanged += OnSettingsChanged;

        _systemProxy.RestoreIfStale();
    }

    // ---------------- Identity / diagnostics ----------------
    public bool IsElevated { get; private set; }
    public string ElevationText => IsElevated ? "Administrator" : "Standard user";

    public string? CorePath { get; private set; }
    public CoreVersion? CoreVersion { get; private set; }
    public bool HasWintun { get; private set; }
    public string? ConfigPath { get; private set; }
    public string? ClashController { get; private set; }

    public string CoreStatusText { get; private set; } = "Detecting the core…";

    // ---------------- State ----------------
    public ConnState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(StateBrush));
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsConnecting));
                OnPropertyChanged(nameof(CanToggle));
                OnPropertyChanged(nameof(ConnectGlyph));
                OnPropertyChanged(nameof(IconVisibility));
                OnPropertyChanged(nameof(RingVisibility));
                OnPropertyChanged(nameof(StatusDetail));
            }
        }
    }

    public string StateText => State switch
    {
        ConnState.Connected => "Connected",
        ConnState.Connecting => "Connecting…",
        ConnState.Error => "Connection error",
        _ => "Disconnected"
    };

    public Brush StateBrush => State switch
    {
        ConnState.Connected => StatusBrushes.Success,
        ConnState.Connecting => StatusBrushes.Warning,
        ConnState.Error => StatusBrushes.Danger,
        _ => StatusBrushes.Muted
    };

    public string ConnectGlyph => State == ConnState.Connected ? "\uE71A" : "\uE7E8";

    public Visibility IconVisibility => IsConnecting ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RingVisibility => IsConnecting ? Visibility.Visible : Visibility.Collapsed;

    public bool RequiresElevation => !IsElevated;

    public bool IsConnected => State == ConnState.Connected;
    public bool IsConnecting => State == ConnState.Connecting;
    public bool CanToggle => State != ConnState.Connecting;

    public string StatusDetail => State switch
    {
        ConnState.Connected => "Traffic is routed through the TUN adapter.",
        ConnState.Connecting => "Validating the config and starting sing-box…",
        ConnState.Error => "Check the log for details, then try again.",
        _ => "Your traffic is not being routed through a proxy."
    };

    public Profile? ActiveProfile
    {
        get => _active;
        set
        {
            if (ReferenceEquals(_active, value)) return;

            if (_active is not null) _active.IsActive = false;
            _active = value;
            if (_active is not null) _active.IsActive = true;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveProfileName));
            OnPropertyChanged(nameof(ActiveProfileDetail));
            AppServices.Settings.LastProfileId = value?.Id ?? "";

            if (State == ConnState.Connected && value is not null)
                _ = SwitchProfileAsync(value);
        }
    }

    public string ActiveProfileName => ActiveProfile?.Name ?? "No profile selected";
    public string ActiveProfileDetail => ActiveProfile is null
        ? "Add a profile to get started"
        : $"{ActiveProfile.Protocol} · {ActiveProfile.TransportText} · {ActiveProfile.Endpoint}";

    // ---------------- Live figures ----------------
    public string DownloadText { get; private set; } = "0 KB/s";
    public string UploadText { get; private set; } = "0 KB/s";
    public string UptimeText { get; private set; } = "00:00:00";
    public string TotalText { get; private set; } = "0 B";

    public double DownloadRate => _downRate;
    public double UploadRate => _upRate;

    // ---------------- Errors ----------------
    public bool HasError
    {
        get => _errorOpen;
        private set => SetProperty(ref _errorOpen, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public void ClearError()
    {
        HasError = false;
        if (State == ConnState.Error) State = ConnState.Disconnected;
    }

    // ---------------- Lifecycle ----------------
    public void RestoreSession()
    {
        IsElevated = SystemHelper.IsElevated();
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(RequiresElevation));

        var settings = AppServices.Settings;
        var profile = AppServices.Profiles.Items.FirstOrDefault(p => p.Id == settings.LastProfileId)
                      ?? AppServices.Profiles.Items.FirstOrDefault();

        ActiveProfile = profile;

        AppServices.Logs.Info("Tunly started.");
        _ = DetectCoreAsync();

        if (settings.AutoConnect && profile is not null)
        {
            _ = ConnectAsync();
        }
    }

    async Task DetectCoreAsync()
    {
        try
        {
            var location = CoreLocator.Locate(AppServices.Settings.CorePath);
            if (location is null)
            {
                CorePath = null;
                CoreVersion = null;
                HasWintun = false;
                CoreStatusText = "Core not found. Set the path in Settings → Core & logs.";
                NotifyCoreChanged();
                AppServices.Logs.Warn("sing-box was not found in Settings, PATH or %LOCALAPPDATA%\\Tunly\\core.");
                return;
            }

            CorePath = location.ExecutablePath;
            HasWintun = location.HasWintun;

            var version = await CoreLocator.ReadVersionAsync(location.ExecutablePath);
            CoreVersion = version;
            CoreStatusText = $"sing-box {version} · {location.ExecutablePath}";
            NotifyCoreChanged();

            AppServices.Logs.Info($"Core: sing-box {version} ({location.ExecutablePath})");

            if (!version.AtLeast(1, 12))
                AppServices.Logs.Warn($"sing-box {version} is too old; version 1.12 or newer is required.");
        }
        catch (Exception ex)
        {
            CoreStatusText = $"Core unavailable: {ex.Message}";
            NotifyCoreChanged();
            AppServices.Logs.Warn($"Could not inspect the core: {ex.Message}");
        }
    }

    public async Task ToggleAsync()
    {
        if (_restarting || State == ConnState.Connecting) return;

        if (State == ConnState.Connected)
        {
            await DisconnectAsync();
            return;
        }

        ClearError();
        await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (State is ConnState.Connected or ConnState.Connecting || _shuttingDown) return;

        var profile = ActiveProfile;
        if (profile is null)
        {
            Fail("No profile selected. Add or select a profile first.");
            return;
        }

        ClearError();

        if (!SystemHelper.IsElevated())
        {
            Fail("Administrator rights are required to create the TUN adapter. Restart Tunly as administrator.");
            return;
        }

        State = ConnState.Connecting;
        AppServices.Logs.Info($"Connecting to {profile.Name} ({profile.Endpoint})…");

        var specs = BuildOutboundSpecs(profile);
        if (specs is null) return;

        _logFile?.Dispose();
        _logFile = new LogFileWriter(AppPaths.LogFile);

        try
        {
            var options = TunOptionsMapper.From(AppServices.Settings);
            await _engine.StartAsync(new EngineStartRequest(specs, profile.Id, options));

            CorePath = _engine.CorePath;
            CoreVersion = _engine.Version;
            HasWintun = _engine.HasWintun;
            ConfigPath = _engine.ConfigPath;
            ClashController = _engine.Endpoint?.Controller;
            CoreStatusText = $"sing-box {_engine.Version} · {CorePath}";
            NotifyCoreChanged();

            if (options.SystemProxy)
            {
                try
                {
                    _systemProxy.Enable(options.MixedPort);
                    AppServices.Logs.Info($"System proxy enabled on 127.0.0.1:{options.MixedPort}.");
                }
                catch (Exception ex)
                {
                    AppServices.Logs.Warn($"Could not enable the system proxy: {ex.Message}");
                }
            }

            _totalBytes = 0;
            _lastTrafficSecond = 0;
            UpdateTrafficText(0, 0);
            TotalText = "0 B";
            UptimeText = "00:00:00";
            OnPropertyChanged(nameof(TotalText));
            _uptime.Restart();
            _uptimeTimer.Start();

            State = ConnState.Connected;
            AppServices.Logs.Info(
                $"Tunnel established via sing-box {_engine.Version} · Clash API {ClashController}");
        }
        catch (Exception ex)
        {
            try { await _engine.StopAsync(); }
            catch (Exception) { /* Already stopped. */ }

            _uptimeTimer.Stop();
            _systemProxy.Disable();
            Fail(ex.Message);
        }
    }

    /// <summary>Builds specs for every usable profile; fails when the active one is invalid.</summary>
    List<OutboundSpec>? BuildOutboundSpecs(Profile active)
    {
        var specs = new List<OutboundSpec>();
        var activeValid = false;

        foreach (var profile in AppServices.Profiles.Items)
        {
            try
            {
                var spec = ProfileMapper.ToSpec(profile);
                var errors = spec.Validate();
                if (errors.Count > 0)
                {
                    if (ReferenceEquals(profile, active))
                    {
                        Fail($"Profile \"{profile.Name}\" cannot be used: {string.Join(", ", errors)}.");
                        return null;
                    }

                    AppServices.Logs.Warn($"Skipping profile \"{profile.Name}\": {string.Join(", ", errors)}.");
                    continue;
                }

                specs.Add(spec);
                if (spec.Id == active.Id) activeValid = true;
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(profile, active))
                {
                    Fail($"Profile \"{profile.Name}\" cannot be used: {ex.Message}");
                    return null;
                }

                AppServices.Logs.Warn($"Skipping profile \"{profile.Name}\": {ex.Message}");
            }
        }

        if (!activeValid || specs.Count == 0)
        {
            Fail($"Profile \"{active.Name}\" cannot be used. Check the profile details.");
            return null;
        }

        return specs;
    }

    public async Task DisconnectAsync()
    {
        if (State == ConnState.Disconnected && !_engine.IsRunning) return;

        _uptimeTimer.Stop();
        _restartTimer.Stop();

        try
        {
            await _engine.StopAsync();
        }
        catch (Exception ex)
        {
            AppServices.Logs.Warn($"Could not stop the core cleanly: {ex.Message}");
        }

        _systemProxy.Disable();

        _uptime.Stop();
        _totalBytes = 0;
        _lastTrafficSecond = 0;
        UpdateTrafficText(0, 0);
        TotalText = "0 B";
        UptimeText = "00:00:00";
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(UptimeText));

        State = ConnState.Disconnected;

        _logFile?.Dispose();
        _logFile = null;

        AppServices.Logs.Info("Tunnel closed.");
    }

    public async Task ShutdownAsync()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        _uptimeTimer.Stop();
        _restartTimer.Stop();

        try { await _engine.StopAsync(); }
        catch (Exception) { /* Best effort. */ }

        _systemProxy.Disable();

        _logFile?.Dispose();
        _logFile = null;
    }

    // ---------------- Engine events ----------------
    void OnEngineLog(object? sender, EngineLog log)
    {
        _logFile?.Write(log.Raw);

        var message = Ansi.Replace(log.Message, "").Trim();
        if (message.Length == 0) return;

        Post(() => AppServices.Logs.Add(MapLevel(log.Level), message, "core"));
    }

    void OnEngineExited(EngineExit exit)
    {
        if (_shuttingDown || State is not (ConnState.Connected or ConnState.Connecting)) return;

        _uptimeTimer.Stop();
        _systemProxy.Disable();

        var message = $"sing-box stopped unexpectedly (exit code {exit.ExitCode}).";
        var tail = exit.RecentLines.TakeLast(6).ToArray();
        if (tail.Length > 0)
            message += Environment.NewLine + string.Join(Environment.NewLine, tail);

        _logFile?.Dispose();
        _logFile = null;

        Fail(message);
    }

    void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_shuttingDown) return;

        if (e.PropertyName == nameof(AppSettings.SystemProxy))
        {
            if (State != ConnState.Connected) return;

            try
            {
                if (AppServices.Settings.SystemProxy)
                    _systemProxy.Enable((int)AppServices.Settings.MixedPort);
                else
                    _systemProxy.Disable();
            }
            catch (Exception ex)
            {
                AppServices.Logs.Warn($"Could not update the system proxy: {ex.Message}");
            }

            return;
        }

        if (!IsConfigAffecting(e.PropertyName)) return;

        _restartTimer.Stop();
        _restartTimer.Start();
    }

    static bool IsConfigAffecting(string? property) => property is
        nameof(AppSettings.Mtu) or
        nameof(AppSettings.MixedPort) or
        nameof(AppSettings.AllowLan) or
        nameof(AppSettings.AutoRoute) or
        nameof(AppSettings.StrictRoute) or
        nameof(AppSettings.EnableIpv6) or
        nameof(AppSettings.Sniffing) or
        nameof(AppSettings.Multiplex) or
        nameof(AppSettings.TunStack) or
        nameof(AppSettings.DnsMode) or
        nameof(AppSettings.DnsServer) or
        nameof(AppSettings.DnsFallback) or
        nameof(AppSettings.LogLevel) or
        nameof(AppSettings.CorePath) or
        nameof(AppSettings.ClashPort);

    async Task RestartAsync()
    {
        if (_restarting || _shuttingDown || State != ConnState.Connected) return;

        _restarting = true;
        try
        {
            AppServices.Logs.Info("Applying settings changes…");
            await DisconnectAsync();
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            AppServices.Logs.Warn($"Could not apply the settings: {ex.Message}");
        }
        finally
        {
            _restarting = false;
        }
    }

    async Task SwitchProfileAsync(Profile profile)
    {
        if (!_engine.IsRunning || _restarting) return;

        try
        {
            await _engine.SwitchOutboundAsync(profile.Id);
            AppServices.Logs.Info($"Outbound switched to {profile.Name}.");
        }
        catch (Exception ex)
        {
            AppServices.Logs.Warn($"Could not switch the outbound: {ex.Message}");
            await RestartAsync();
        }
    }

    public async Task<int?> TestDelayAsync(string profileId)
    {
        if (State != ConnState.Connected) return null;
        return await _engine.TestDelayAsync(profileId);
    }

    // ---------------- Traffic ----------------
    void ApplyTraffic(TrafficSample sample)
    {
        if (State != ConnState.Connected) return;

        var now = _uptime.Elapsed.TotalSeconds;
        if (_lastTrafficSecond > 0)
        {
            var delta = now - _lastTrafficSecond;
            if (delta is > 0 and < 10)
                _totalBytes += (long)((sample.UpBytesPerSecond + sample.DownBytesPerSecond) * delta);
        }

        _lastTrafficSecond = now;
        UpdateTrafficText(sample.DownBytesPerSecond, sample.UpBytesPerSecond);
        TotalText = FormatBytes(_totalBytes);
        OnPropertyChanged(nameof(TotalText));
    }

    void UpdateTrafficText(double downBytesPerSecond, double upBytesPerSecond)
    {
        _downRate = downBytesPerSecond;
        _upRate = upBytesPerSecond;
        DownloadText = FormatRate(downBytesPerSecond);
        UploadText = FormatRate(upBytesPerSecond);
        OnPropertyChanged(nameof(DownloadText));
        OnPropertyChanged(nameof(UploadText));
        OnPropertyChanged(nameof(DownloadRate));
        OnPropertyChanged(nameof(UploadRate));
    }

    void UpdateUptime()
    {
        if (State != ConnState.Connected) return;

        var elapsed = _uptime.Elapsed;
        UptimeText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        OnPropertyChanged(nameof(UptimeText));
    }

    static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0.5) return "0 KB/s";
        return bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / (1024 * 1024):0.0} MB/s"
            : $"{bytesPerSecond / 1024:0} KB/s";
    }

    static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    // ---------------- Helpers ----------------
    void Fail(string message)
    {
        ErrorMessage = message;
        HasError = true;
        State = ConnState.Error;
        AppServices.Logs.Error(message);
    }

    void NotifyCoreChanged()
    {
        OnPropertyChanged(nameof(CorePath));
        OnPropertyChanged(nameof(CoreVersion));
        OnPropertyChanged(nameof(HasWintun));
        OnPropertyChanged(nameof(ConfigPath));
        OnPropertyChanged(nameof(ClashController));
        OnPropertyChanged(nameof(CoreStatusText));
    }

    static LogLevel MapLevel(EngineLogLevel level) => level switch
    {
        EngineLogLevel.Trace => LogLevel.Trace,
        EngineLogLevel.Debug => LogLevel.Debug,
        EngineLogLevel.Warn => LogLevel.Warn,
        EngineLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Info
    };

    void Post(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }
}
