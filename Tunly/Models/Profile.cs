using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Text.Json.Serialization;
using Windows.UI;

namespace Tunly.Models;

public static class StatusBrushes
{
    public static readonly SolidColorBrush Success = new(Color.FromArgb(255, 82, 196, 133));
    public static readonly SolidColorBrush Warning = new(Color.FromArgb(255, 245, 176, 65));
    public static readonly SolidColorBrush Danger = new(Color.FromArgb(255, 240, 101, 110));
    public static readonly SolidColorBrush Muted = new(Color.FromArgb(255, 150, 150, 150));
    public static readonly SolidColorBrush Accent = new(Color.FromArgb(255, 96, 165, 250));
}

/// <summary>Profile stored in profiles.json. Latency is a runtime value refreshed by "Test latency".</summary>
public sealed class Profile : Core.ObservableObject
{
    string _name = "";
    string _address = "";
    int _port = 443;
    string _protocol = "VLESS";
    string _transport = "TCP";
    bool _tls = true;
    string _sni = "";
    int? _latency;
    string _latencyMode = "";
    bool _active;

    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Secret { get; set; } = "";

    // ---------------- Protocol details ----------------
    public string Uuid { get; set; } = "";
    public string Method { get; set; } = "";
    public string Flow { get; set; } = "";
    public string TransportPath { get; set; } = "/";
    public string TransportHost { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Alpn { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public bool Insecure { get; set; }
    public string RealityPublicKey { get; set; } = "";
    public string RealityShortId { get; set; } = "";
    public string ObfsType { get; set; } = "";
    public string ObfsPassword { get; set; } = "";

    /// <summary>Raw sing-box outbound JSON; when set it replaces the generated outbound.</summary>
    public string RawOutbound { get; set; } = "";

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(Initial)); }
    }

    public string Protocol
    {
        get => _protocol;
        set => SetProperty(ref _protocol, value);
    }

    public string Address
    {
        get => _address;
        set { if (SetProperty(ref _address, value)) OnPropertyChanged(nameof(Endpoint)); }
    }

    public int Port
    {
        get => _port;
        set { if (SetProperty(ref _port, value)) OnPropertyChanged(nameof(Endpoint)); }
    }

    public string Transport
    {
        get => _transport;
        set { if (SetProperty(ref _transport, value)) OnPropertyChanged(nameof(TransportText)); }
    }

    public bool Tls
    {
        get => _tls;
        set { if (SetProperty(ref _tls, value)) OnPropertyChanged(nameof(TransportText)); }
    }

    public string Sni
    {
        get => _sni;
        set => SetProperty(ref _sni, value);
    }

    public int? LatencyMs
    {
        get => _latency;
        set
        {
            if (SetProperty(ref _latency, value))
            {
                OnPropertyChanged(nameof(LatencyText));
                OnPropertyChanged(nameof(LatencyBrush));
            }
        }
    }

    [JsonIgnore]
    public bool IsActive
    {
        get => _active;
        set
        {
            if (SetProperty(ref _active, value))
            {
                OnPropertyChanged(nameof(ActiveMarkVisibility));
                OnPropertyChanged(nameof(ActiveMarkOpacity));
            }
        }
    }

    /// <summary>How the current latency figure was measured: "TcpPing", "RealDelay" or "" (untested). Runtime value.</summary>
    [JsonIgnore]
    public string LatencyMode
    {
        get => _latencyMode;
        set => SetProperty(ref _latencyMode, value ?? "");
    }

    [JsonIgnore] public string Endpoint => string.IsNullOrWhiteSpace(Address) ? "Not configured" : Port > 0 ? $"{Address}:{Port}" : Address;
    [JsonIgnore] public string TransportText => Tls ? $"{Transport} · TLS" : Transport;
    [JsonIgnore] public string LatencyText => LatencyMs is int ms ? $"{ms} ms" : "—";

    /// <summary>Human explanation of the latency figure, used as the row tooltip.</summary>
    [JsonIgnore]
    public string LatencyModeText => LatencyMs is null
        ? "Not tested yet."
        : LatencyMode switch
        {
            "RealDelay" => "Real delay — an HTTP request was pushed through the whole tunnel. Honest for Cloudflare/CDN IPs and proves the server is alive.",
            "TcpPing" => $"TCP ping — direct socket connect to {Endpoint}. For Cloudflare/CDN IPs this measures the nearest edge node, not the server behind it.",
            _ => "Latency measured with an unknown method."
        };
    [JsonIgnore] public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    [JsonIgnore]
    public Brush LatencyBrush => LatencyMs switch
    {
        null => StatusBrushes.Muted,
        <= 120 => StatusBrushes.Success,
        <= 260 => StatusBrushes.Warning,
        _ => StatusBrushes.Danger
    };

    [JsonIgnore] public Visibility ActiveMarkVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore] public double ActiveMarkOpacity => IsActive ? 1 : 0;

    public void CopyFrom(Profile other)
    {
        Name = other.Name;
        Protocol = other.Protocol;
        Address = other.Address;
        Port = other.Port;
        Secret = other.Secret;
        Transport = other.Transport;
        Tls = other.Tls;
        Sni = other.Sni;
        LatencyMs = other.LatencyMs;
        LatencyMode = other.LatencyMode;

        Uuid = other.Uuid;
        Method = other.Method;
        Flow = other.Flow;
        TransportPath = other.TransportPath;
        TransportHost = other.TransportHost;
        ServiceName = other.ServiceName;
        Alpn = other.Alpn;
        Fingerprint = other.Fingerprint;
        Insecure = other.Insecure;
        RealityPublicKey = other.RealityPublicKey;
        RealityShortId = other.RealityShortId;
        ObfsType = other.ObfsType;
        ObfsPassword = other.ObfsPassword;
        RawOutbound = other.RawOutbound;
    }

    public Profile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Protocol = Protocol,
        Address = Address,
        Port = Port,
        Secret = Secret,
        Transport = Transport,
        Tls = Tls,
        Sni = Sni,
        LatencyMs = LatencyMs,
        LatencyMode = LatencyMode,
        Uuid = Uuid,
        Method = Method,
        Flow = Flow,
        TransportPath = TransportPath,
        TransportHost = TransportHost,
        ServiceName = ServiceName,
        Alpn = Alpn,
        Fingerprint = Fingerprint,
        Insecure = Insecure,
        RealityPublicKey = RealityPublicKey,
        RealityShortId = RealityShortId,
        ObfsType = ObfsType,
        ObfsPassword = ObfsPassword,
        RawOutbound = RawOutbound
    };
}
