namespace Tunly.Core.Models;

/// <summary>
/// Immutable description of one proxy outbound, independent of the WinUI model.
/// </summary>
public sealed record OutboundSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ProtocolKind Protocol { get; init; }
    public required string Server { get; init; }
    public int Port { get; init; } = 443;

    public string Uuid { get; init; } = "";
    public string Password { get; init; } = "";
    public string Method { get; init; } = "";
    public string Flow { get; init; } = "";

    public string Transport { get; init; } = "TCP";
    public string Path { get; init; } = "/";
    public string Host { get; init; } = "";
    public string ServiceName { get; init; } = "";

    public bool Tls { get; init; }
    public string ServerName { get; init; } = "";
    public string Alpn { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public bool Insecure { get; init; }

    public string RealityPublicKey { get; init; } = "";
    public string RealityShortId { get; init; } = "";

    public string ObfsType { get; init; } = "";
    public string ObfsPassword { get; init; } = "";

    public string RawOutboundJson { get; init; } = "";

    public string Tag => $"profile:{Id}";

    public string Endpoint => $"{Server}:{Port}";

    public bool IsReality => Tls && !string.IsNullOrWhiteSpace(RealityPublicKey);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Server))
            errors.Add("server address is empty");

        if (Port is < 1 or > 65535)
            errors.Add($"port {Port} is out of range");

        if (!string.IsNullOrWhiteSpace(RawOutboundJson))
            return errors;

        switch (Protocol)
        {
            case ProtocolKind.Vless:
            case ProtocolKind.Vmess:
                if (string.IsNullOrWhiteSpace(Uuid)) errors.Add("UUID is empty");
                break;
            case ProtocolKind.Trojan:
            case ProtocolKind.Hysteria2:
                if (string.IsNullOrWhiteSpace(Password)) errors.Add("password is empty");
                break;
            case ProtocolKind.Shadowsocks:
                if (string.IsNullOrWhiteSpace(Password)) errors.Add("password is empty");
                if (string.IsNullOrWhiteSpace(Method)) errors.Add("encryption method is empty");
                break;
            case ProtocolKind.Tuic:
                if (string.IsNullOrWhiteSpace(Uuid)) errors.Add("UUID is empty");
                if (string.IsNullOrWhiteSpace(Password)) errors.Add("password is empty");
                break;
        }

        if (IsReality && string.IsNullOrWhiteSpace(RealityShortId))
        {
            // short_id may legitimately be empty, so this is informational only.
        }

        return errors;
    }
}
