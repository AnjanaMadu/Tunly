using System.Diagnostics;
using Tunly.Core;
using Tunly.Core.Models;
using Xunit;

namespace Tunly.Tests;

/// <summary>Regression coverage for real-world share-link formats and edge cases.</summary>
public class RealWorldLinkTests
{
    public const string VlessTcp = "vless://11111111-1111-1111-1111-111111111111@vless.example.com:443?security=tls&encryption=none&sni=sni.example.com%20&fp=chrome&insecure=1&type=tcp&alpn=h2%2Chttp%2F1.1&headerType=none&allowInsecure=1#vless-tcp";

    public const string VlessIpv6Ws = "vless://22222222-2222-2222-2222-222222222222@[2001:db8::1]:8443?insecure=1&encryption=none&fp=chrome&sni=ws.example.com&type=ws&alpn=h3%2Ch2%2Chttp%2F1.1&security=tls&host=ws.example.com&allowInsecure=1#vless-ws-v6";

    public const string VlessIpv4Ws = "vless://22222222-2222-2222-2222-222222222222@192.0.2.10:8443?fp=chrome&encryption=none&allowInsecure=1&type=ws&sni=ws.example.com&insecure=1&host=ws.example.com&security=tls&alpn=h3%2Ch2%2Chttp%2F1.1#vless-ws-v4";

    public const string VmessWs = "vmess://ewogICJhZGQiOiAiMjAwMTpkYjg6OjEiLAogICJhaWQiOiAiMCIsCiAgImFscG4iOiAiaDMsaDIsaHR0cC8xLjEiLAogICJmcCI6ICJjaHJvbWUiLAogICJob3N0IjogInZtZXNzLmV4YW1wbGUuY29tIiwKICAiaWQiOiAiMzMzMzMzMzMtMzMzMy0zMzMzLTMzMzMtMzMzMzMzMzMzMzMzIiwKICAiaW5zZWN1cmUiOiAiMSIsCiAgIm5ldCI6ICJ3cyIsCiAgInBvcnQiOiAiMjA5NiIsCiAgInBzIjogIlZNZXNzIFdTIFRlc3QiLAogICJzY3kiOiAiYXV0byIsCiAgInNuaSI6ICJ2bWVzcy5leGFtcGxlLmNvbSIsCiAgInRscyI6ICJ0bHMiLAogICJ0eXBlIjogIm5vbmUiLAogICJ2IjogIjIiCn0=";

    public static TheoryData<string, ProtocolKind, string, int> Links => new()
    {
        { VlessTcp, ProtocolKind.Vless, "vless.example.com", 443 },
        { VlessIpv6Ws, ProtocolKind.Vless, "2001:db8::1", 8443 },
        { VlessIpv4Ws, ProtocolKind.Vless, "192.0.2.10", 8443 },
        { VmessWs, ProtocolKind.Vmess, "2001:db8::1", 2096 }
    };

    [Theory]
    [MemberData(nameof(Links))]
    public void Parses_links_without_brackets_or_trailing_space(
        string link, ProtocolKind protocol, string server, int port)
    {
        var parsed = ShareLinkParser.Parse(link);

        Assert.NotNull(parsed);
        Assert.Equal(protocol, parsed!.Protocol);
        Assert.Equal(server, parsed.Server);
        Assert.Equal(port, parsed.Port);
        Assert.DoesNotContain('[', parsed.Server);
        Assert.DoesNotContain(']', parsed.Server);
        Assert.Equal(parsed.ServerName, parsed.ServerName.Trim());
        Assert.True(parsed.Tls);
        Assert.True(parsed.Insecure);
    }

    [Fact]
    public void Vless_tcp_link_keeps_alpn_and_utls()
    {
        var parsed = ShareLinkParser.Parse(VlessTcp)!;

        Assert.Equal("TCP", parsed.Transport);
        Assert.Equal("sni.example.com", parsed.ServerName);
        Assert.Equal("h2,http/1.1", parsed.Alpn);
        Assert.Equal("chrome", parsed.Fingerprint);
        Assert.Equal("vless-tcp", parsed.Name);
    }

    [Fact]
    public void Vless_ws_link_sets_host_and_path()
    {
        var parsed = ShareLinkParser.Parse(VlessIpv6Ws)!;

        Assert.Equal("WebSocket", parsed.Transport);
        Assert.Equal("ws.example.com", parsed.Host);
        Assert.Equal("/", parsed.Path);
        Assert.Equal("h3,h2,http/1.1", parsed.Alpn);
        Assert.Equal("vless-ws-v6", parsed.Name);
    }

    [Fact]
    public void Vmess_link_reads_pretty_printed_json()
    {
        var parsed = ShareLinkParser.Parse(VmessWs)!;

        Assert.Equal("VMess WS Test", parsed.Name);
        Assert.Equal("33333333-3333-3333-3333-333333333333", parsed.Uuid);
        Assert.Equal("WebSocket", parsed.Transport);
        Assert.Equal("vmess.example.com", parsed.Host);
        Assert.Equal("vmess.example.com", parsed.ServerName);
        Assert.Equal("h3,h2,http/1.1", parsed.Alpn);
        Assert.Equal("chrome", parsed.Fingerprint);
        Assert.True(parsed.Insecure);
    }

    [Fact]
    public void Ws_links_generate_http11_alpn_only()
    {
        foreach (var link in new[] { VlessIpv6Ws, VlessIpv4Ws, VmessWs })
        {
            var parsed = ShareLinkParser.Parse(link)!;
            var spec = ToSpec(parsed);

            var config = SingBoxConfigFactory.Build(
                [spec],
                spec.Id,
                new TunOptions { DnsMode = DnsMode.System, Ipv6 = true, Sniffing = true },
                new ClashEndpoint("127.0.0.1", 9099, "secret"),
                new CoreVersion(1, 14, 4, "test"));

            var tls = config["outbounds"]!.AsArray()[0]!["tls"]!.AsObject();
            var alpn = tls["alpn"]!.AsArray().Select(item => (string)item!).ToList();

            Assert.Equal(["http/1.1"], alpn);
        }
    }

    [Theory]
    [MemberData(nameof(Links))]
    public void Generated_configs_pass_sing_box_check_for_each_link(
        string link, ProtocolKind protocol, string server, int port)
    {
        _ = protocol;
        _ = server;
        _ = port;

        var core = CoreLocator.Locate("");
        if (core is null) return; // Skip when no core is installed.

        var parsed = ShareLinkParser.Parse(link);
        Assert.NotNull(parsed);

        var spec = ToSpec(parsed!);

        var config = SingBoxConfigFactory.Build(
            [spec],
            spec.Id,
            new TunOptions { DnsMode = DnsMode.System, Ipv6 = true, Sniffing = true },
            new ClashEndpoint("127.0.0.1", 9099, "secret"),
            new CoreVersion(1, 14, 4, "test"));

        var path = Path.Combine(Path.GetTempPath(), $"tunly-link-{Guid.NewGuid():N}.json");
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
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    static OutboundSpec ToSpec(ProfileLink link) => new()
    {
        Id = "test",
        Name = link.Name,
        Protocol = link.Protocol,
        Server = link.Server,
        Port = link.Port,
        Uuid = link.Uuid,
        Password = link.Password,
        Method = link.Method,
        Flow = link.Flow,
        Transport = link.Transport,
        Path = link.Path,
        Host = link.Host,
        ServiceName = link.ServiceName,
        Tls = link.Tls,
        ServerName = link.ServerName,
        Alpn = link.Alpn,
        Fingerprint = link.Fingerprint,
        Insecure = link.Insecure,
        RealityPublicKey = link.RealityPublicKey,
        RealityShortId = link.RealityShortId,
        ObfsType = link.ObfsType,
        ObfsPassword = link.ObfsPassword
    };
}
