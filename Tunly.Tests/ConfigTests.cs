using System.Diagnostics;
using System.Text.Json.Nodes;
using Tunly.Core;
using Tunly.Core.Models;
using Xunit;

namespace Tunly.Tests;

public class ConfigTests
{
    static readonly ClashEndpoint Clash = new("127.0.0.1", 9099, "secret");
    static readonly CoreVersion V14 = new(1, 14, 4, "sing-box version 1.14.4");
    static readonly CoreVersion V15 = new(1, 15, 0, "sing-box version 1.15.0");

    static JsonObject Obj(JsonNode? node) => node!.AsObject();
    static JsonArray Arr(JsonNode? node) => node!.AsArray();
    static JsonObject OutboundAt(JsonObject config, int index) => Obj(Arr(config["outbounds"])[index]);

    static OutboundSpec Vless(string id = "a", string name = "A") => new()
    {
        Id = id,
        Name = name,
        Protocol = ProtocolKind.Vless,
        Server = "example.com",
        Port = 443,
        Uuid = "bf000d23-0752-40b4-affe-68f7707a9661",
        Transport = "WebSocket",
        Path = "/ws",
        Host = "cdn.example.com",
        Tls = true,
        ServerName = "cdn.example.com"
    };

    static OutboundSpec Vmess(string id = "b") => new()
    {
        Id = id,
        Name = "B",
        Protocol = ProtocolKind.Vmess,
        Server = "vmess.example.com",
        Port = 8443,
        Uuid = "bf000d23-0752-40b4-affe-68f7707a9661",
        Transport = "gRPC",
        ServiceName = "grpc",
        Tls = true
    };

    static OutboundSpec Trojan(string id = "c") => new()
    {
        Id = id,
        Name = "C",
        Protocol = ProtocolKind.Trojan,
        Server = "trojan.example.com",
        Port = 443,
        Password = "pass",
        Tls = true
    };

    static OutboundSpec Shadowsocks(string id = "d") => new()
    {
        Id = id,
        Name = "D",
        Protocol = ProtocolKind.Shadowsocks,
        Server = "ss.example.com",
        Port = 8388,
        Method = "2022-blake3-aes-128-gcm",
        Password = "8JCsPssfgS8tiRwiMlhARg=="
    };

    static OutboundSpec Hysteria2(string id = "e") => new()
    {
        Id = id,
        Name = "E",
        Protocol = ProtocolKind.Hysteria2,
        Server = "hy.example.com",
        Port = 8443,
        Password = "pass",
        Tls = true,
        ObfsType = "salamander",
        ObfsPassword = "obfs"
    };

    static OutboundSpec Tuic(string id = "f") => new()
    {
        Id = id,
        Name = "F",
        Protocol = ProtocolKind.Tuic,
        Server = "tuic.example.com",
        Port = 443,
        Uuid = "2DD61D93-75D8-4DA4-AC0E-6AECE7EAC365",
        Password = "pass",
        Tls = true
    };

    static TunOptions Options(DnsMode dns = DnsMode.System, bool ipv6 = true, bool sniffing = true) => new()
    {
        DnsMode = dns,
        Ipv6 = ipv6,
        Sniffing = sniffing,
        Mtu = 9000,
        MixedPort = 2080,
        AllowLan = false,
        Multiplex = false
    };

    static JsonObject Build(IReadOnlyList<OutboundSpec> profiles, TunOptions? options = null, CoreVersion? version = null) =>
        SingBoxConfigFactory.Build(profiles, profiles[0].Id, options ?? Options(), Clash, version ?? V14);

    [Fact]
    public void Selector_contains_all_profiles_and_direct()
    {
        var config = Build([Vless(), Vmess(), Trojan()]);
        var outbounds = Arr(config["outbounds"]);

        var selector = outbounds.OfType<JsonObject>()
            .Single(o => (string?)o["type"] == "selector");

        var names = Arr(selector["outbounds"]).Select(n => (string)n!).ToList();

        Assert.Contains("profile:a", names);
        Assert.Contains("profile:b", names);
        Assert.Contains("profile:c", names);
        Assert.Contains("direct", names);
        Assert.Equal("profile:a", (string?)selector["default"]);
        Assert.Equal("proxy", (string?)Obj(config["route"])["final"]);
    }

    [Fact]
    public void Tun_inbound_has_ipv6_only_when_enabled()
    {
        var tun = Arr(Build([Vless()], Options(ipv6: true))["inbounds"]).OfType<JsonObject>()
            .Single(i => (string?)i["type"] == "tun");
        Assert.Equal(2, Arr(tun["address"]).Count);

        var tun4 = Arr(Build([Vless()], Options(ipv6: false))["inbounds"]).OfType<JsonObject>()
            .Single(i => (string?)i["type"] == "tun");
        Assert.Single(Arr(tun4["address"]));
    }

    [Fact]
    public void Stack_is_emitted_before_1_15_and_omitted_after()
    {
        var before = Arr(Build([Vless()], version: V14)["inbounds"])[0]!;
        var after = Arr(Build([Vless()], version: V15)["inbounds"])[0]!;

        Assert.NotNull(Obj(before)["stack"]);
        Assert.Null(Obj(after)["stack"]);
    }

    [Fact]
    public void Sniff_rule_follows_setting()
    {
        var on = Arr(Build([Vless()], Options(sniffing: true))["route"]!["rules"]);
        var off = Arr(Build([Vless()], Options(sniffing: false))["route"]!["rules"]);

        Assert.Contains(on, rule => (string?)rule!["action"] == "sniff");
        Assert.DoesNotContain(off, rule => (string?)rule!["action"] == "sniff");
    }

    [Theory]
    [InlineData("System", "local", false)]
    [InlineData("Doh", "remote", true)]
    [InlineData("Custom", "remote", true)]
    public void Dns_modes_produce_valid_servers(string mode, string finalTag, bool hasRemote)
    {
        var dnsMode = mode switch
        {
            "Doh" => DnsMode.Doh,
            "Custom" => DnsMode.Custom,
            _ => DnsMode.System
        };

        var dns = Obj(Build([Vless()], Options(dns: dnsMode))["dns"]);
        Assert.Equal(finalTag, (string?)dns["final"]);

        var servers = Arr(dns["servers"]);
        Assert.NotEmpty(servers);
        Assert.All(servers, server => Assert.False(string.IsNullOrWhiteSpace((string?)Obj(server)["type"])));

        var hasRemoteTag = servers.Any(server => (string?)Obj(server)["tag"] == "remote");
        Assert.Equal(hasRemote, hasRemoteTag);
    }

    [Fact]
    public void Vless_emits_uuid_tls_and_websocket_transport()
    {
        var outbound = OutboundAt(Build([Vless()]), 0);
        Assert.Equal("vless", (string?)outbound["type"]);
        Assert.Equal("bf000d23-0752-40b4-affe-68f7707a9661", (string?)outbound["uuid"]);

        var tls = Obj(outbound["tls"]);
        Assert.True((bool?)tls["enabled"]);

        var transport = Obj(outbound["transport"]);
        Assert.Equal("ws", (string?)transport["type"]);
        Assert.Equal("/ws", (string?)transport["path"]);
        Assert.Equal("cdn.example.com", (string?)Obj(transport["headers"])["Host"]);
    }

    [Fact]
    public void Vmess_emits_security_and_grpc()
    {
        var outbound = OutboundAt(Build([Vmess()]), 0);
        Assert.Equal("vmess", (string?)outbound["type"]);
        Assert.Equal("auto", (string?)outbound["security"]);
        Assert.Equal(0, (int?)outbound["alter_id"]);

        var transport = Obj(outbound["transport"]);
        Assert.Equal("grpc", (string?)transport["type"]);
        Assert.Equal("grpc", (string?)transport["service_name"]);
    }

    [Fact]
    public void Shadowsocks_requires_method_and_password()
    {
        var outbound = OutboundAt(Build([Shadowsocks()]), 0);
        Assert.Equal("shadowsocks", (string?)outbound["type"]);
        Assert.Equal("2022-blake3-aes-128-gcm", (string?)outbound["method"]);
        Assert.Equal("8JCsPssfgS8tiRwiMlhARg==", (string?)outbound["password"]);

        var broken = Shadowsocks() with { Method = "" };
        Assert.Throws<EngineException>(() => Build([broken]));
    }

    [Fact]
    public void Hysteria2_is_tls_only_and_emits_obfs()
    {
        var outbound = OutboundAt(Build([Hysteria2()]), 0);
        Assert.Equal("hysteria2", (string?)outbound["type"]);
        Assert.Equal("salamander", (string?)Obj(outbound["obfs"])["type"]);
        Assert.Equal("h3", (string?)Arr(Obj(outbound["tls"])["alpn"])[0]);
        Assert.Null(outbound["transport"]);

        var noTls = Hysteria2() with { Tls = false };
        var forced = OutboundAt(Build([noTls]), 0);
        Assert.True((bool?)Obj(forced["tls"])["enabled"]);
    }

    [Fact]
    public void Tuic_emits_uuid_password_and_control()
    {
        var outbound = OutboundAt(Build([Tuic()]), 0);
        Assert.Equal("tuic", (string?)outbound["type"]);
        Assert.Equal("2DD61D93-75D8-4DA4-AC0E-6AECE7EAC365", (string?)outbound["uuid"]);
        Assert.Equal("pass", (string?)outbound["password"]);
        Assert.Equal("bbr", (string?)outbound["congestion_control"]);
    }

    [Fact]
    public void Reality_emits_public_key_and_utls()
    {
        var profile = Vless() with
        {
            Transport = "TCP",
            RealityPublicKey = "jNXHt1yRo0vDuchQlIP6Z0ZvjT3KtzVI-T4E7RoLJS0",
            RealityShortId = "0123abcd",
            Fingerprint = ""
        };

        var tls = Obj(OutboundAt(Build([profile]), 0)["tls"]);
        var reality = Obj(tls["reality"]);

        Assert.True((bool?)reality["enabled"]);
        Assert.Equal("jNXHt1yRo0vDuchQlIP6Z0ZvjT3KtzVI-T4E7RoLJS0", (string?)reality["public_key"]);
        Assert.Equal("0123abcd", (string?)reality["short_id"]);
        Assert.Equal("chrome", (string?)Obj(tls["utls"])["fingerprint"]);
    }

    [Fact]
    public void WebSocket_alpn_is_filtered_to_http11()
    {
        var profile = Vless() with { Alpn = "h3,h2,http/1.1" };
        var tls = Obj(OutboundAt(Build([profile]), 0)["tls"]);

        var alpn = Arr(tls["alpn"]).Select(item => (string)item!).ToList();
        Assert.Equal(["http/1.1"], alpn);
    }

    [Fact]
    public void WebSocket_alpn_is_omitted_when_only_h3_and_h2()
    {
        var profile = Vless() with { Alpn = "h3,h2" };
        var tls = Obj(OutboundAt(Build([profile]), 0)["tls"]);

        Assert.Null(tls["alpn"]);
    }

    [Fact]
    public void Grpc_alpn_is_filtered_to_h2()
    {
        var profile = Vmess() with { Alpn = "h3,h2,http/1.1" };
        var tls = Obj(OutboundAt(Build([profile]), 0)["tls"]);

        var alpn = Arr(tls["alpn"]).Select(item => (string)item!).ToList();
        Assert.Equal(["h2"], alpn);
    }

    [Fact]
    public void Tcp_alpn_is_kept_unchanged()
    {
        var profile = Vless() with { Transport = "TCP", Alpn = "h2,http/1.1" };
        var tls = Obj(OutboundAt(Build([profile]), 0)["tls"]);

        var alpn = Arr(tls["alpn"]).Select(item => (string)item!).ToList();
        Assert.Equal(["h2", "http/1.1"], alpn);
    }

    [Fact]
    public void Multiplex_is_added_for_stream_protocols_only()
    {
        var options = Options() with { Multiplex = true };

        var vless = OutboundAt(Build([Vless()], options), 0);
        Assert.True((bool?)Obj(vless["multiplex"])["enabled"]);

        var hy2 = OutboundAt(Build([Hysteria2()], options), 0);
        Assert.Null(hy2["multiplex"]);
    }

    [Fact]
    public void Raw_outbound_override_is_used_verbatim_with_engine_tag()
    {
        var profile = Vless() with
        {
            RawOutboundJson = """{"type":"vless","tag":"ignored","server":"raw.example.com","server_port":443,"uuid":"raw-uuid","tls":{"enabled":true}}"""
        };

        var outbound = OutboundAt(Build([profile]), 0);
        Assert.Equal("raw.example.com", (string?)outbound["server"]);
        Assert.Equal("raw-uuid", (string?)outbound["uuid"]);
        Assert.Equal("profile:a", (string?)outbound["tag"]);
    }

    [Fact]
    public void Invalid_profile_throws_a_named_error()
    {
        var broken = Vless() with { Uuid = "" };
        var error = Assert.Throws<EngineException>(() => Build([broken]));
        Assert.Contains("A", error.Message);
        Assert.Contains("UUID", error.Message);
    }

    [Fact]
    public void No_profiles_throws()
    {
        Assert.Throws<EngineException>(() =>
            SingBoxConfigFactory.Build([], "", Options(), Clash, V14));
    }

    [Fact]
    public void Dns_server_parsing_handles_ip_url_and_port()
    {
        var ip = SingBoxConfigFactory.ParseDnsServer("8.8.8.8", preferHttps: false);
        Assert.Equal("udp", (string?)ip["type"]);
        Assert.Equal("8.8.8.8", (string?)ip["server"]);

        var doh = SingBoxConfigFactory.ParseDnsServer("https://dns.google/dns-query", preferHttps: false);
        Assert.Equal("https", (string?)doh["type"]);
        Assert.Equal("dns.google", (string?)doh["server"]);
        Assert.Equal("/dns-query", (string?)doh["path"]);

        var withPort = SingBoxConfigFactory.ParseDnsServer("1.1.1.1:5353", preferHttps: false);
        Assert.Equal(5353, (int?)withPort["server_port"]);

        var ipv6 = SingBoxConfigFactory.ParseDnsServer("2606:4700:4700::1111", preferHttps: false);
        Assert.Equal("2606:4700:4700::1111", (string?)ipv6["server"]);
        Assert.Null(ipv6["server_port"]);

        var ipv6Bracketed = SingBoxConfigFactory.ParseDnsServer("[2001:4860:4860::8888]", preferHttps: false);
        Assert.Equal("2001:4860:4860::8888", (string?)ipv6Bracketed["server"]);
    }

    [Fact]
    public void Clash_api_is_present_and_authenticated()
    {
        var config = Build([Vless()]);
        var experimental = Obj(config["experimental"]);
        var clash = Obj(experimental["clash_api"]);

        Assert.Equal("127.0.0.1:9099", (string?)clash["external_controller"]);
        Assert.Equal("secret", (string?)clash["secret"]);
        Assert.True((bool?)Obj(experimental["cache_file"])["enabled"]);
    }

    [Fact]
    public void Generated_configs_pass_sing_box_check()
    {
        var core = CoreLocator.Locate("");
        if (core is null) return; // No local core: syntax test is skipped.

        var profiles = new[] { Vless(), Vmess(), Trojan(), Shadowsocks(), Hysteria2(), Tuic() };
        var config = Build(profiles);
        var directory = Path.Combine(Path.GetTempPath(), "tunly-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");

        try
        {
            File.WriteAllText(path, SingBoxConfigFactory.Serialize(config));

            var startInfo = new ProcessStartInfo(core.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("check");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo)!;
            var error = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"sing-box check failed: {error}{stdout}");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void Selector_default_falls_back_to_first_profile()
    {
        var config = SingBoxConfigFactory.Build(
            [Vless("a"), Vmess("b")], "does-not-exist", Options(), Clash, V14);

        var selector = Arr(config["outbounds"]).OfType<JsonObject>()
            .Single(o => (string?)o["type"] == "selector");

        Assert.Equal("profile:a", (string?)selector["default"]);
    }

    [Fact]
    public void Custom_dns_emits_fallback()
    {
        var options = Options(DnsMode.Custom) with { DnsServer = "9.9.9.9", DnsFallback = "1.0.0.1" };
        var servers = Arr(Obj(Build([Vless()], options)["dns"])["servers"]);

        Assert.Contains(servers, s => (string?)Obj(s)["tag"] == "remote" && (string?)Obj(s)["server"] == "9.9.9.9");
        Assert.Contains(servers, s => (string?)Obj(s)["tag"] == "local" && (string?)Obj(s)["server"] == "1.0.0.1");
    }
}
