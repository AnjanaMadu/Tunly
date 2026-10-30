using Tunly.Core.Models;
using Tunly.Models;

namespace Tunly.Services;

/// <summary>Maps the UI profile model to the engine's immutable outbound description.</summary>
public static class ProfileMapper
{
    public static OutboundSpec ToSpec(Profile profile)
    {
        var protocol = ParseProtocol(profile.Protocol);

        var uuid = protocol is ProtocolKind.Vless or ProtocolKind.Vmess or ProtocolKind.Tuic
            ? FirstNonEmpty(profile.Uuid, protocol is ProtocolKind.Tuic ? "" : profile.Secret)
            : "";

        var password = protocol switch
        {
            ProtocolKind.Trojan or ProtocolKind.Shadowsocks or ProtocolKind.Hysteria2 or ProtocolKind.Tuic
                => profile.Secret,
            _ => ""
        };

        var method = protocol == ProtocolKind.Shadowsocks && string.IsNullOrWhiteSpace(profile.Method)
            ? "aes-256-gcm"
            : profile.Method;

        return new OutboundSpec
        {
            Id = profile.Id,
            Name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Address : profile.Name,
            Protocol = protocol,
            Server = profile.Address.Trim(),
            Port = profile.Port,
            Uuid = uuid,
            Password = password,
            Method = method,
            Flow = profile.Flow,
            Transport = NormalizeTransport(profile.Transport),
            Path = string.IsNullOrWhiteSpace(profile.TransportPath) ? "/" : profile.TransportPath,
            Host = profile.TransportHost,
            ServiceName = profile.ServiceName,
            Tls = profile.Tls,
            ServerName = profile.Sni,
            Alpn = profile.Alpn,
            Fingerprint = profile.Fingerprint,
            Insecure = profile.Insecure,
            RealityPublicKey = profile.RealityPublicKey,
            RealityShortId = profile.RealityShortId,
            ObfsType = profile.ObfsType,
            ObfsPassword = profile.ObfsPassword,
            RawOutboundJson = profile.RawOutbound
        };
    }

    static ProtocolKind ParseProtocol(string value) => value.Trim().ToUpperInvariant() switch
    {
        "VLESS" => ProtocolKind.Vless,
        "VMESS" => ProtocolKind.Vmess,
        "TROJAN" => ProtocolKind.Trojan,
        "SHADOWSOCKS" or "SS" => ProtocolKind.Shadowsocks,
        "HYSTERIA2" or "HY2" => ProtocolKind.Hysteria2,
        "TUIC" => ProtocolKind.Tuic,
        _ => throw new EngineException($"Unsupported protocol \"{value}\".")
    };

    static string NormalizeTransport(string value) => value.Trim().ToUpperInvariant() switch
    {
        "WEBSOCKET" or "WS" => "WebSocket",
        "GRPC" => "gRPC",
        "HTTPUPGRADE" or "HTTP-UPGRADE" => "HTTPUpgrade",
        "QUIC" => "QUIC",
        _ => "TCP"
    };

    static string FirstNonEmpty(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;
}
