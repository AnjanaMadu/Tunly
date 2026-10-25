namespace Tunly.Core.Models;

public enum ProtocolKind
{
    Vless,
    Vmess,
    Trojan,
    Shadowsocks,
    Hysteria2,
    Tuic
}

public enum DnsMode
{
    System,
    Doh,
    Custom
}
