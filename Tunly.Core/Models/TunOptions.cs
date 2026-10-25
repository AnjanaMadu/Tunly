namespace Tunly.Core.Models;

/// <summary>Engine options derived from the application settings.</summary>
public sealed record TunOptions
{
    public string LogLevel { get; init; } = "info";
    public string TunStack { get; init; } = "Mixed";

    public bool AutoRoute { get; init; } = true;
    public bool StrictRoute { get; init; }
    public bool Ipv6 { get; init; } = true;
    public bool Sniffing { get; init; } = true;
    public bool Multiplex { get; init; }
    public bool AllowLan { get; init; }
    public bool SystemProxy { get; init; }

    public int Mtu { get; init; } = 9000;
    public int MixedPort { get; init; } = 2080;
    public string InterfaceName { get; init; } = "tunly0";

    public DnsMode DnsMode { get; init; } = DnsMode.System;
    public string DnsServer { get; init; } = "1.1.1.1";
    public string DnsFallback { get; init; } = "8.8.8.8";

    public string CorePath { get; init; } = "";
    public string ConfigDirectory { get; init; } = "";
    public int ClashPort { get; init; }
}
