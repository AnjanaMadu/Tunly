using System.Globalization;
using System.Text;
using System.Text.Json;
using Tunly.Core.Models;

namespace Tunly.Core;

/// <summary>Protocol-neutral result of parsing a share link.</summary>
public sealed record ProfileLink
{
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

    public bool Tls { get; init; } = true;
    public string ServerName { get; init; } = "";
    public string Alpn { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public bool Insecure { get; init; }

    public string RealityPublicKey { get; init; } = "";
    public string RealityShortId { get; init; } = "";

    public string ObfsType { get; init; } = "";
    public string ObfsPassword { get; init; } = "";
}

/// <summary>Parses real-world share links into <see cref="ProfileLink"/> values.</summary>
public static class ShareLinkParser
{
    static readonly string[] LineSeparators = ["\r\n", "\n", "\r"];

    public static ProfileLink? Parse(string text)
        => ParseMany(text).FirstOrDefault();

    public static IReadOnlyList<ProfileLink> ParseMany(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<ProfileLink>();

        var trimmed = text.Trim();
        var results = new List<ProfileLink>();

        foreach (var line in trimmed.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var item = line.Trim();
            if (item.Length == 0) continue;

            var parsed = ParseSingle(item);
            if (parsed is not null) results.Add(parsed);
        }

        if (results.Count == 0 && !trimmed.Contains('\n'))
        {
            // A subscription is often a base64 blob containing one link per line.
            var decoded = TryBase64(trimmed);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return ParseMany(decoded);
            }
        }

        return results;
    }

    static ProfileLink? ParseSingle(string text)
    {
        if (text.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
            return ParseVmess(text);

        if (text.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
            return ParseShadowsocks(text);

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;

        var protocol = uri.Scheme.ToLowerInvariant() switch
        {
            "vless" => ProtocolKind.Vless,
            "trojan" => ProtocolKind.Trojan,
            "hysteria2" or "hy2" => ProtocolKind.Hysteria2,
            "tuic" => ProtocolKind.Tuic,
            _ => (ProtocolKind?)null
        };

        if (protocol is null || string.IsNullOrWhiteSpace(uri.DnsSafeHost)) return null;

        var query = ParseQuery(uri.Query);
        var name = DisplayName(uri, protocol.Value);
        var server = CleanHost(uri.DnsSafeHost);

        var transport = NormalizeTransport(Get(query, "type"));
        var path = Get(query, "path") ?? "/";
        var host = CleanHost(Get(query, "host") ?? "");
        var serviceName = Get(query, "serviceName", "service_name") ?? "";

        var security = Get(query, "security") ?? "tls";
        var tls = !security.Equals("none", StringComparison.OrdinalIgnoreCase);
        var frequency = Get(query, "fp", "fingerprint") ?? "";

        var alpn = Get(query, "alpn") ?? "";

        var insecure = IsTrue(Get(query, "allowInsecure", "insecure"));

        var sni = Get(query, "sni") ?? Get(query, "peer") ?? (string.IsNullOrWhiteSpace(host) ? server : host);
        sni = CleanHost(sni.Trim());

        var userInfo = Uri.UnescapeDataString(uri.UserInfo);
        var (user, password) = SplitUserInfo(userInfo);

        var protocolKind = protocol.Value;

        return new ProfileLink
        {
            Name = name,
            Protocol = protocolKind,
            Server = server,
            Port = uri.Port > 0 ? uri.Port : 443,
            Uuid = protocolKind is ProtocolKind.Vless or ProtocolKind.Tuic ? user : "",
            Password = protocolKind switch
            {
                ProtocolKind.Trojan or ProtocolKind.Hysteria2 => userInfo,
                ProtocolKind.Tuic => password,
                _ => ""
            },
            Transport = transport,
            Path = path,
            Host = host,
            ServiceName = serviceName,
            Tls = tls,
            ServerName = sni,
            Alpn = alpn,
            Fingerprint = frequency,
            Insecure = insecure,
            Flow = Get(query, "flow") ?? "",
            RealityPublicKey = Get(query, "pbk", "publicKey", "public_key") ?? "",
            RealityShortId = Get(query, "sid", "shortId", "short_id") ?? "",
            ObfsType = Get(query, "obfs") ?? "",
            ObfsPassword = Get(query, "obfs-password", "obfs_password") ?? ""
        };
    }

    static ProfileLink? ParseVmess(string text)
    {
        var payload = text["vmess://".Length..].Trim();
        var decoded = TryBase64(payload);
        if (decoded is null) return null;

        try
        {
            using var document = JsonDocument.Parse(decoded);
            var root = document.RootElement;

            var server = CleanHost(GetString(root, "add") ?? "");
            if (string.IsNullOrWhiteSpace(server)) return null;

            var port = GetInt(root, "port") ?? 443;
            var uuid = GetString(root, "id") ?? "";
            var network = GetString(root, "net") ?? "tcp";
            var tlsValue = (GetString(root, "tls") ?? "").Trim();
            var tls = tlsValue is "tls" or "reality" ||
                      (bool.TryParse(tlsValue, out var flag) && flag);

            var path = GetString(root, "path") ?? "/";
            var host = CleanHost(GetString(root, "host") ?? "");
            var sni = CleanHost(GetString(root, "sni") ?? "");
            if (string.IsNullOrWhiteSpace(sni))
                sni = string.IsNullOrWhiteSpace(host) ? server : host;

            var insecure = IsTrue(GetString(root, "insecure")) || IsTrue(GetString(root, "allowInsecure"));

            var name = GetString(root, "ps");
            if (string.IsNullOrWhiteSpace(name))
                name = $"{server}:{port}";

            return new ProfileLink
            {
                Name = name,
                Protocol = ProtocolKind.Vmess,
                Server = server,
                Port = port,
                Uuid = uuid,
                Method = GetString(root, "scy") ?? "",
                Transport = NormalizeTransport(network),
                Path = path,
                Host = host,
                ServiceName = GetString(root, "path") ?? "",
                Tls = tls,
                ServerName = sni,
                Alpn = (GetString(root, "alpn") ?? "").Trim(),
                Fingerprint = (GetString(root, "fp") ?? "").Trim(),
                Insecure = insecure
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static ProfileLink? ParseShadowsocks(string text)
    {
        var rest = text["ss://".Length..];

        var hashIndex = rest.IndexOf('#');
        var name = hashIndex >= 0
            ? Uri.UnescapeDataString(rest[(hashIndex + 1)..])
            : "";
        if (hashIndex >= 0) rest = rest[..hashIndex];

        var queryIndex = rest.IndexOf('?');
        if (queryIndex >= 0)
            rest = rest[..queryIndex];

        string? method = null;
        string? password = null;
        string host;
        int port = 8388;

        var atIndex = rest.LastIndexOf('@');
        if (atIndex >= 0)
        {
            var userInfo = rest[..atIndex];
            var hostPort = rest[(atIndex + 1)..];

            var decodedUserInfo = TryBase64(userInfo) ?? Uri.UnescapeDataString(userInfo);
            var colon = decodedUserInfo.IndexOf(':');
            if (colon >= 0)
            {
                method = decodedUserInfo[..colon];
                password = decodedUserInfo[(colon + 1)..];
            }
            else
            {
                password = decodedUserInfo;
            }

            if (!TryParseHostPort(hostPort, out host, out port))
                return null;
        }
        else
        {
            var decoded = TryBase64(rest);
            if (decoded is null) return null;

            var innerAt = decoded.LastIndexOf('@');
            if (innerAt < 0) return null;

            var credentials = decoded[..innerAt];
            var hostPort = decoded[(innerAt + 1)..];

            var colon = credentials.IndexOf(':');
            if (colon >= 0)
            {
                method = credentials[..colon];
                password = credentials[(colon + 1)..];
            }
            else
            {
                password = credentials;
            }

            if (!TryParseHostPort(hostPort, out host, out port))
                return null;
        }

        if (string.IsNullOrWhiteSpace(host)) return null;
        if (string.IsNullOrWhiteSpace(name)) name = $"{host}:{port}";

        return new ProfileLink
        {
            Name = name,
            Protocol = ProtocolKind.Shadowsocks,
            Server = host,
            Port = port,
            Password = password ?? "",
            Method = method ?? "",
            Transport = "TCP",
            Tls = false,
            ServerName = host
        };
    }

    static bool TryParseHostPort(string value, out string host, out int port)
    {
        host = value;
        port = 8388;

        value = value.Trim();
        if (value.StartsWith('['))
        {
            var closing = value.IndexOf(']');
            if (closing < 0) return false;

            host = value[1..closing];
            if (closing + 1 < value.Length && value[closing + 1] == ':' &&
                int.TryParse(value[(closing + 2)..], out var parsed6))
            {
                port = parsed6;
            }

            return true;
        }

        var colon = value.LastIndexOf(':');
        if (colon < 0) return true;

        host = value[..colon];
        if (int.TryParse(value[(colon + 1)..], out var parsed))
            port = parsed;

        return !string.IsNullOrWhiteSpace(host);
    }

    static string DisplayName(Uri uri, ProtocolKind protocol)
    {
        if (!string.IsNullOrWhiteSpace(uri.Fragment))
        {
            var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        return $"{protocol} {uri.DnsSafeHost}";
    }

    /// <summary>Removes the brackets .NET keeps around IPv6 literals.</summary>
    static string CleanHost(string host)
    {
        var trimmed = host.Trim();
        return trimmed.StartsWith('[') && trimmed.EndsWith(']')
            ? trimmed[1..^1]
            : trimmed;
    }

    static (string User, string Password) SplitUserInfo(string userInfo)
    {
        if (string.IsNullOrEmpty(userInfo)) return ("", "");

        var colon = userInfo.IndexOf(':');
        return colon < 0
            ? (userInfo, "")
            : (userInfo[..colon], userInfo[(colon + 1)..]);
    }

    static string NormalizeTransport(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ws" or "websocket" => "WebSocket",
        "grpc" => "gRPC",
        "httpupgrade" or "http-upgrade" => "HTTPUpgrade",
        "quic" => "QUIC",
        "h2" or "http" => "TCP",
        _ => "TCP"
    };

    static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index <= 0) continue;

            var key = Uri.UnescapeDataString(pair[..index]);
            var value = Uri.UnescapeDataString(pair[(index + 1)..]).Trim();
            result[key] = value;
        }

        return result;
    }

    static string? Get(Dictionary<string, string> query, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    static bool IsTrue(string? value) =>
        value is not null &&
        (value.Equals("1", StringComparison.Ordinal) ||
         value.Equals("true", StringComparison.OrdinalIgnoreCase));

    static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null
            }
            : null;

    static int? GetInt(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    static string? TryBase64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2:
                normalized += "==";
                break;
            case 3:
                normalized += "=";
                break;
            case 1:
                return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(normalized);
            var text = Encoding.UTF8.GetString(bytes);
            return text.All(c => c == '\t' || c == '\r' || c == '\n' || c >= ' ')
                ? text
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
