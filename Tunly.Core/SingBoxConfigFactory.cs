using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Tunly.Core.Models;

namespace Tunly.Core;

/// <summary>
/// Generates a complete sing-box configuration. Targets sing-box 1.12+ (typed DNS
/// servers and route actions) and adapts a few fields for 1.15+ where the old
/// options were removed.
/// </summary>
public static class SingBoxConfigFactory
{
    public const string SelectorTag = "proxy";
    public const string DirectTag = "direct";
    public const string TunInterfaceName = "tunly0";

    static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static string Serialize(JsonObject config) => config.ToJsonString(Indented);

    public static JsonObject Build(
        IReadOnlyList<OutboundSpec> profiles,
        string activeProfileId,
        TunOptions options,
        ClashEndpoint clash,
        CoreVersion version)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clash);
        ArgumentNullException.ThrowIfNull(version);

        if (profiles.Count == 0)
            throw new EngineException("No profiles are configured. Add a profile before connecting.");

        ValidateProfiles(profiles);

        var active = profiles.FirstOrDefault(p => p.Id == activeProfileId) ?? profiles[0];

        var root = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["level"] = NormalizeLogLevel(options.LogLevel),
                ["timestamp"] = true
            },
            ["dns"] = BuildDns(options),
            ["inbounds"] = BuildInbounds(options, version),
            ["outbounds"] = BuildOutbounds(profiles, active, options),
            ["route"] = BuildRoute(options),
            ["experimental"] = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = clash.Controller,
                    ["secret"] = clash.Secret
                },
                ["cache_file"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["path"] = "cache.db"
                }
            }
        };

        return root;
    }

    /// <summary>
    /// Builds a bare proxy-mode config for one profile: a local mixed inbound on
    /// the given port, that outbound, and nothing else. No TUN, no Clash API —
    /// used by the latency probe so a request can be pushed through the real
    /// tunnel without administrator rights.
    /// </summary>
    public static JsonObject BuildProbeConfig(OutboundSpec profile, int mixedPort)
    {
        var root = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["level"] = "error",
                ["timestamp"] = true
            },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray(new JsonObject
                {
                    ["type"] = "local",
                    ["tag"] = "local"
                }),
                ["final"] = "local"
            },
            ["inbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = "mixed-in",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = mixedPort
            }),
            ["outbounds"] = new JsonArray(
                BuildOutbound(profile, new TunOptions()),
                new JsonObject
                {
                    ["type"] = "direct",
                    ["tag"] = DirectTag
                }),
            ["route"] = new JsonObject
            {
                ["final"] = profile.Tag,
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "local"
            }
        };

        return root;
    }

    /// <summary>Validates every outbound and throws a message naming the profile.</summary>
    public static void ValidateProfiles(IReadOnlyList<OutboundSpec> profiles)
    {
        foreach (var profile in profiles)
        {
            var errors = profile.Validate();
            if (errors.Count > 0)
            {
                throw new EngineException(
                    $"Profile \"{profile.Name}\" cannot be used: {string.Join(", ", errors)}.");
            }
        }
    }

    static JsonArray BuildInbounds(TunOptions options, CoreVersion version)
    {
        var address = new JsonArray { "172.19.0.1/30" };
        if (options.Ipv6) address.Add("fdfe:dcba:9876::1/126");

        var tun = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["interface_name"] = string.IsNullOrWhiteSpace(options.InterfaceName)
                ? TunInterfaceName
                : options.InterfaceName,
            ["address"] = address,
            ["mtu"] = options.Mtu,
            ["auto_route"] = options.AutoRoute,
            ["strict_route"] = options.StrictRoute
        };

        // `stack` is deprecated in sing-box 1.15 and removed in 1.17.
        if (version.Major < 1 || (version.Major == 1 && version.Minor < 15))
        {
            tun["stack"] = options.TunStack.ToLowerInvariant() switch
            {
                "system" => "system",
                "gvisor" or "g visor" => "gvisor",
                _ => "mixed"
            };
        }

        var mixed = new JsonObject
        {
            ["type"] = "mixed",
            ["tag"] = "mixed-in",
            ["listen"] = options.AllowLan ? "0.0.0.0" : "127.0.0.1",
            ["listen_port"] = options.MixedPort
        };

        return new JsonArray { tun, mixed };
    }

    static JsonArray BuildOutbounds(IReadOnlyList<OutboundSpec> profiles, OutboundSpec active, TunOptions options)
    {
        var outbounds = new JsonArray();

        foreach (var profile in profiles)
            outbounds.Add(BuildOutbound(profile, options));

        var selectorOutbounds = new JsonArray();
        foreach (var profile in profiles) selectorOutbounds.Add(profile.Tag);
        selectorOutbounds.Add(DirectTag);

        outbounds.Add(new JsonObject
        {
            ["type"] = "selector",
            ["tag"] = SelectorTag,
            ["outbounds"] = selectorOutbounds,
            ["default"] = active.Tag
        });

        outbounds.Add(new JsonObject
        {
            ["type"] = "direct",
            ["tag"] = DirectTag
        });

        return outbounds;
    }

    static JsonObject BuildOutbound(OutboundSpec profile, TunOptions options)
    {
        if (!string.IsNullOrWhiteSpace(profile.RawOutboundJson))
            return BuildRawOutbound(profile);

        var outbound = new JsonObject
        {
            ["type"] = profile.Protocol switch
            {
                ProtocolKind.Vless => "vless",
                ProtocolKind.Vmess => "vmess",
                ProtocolKind.Trojan => "trojan",
                ProtocolKind.Shadowsocks => "shadowsocks",
                ProtocolKind.Hysteria2 => "hysteria2",
                ProtocolKind.Tuic => "tuic",
                _ => throw new EngineException($"Unsupported protocol for profile \"{profile.Name}\".")
            },
            ["tag"] = profile.Tag,
            ["server"] = profile.Server,
            ["server_port"] = profile.Port
        };

        switch (profile.Protocol)
        {
            case ProtocolKind.Vless:
                outbound["uuid"] = profile.Uuid;
                if (!string.IsNullOrWhiteSpace(profile.Flow))
                    outbound["flow"] = profile.Flow;
                AddTls(outbound, profile);
                AddTransport(outbound, profile);
                AddMultiplex(outbound, profile, options);
                break;

            case ProtocolKind.Vmess:
                outbound["uuid"] = profile.Uuid;
                outbound["security"] = "auto";
                outbound["alter_id"] = 0;
                AddTls(outbound, profile);
                AddTransport(outbound, profile);
                AddMultiplex(outbound, profile, options);
                break;

            case ProtocolKind.Trojan:
                outbound["password"] = profile.Password;
                AddTls(outbound, profile);
                AddTransport(outbound, profile);
                AddMultiplex(outbound, profile, options);
                break;

            case ProtocolKind.Shadowsocks:
                outbound["method"] = string.IsNullOrWhiteSpace(profile.Method)
                    ? "aes-256-gcm"
                    : profile.Method;
                outbound["password"] = profile.Password;
                break;

            case ProtocolKind.Hysteria2:
                outbound["password"] = profile.Password;
                AddTls(outbound, profile, forceHttp3: true);
                if (!string.IsNullOrWhiteSpace(profile.ObfsType) &&
                    !string.IsNullOrWhiteSpace(profile.ObfsPassword))
                {
                    outbound["obfs"] = new JsonObject
                    {
                        ["type"] = profile.ObfsType,
                        ["password"] = profile.ObfsPassword
                    };
                }
                break;

            case ProtocolKind.Tuic:
                outbound["uuid"] = profile.Uuid;
                outbound["password"] = profile.Password;
                outbound["congestion_control"] = "bbr";
                AddTls(outbound, profile, forceHttp3: true);
                break;

            default:
                throw new EngineException($"Unsupported protocol for profile \"{profile.Name}\".");
        }

        return outbound;
    }

    static JsonObject BuildRawOutbound(OutboundSpec profile)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(profile.RawOutboundJson);
        }
        catch (JsonException ex)
        {
            throw new EngineException(
                $"Profile \"{profile.Name}\" has invalid raw JSON: {ex.Message}", ex);
        }

        if (node is not JsonObject raw)
            throw new EngineException($"Profile \"{profile.Name}\" raw JSON must be a JSON object.");

        // The tag is owned by the engine so selector switching stays stable.
        raw["tag"] = profile.Tag;
        return raw;
    }

    static void AddTls(JsonObject outbound, OutboundSpec profile, bool forceHttp3 = false)
    {
        // Hysteria2 and TUIC are TLS-only protocols.
        if (!profile.Tls && !forceHttp3) return;

        var tls = new JsonObject
        {
            ["enabled"] = true
        };

        var serverName = !string.IsNullOrWhiteSpace(profile.ServerName)
            ? profile.ServerName
            : !string.IsNullOrWhiteSpace(profile.Host)
                ? profile.Host
                : profile.Server;

        if (!string.IsNullOrWhiteSpace(serverName))
            tls["server_name"] = serverName;

        if (profile.Insecure)
            tls["insecure"] = true;

        if (forceHttp3)
        {
            tls["alpn"] = new JsonArray("h3");
        }
        else
        {
            var alpn = BuildAlpn(profile);
            if (alpn is not null) tls["alpn"] = alpn;
        }

        var fingerprint = profile.Fingerprint;
        if (profile.IsReality && string.IsNullOrWhiteSpace(fingerprint))
            fingerprint = "chrome";

        if (!string.IsNullOrWhiteSpace(fingerprint))
        {
            tls["utls"] = new JsonObject
            {
                ["enabled"] = true,
                ["fingerprint"] = fingerprint
            };
        }

        if (profile.IsReality)
        {
            var reality = new JsonObject
            {
                ["enabled"] = true,
                ["public_key"] = profile.RealityPublicKey
            };
            if (!string.IsNullOrWhiteSpace(profile.RealityShortId))
                reality["short_id"] = profile.RealityShortId;
            tls["reality"] = reality;
        }

        outbound["tls"] = tls;
    }

    static void AddTransport(JsonObject outbound, OutboundSpec profile)
    {
        var transport = NormalizeTransport(profile.Transport);
        if (transport is null) return;

        switch (transport)
        {
            case "ws":
            {
                var ws = new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = string.IsNullOrWhiteSpace(profile.Path) ? "/" : profile.Path
                };
                if (!string.IsNullOrWhiteSpace(profile.Host))
                {
                    ws["headers"] = new JsonObject { ["Host"] = profile.Host };
                }
                outbound["transport"] = ws;
                break;
            }
            case "grpc":
            {
                var grpc = new JsonObject { ["type"] = "grpc" };
                if (!string.IsNullOrWhiteSpace(profile.ServiceName))
                    grpc["service_name"] = profile.ServiceName;
                outbound["transport"] = grpc;
                break;
            }
            case "httpupgrade":
            {
                var upgrade = new JsonObject
                {
                    ["type"] = "httpupgrade",
                    ["path"] = string.IsNullOrWhiteSpace(profile.Path) ? "/" : profile.Path
                };
                if (!string.IsNullOrWhiteSpace(profile.Host))
                    upgrade["host"] = profile.Host;
                outbound["transport"] = upgrade;
                break;
            }
            case "quic":
                outbound["transport"] = new JsonObject { ["type"] = "quic" };
                break;
        }
    }

    static string? NormalizeTransport(string transport) => transport.Trim().ToLowerInvariant() switch
    {
        "websocket" or "ws" => "ws",
        "grpc" => "grpc",
        "httpupgrade" or "http-upgrade" => "httpupgrade",
        "quic" => "quic",
        _ => null
    };

    static void AddMultiplex(JsonObject outbound, OutboundSpec profile, TunOptions options)
    {
        if (!options.Multiplex) return;

        // Stream-based multiplexing does not apply to QUIC-based protocols.
        if (profile.Protocol is ProtocolKind.Hysteria2 or ProtocolKind.Tuic) return;
        if (NormalizeTransport(profile.Transport) == "quic") return;

        outbound["multiplex"] = new JsonObject
        {
            ["enabled"] = true,
            ["protocol"] = "h2mux"
        };
    }

    static JsonObject BuildDns(TunOptions options)
    {
        var servers = new JsonArray();
        string final;

        switch (options.DnsMode)
        {
            case DnsMode.System:
                servers.Add(new JsonObject
                {
                    ["type"] = "local",
                    ["tag"] = "local"
                });
                final = "local";
                break;

            case DnsMode.Doh:
            {
                var remote = ParseDnsServer(
                    string.IsNullOrWhiteSpace(options.DnsServer) ? "1.1.1.1" : options.DnsServer,
                    preferHttps: true);
                remote["tag"] = "remote";
                remote["detour"] = SelectorTag;
                servers.Add(remote);
                servers.Add(new JsonObject { ["type"] = "local", ["tag"] = "local" });
                final = "remote";
                break;
            }

            default:
            {
                var remote = ParseDnsServer(
                    string.IsNullOrWhiteSpace(options.DnsServer) ? "1.1.1.1" : options.DnsServer,
                    preferHttps: false);
                remote["tag"] = "remote";
                remote["detour"] = SelectorTag;
                servers.Add(remote);

                var fallback = ParseDnsServer(
                    string.IsNullOrWhiteSpace(options.DnsFallback) ? "8.8.8.8" : options.DnsFallback,
                    preferHttps: false);
                fallback["tag"] = "local";
                servers.Add(fallback);
                final = "remote";
                break;
            }
        }

        return new JsonObject
        {
            ["servers"] = servers,
            ["final"] = final,
            ["strategy"] = options.Ipv6 ? "prefer_ipv4" : "ipv4_only"
        };
    }

    /// <summary>Converts a DNS server setting into a typed DNS server object.</summary>
    public static JsonObject ParseDnsServer(string value, bool preferHttps)
    {
        var text = value.Trim();

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            var type = uri.Scheme.ToLowerInvariant() switch
            {
                "https" => "https",
                "h3" => "h3",
                "tls" => "tls",
                "quic" => "quic",
                "udp" => "udp",
                "tcp" => "tcp",
                _ => preferHttps ? "https" : "udp"
            };

            var server = new JsonObject
            {
                ["type"] = type,
                ["server"] = uri.Host
            };

            if (!uri.IsDefaultPort)
                server["server_port"] = uri.Port;

            if (type is "https" or "h3")
            {
                var path = uri.AbsolutePath;
                if (!string.IsNullOrWhiteSpace(path) && path != "/")
                    server["path"] = path;
            }

            return server;
        }

        // Strip an optional "host:port" suffix, but never treat an IPv6 literal as host:port.
        var host = text;
        int? port = null;

        if (!IPAddress.TryParse(text.Trim('[', ']'), out var address))
        {
            var colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text[(colon + 1)..], out var parsedPort))
            {
                host = text[..colon];
                port = parsedPort;
            }
        }
        else
        {
            host = address.ToString();
        }

        var result = new JsonObject
        {
            ["type"] = preferHttps ? "https" : "udp",
            ["server"] = host
        };

        if (port is not null) result["server_port"] = port;
        return result;
    }

    /// <summary>
    /// Filters the user-supplied ALPN list to what the transport can actually use.
    /// WebSocket/HTTPUpgrade run over HTTP/1.1, gRPC over HTTP/2 and QUIC over h3;
    /// offering h3/h2 for a TCP WebSocket makes servers negotiate a protocol the
    /// upgrade cannot use.
    /// </summary>
    static JsonArray? BuildAlpn(OutboundSpec profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Alpn)) return null;

        var requested = profile.Alpn
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToList();

        if (requested.Count == 0) return null;

        var allowed = NormalizeTransport(profile.Transport) switch
        {
            "ws" or "httpupgrade" => requested.Where(item =>
                item.Equals("http/1.1", StringComparison.OrdinalIgnoreCase)).ToList(),
            "grpc" => requested.Where(item =>
                item.Equals("h2", StringComparison.OrdinalIgnoreCase)).ToList(),
            "quic" => requested.Where(item =>
                item.Equals("h3", StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => requested
        };

        if (allowed.Count == 0) return null;

        var array = new JsonArray();
        foreach (var item in allowed) array.Add(item);
        return array;
    }

    static JsonObject BuildRoute(TunOptions options)
    {
        var rules = new JsonArray();

        if (options.Sniffing)
            rules.Add(new JsonObject { ["action"] = "sniff" });

        rules.Add(new JsonObject
        {
            ["protocol"] = "dns",
            ["action"] = "hijack-dns"
        });

        rules.Add(new JsonObject
        {
            ["ip_is_private"] = true,
            ["outbound"] = DirectTag
        });

        return new JsonObject
        {
            ["rules"] = rules,
            ["final"] = SelectorTag,
            ["auto_detect_interface"] = true,
            ["default_domain_resolver"] = "local"
        };
    }

    static string NormalizeLogLevel(string level) => level.Trim().ToLowerInvariant() switch
    {
        "trace" => "trace",
        "debug" => "debug",
        "warn" or "warning" => "warn",
        "error" => "error",
        _ => "info"
    };
}
