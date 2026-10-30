using Tunly.Core;
using System.Reflection;

namespace Tunly.Models;

/// <summary>
/// All user preferences. Every setter raises PropertyChanged, which the
/// SettingsService uses to persist the file automatically.
/// </summary>
public sealed class AppSettings : ObservableObject
{
    // ---------------- Appearance ----------------
    string _theme = "System";
    string _backdrop = "Mica";
    bool _compactPane;

    public string Theme { get => _theme; set => SetProperty(ref _theme, value); }
    public string Backdrop { get => _backdrop; set => SetProperty(ref _backdrop, value); }
    public bool CompactPane { get => _compactPane; set => SetProperty(ref _compactPane, value); }

    // ---------------- Window & startup ----------------
    bool _autoConnect = true;
    bool _rememberWindow = true;
    bool _closeToTray = true;
    bool _alwaysOnTop;
    bool _launchMaximized;

    public bool AutoConnect { get => _autoConnect; set => SetProperty(ref _autoConnect, value); }
    public bool RememberWindow { get => _rememberWindow; set => SetProperty(ref _rememberWindow, value); }
    public bool CloseToTray { get => _closeToTray; set => SetProperty(ref _closeToTray, value); }
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }
    public bool LaunchMaximized { get => _launchMaximized; set => SetProperty(ref _launchMaximized, value); }

    // ---------------- Connection engine ----------------
    string _tunStack = "Mixed";
    bool _autoRoute = true;
    bool _strictRoute;
    bool _enableIpv6 = true;
    bool _sniffing = true;
    bool _multiplex;
    bool _allowLan;
    bool _systemProxy;
    double _mtu = 9000;
    double _mixedPort = 2080;

    public string TunStack { get => _tunStack; set => SetProperty(ref _tunStack, value); }
    public bool AutoRoute { get => _autoRoute; set => SetProperty(ref _autoRoute, value); }
    public bool StrictRoute { get => _strictRoute; set => SetProperty(ref _strictRoute, value); }
    public bool EnableIpv6 { get => _enableIpv6; set => SetProperty(ref _enableIpv6, value); }
    public bool Sniffing { get => _sniffing; set => SetProperty(ref _sniffing, value); }
    public bool Multiplex { get => _multiplex; set => SetProperty(ref _multiplex, value); }
    public bool AllowLan { get => _allowLan; set => SetProperty(ref _allowLan, value); }
    public bool SystemProxy { get => _systemProxy; set => SetProperty(ref _systemProxy, value); }
    public double Mtu { get => _mtu; set => SetProperty(ref _mtu, value); }
    public double MixedPort { get => _mixedPort; set => SetProperty(ref _mixedPort, value); }

    // ---------------- Latency ----------------
    string _latencyTest = "TcpPing";

    public string LatencyTest { get => _latencyTest; set => SetProperty(ref _latencyTest, value); }

    // ---------------- DNS ----------------
    string _dnsMode = "System";
    string _dnsServer = "1.1.1.1";
    string _dnsFallback = "8.8.8.8";

    public string DnsMode
    {
        get => _dnsMode;
        set { if (SetProperty(ref _dnsMode, value)) OnPropertyChanged(nameof(IsCustomDns)); }
    }

    public string DnsServer { get => _dnsServer; set => SetProperty(ref _dnsServer, value); }
    public string DnsFallback { get => _dnsFallback; set => SetProperty(ref _dnsFallback, value); }
    public bool IsCustomDns => DnsMode == "Custom";

    // ---------------- Core ----------------
    string _logLevel = "Info";
    string _corePath = "";
    string _configDirectory = "";
    int _clashPort;

    public string LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }
    public string CorePath { get => _corePath; set => SetProperty(ref _corePath, value); }
    public string ConfigDirectory { get => _configDirectory; set => SetProperty(ref _configDirectory, value); }

    /// <summary>Clash API port. 0 lets the engine pick a free loopback port.</summary>
    public int ClashPort { get => _clashPort; set => SetProperty(ref _clashPort, value); }

    // ---------------- Session (not shown in the UI) ----------------
    string _lastProfileId = "";
    double _windowWidth = 1200;
    double _windowHeight = 800;
    bool _windowMaximized;

    public string LastProfileId { get => _lastProfileId; set => SetProperty(ref _lastProfileId, value); }
    public double WindowWidth { get => _windowWidth; set => SetProperty(ref _windowWidth, value); }
    public double WindowHeight { get => _windowHeight; set => SetProperty(ref _windowHeight, value); }
    public bool WindowMaximized { get => _windowMaximized; set => SetProperty(ref _windowMaximized, value); }

    public void CopyFrom(AppSettings other)
    {
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanRead && p.CanWrite) p.SetValue(this, p.GetValue(other));
        }
    }
}
