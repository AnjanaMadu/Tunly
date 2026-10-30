using Tunly.Core.Models;
using Tunly.Models;

namespace Tunly.Services;

/// <summary>Maps persisted app settings to the engine's immutable options.</summary>
public static class TunOptionsMapper
{
    public static TunOptions From(AppSettings settings) => new()
    {
        LogLevel = settings.LogLevel,
        TunStack = settings.TunStack,
        AutoRoute = settings.AutoRoute,
        StrictRoute = settings.StrictRoute,
        Ipv6 = settings.EnableIpv6,
        Sniffing = settings.Sniffing,
        Multiplex = settings.Multiplex,
        AllowLan = settings.AllowLan,
        SystemProxy = settings.SystemProxy,
        Mtu = Clamp((int)settings.Mtu, 576, 65535, 9000),
        MixedPort = Clamp((int)settings.MixedPort, 1, 65535, 2080),
        InterfaceName = "tunly0",
        DnsMode = settings.DnsMode switch
        {
            "Custom" => DnsMode.Custom,
            "DoH" => DnsMode.Doh,
            _ => DnsMode.System
        },
        DnsServer = settings.DnsServer,
        DnsFallback = settings.DnsFallback,
        CorePath = settings.CorePath,
        ConfigDirectory = settings.ConfigDirectory,
        ClashPort = Clamp(settings.ClashPort, 0, 65535, 0)
    };

    static int Clamp(int value, int min, int max, int fallback) =>
        value < min || value > max ? fallback : value;
}
