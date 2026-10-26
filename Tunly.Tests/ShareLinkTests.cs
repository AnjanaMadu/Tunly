using System.Text;
using Tunly.Core;
using Tunly.Core.Models;
using Xunit;

namespace Tunly.Tests;

public class ShareLinkTests
{
    static string Base64(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Parses_vless_websocket_link()
    {
        var link = ShareLinkParser.Parse(
            "vless://bf000d23-0752-40b4-affe-68f7707a9661@example.com:8443?type=ws&security=tls&path=%2Fws&host=cdn.example.com&sni=cdn.example.com#Frankfurt");

        Assert.NotNull(link);
        Assert.Equal(ProtocolKind.Vless, link!.Protocol);
        Assert.Equal("example.com", link.Server);
        Assert.Equal(8443, link.Port);
        Assert.Equal("bf000d23-0752-40b4-affe-68f7707a9661", link.Uuid);
        Assert.Equal("WebSocket", link.Transport);
        Assert.Equal("/ws", link.Path);
        Assert.Equal("cdn.example.com", link.Host);
        Assert.True(link.Tls);
        Assert.Equal("Frankfurt", link.Name);
    }

    [Fact]
    public void Parses_vless_reality_link()
    {
        var link = ShareLinkParser.Parse(
            "vless://uuid-1@example.com:443?security=reality&type=tcp&pbk=PUBKEY&sid=ab12&fp=chrome&flow=xtls-rprx-vision&sni=www.microsoft.com#Reality");

        Assert.NotNull(link);
        Assert.True(link!.Tls);
        Assert.Equal("PUBKEY", link.RealityPublicKey);
        Assert.Equal("ab12", link.RealityShortId);
        Assert.Equal("chrome", link.Fingerprint);
        Assert.Equal("xtls-rprx-vision", link.Flow);
        Assert.Equal("www.microsoft.com", link.ServerName);
    }

    [Fact]
    public void Parses_vmess_base64_json()
    {
        var json = """
            {"v":"2","ps":"Tokyo","add":"jp.example.com","port":"443","id":"uuid-1","aid":"0","net":"ws","path":"/ray","host":"jp.example.com","tls":"tls","sni":"jp.example.com"}
            """;
        var link = ShareLinkParser.Parse("vmess://" + Base64(json));

        Assert.NotNull(link);
        Assert.Equal(ProtocolKind.Vmess, link!.Protocol);
        Assert.Equal("Tokyo", link.Name);
        Assert.Equal("jp.example.com", link.Server);
        Assert.Equal(443, link.Port);
        Assert.Equal("uuid-1", link.Uuid);
        Assert.Equal("WebSocket", link.Transport);
        Assert.Equal("/ray", link.Path);
        Assert.True(link.Tls);
    }

    [Fact]
    public void Parses_shadowsocks_sip002_userinfo()
    {
        var userInfo = Base64("aes-256-gcm:correct-horse");
        var link = ShareLinkParser.Parse($"ss://{userInfo}@1.2.3.4:8388#Node");

        Assert.NotNull(link);
        Assert.Equal(ProtocolKind.Shadowsocks, link!.Protocol);
        Assert.Equal("1.2.3.4", link.Server);
        Assert.Equal(8388, link.Port);
        Assert.Equal("aes-256-gcm", link.Method);
        Assert.Equal("correct-horse", link.Password);
        Assert.Equal("Node", link.Name);
    }

    [Fact]
    public void Parses_shadowsocks_all_in_one_base64()
    {
        var payload = Base64("chacha20-ietf-poly1305:pw@[2001:db8::1]:443");
        var link = ShareLinkParser.Parse($"ss://{payload}#IPv6");

        Assert.NotNull(link);
        Assert.Equal("2001:db8::1", link!.Server);
        Assert.Equal(443, link.Port);
        Assert.Equal("chacha20-ietf-poly1305", link.Method);
        Assert.Equal("pw", link.Password);
    }

    [Fact]
    public void Parses_hysteria2_obfs()
    {
        var link = ShareLinkParser.Parse(
            "hysteria2://letmein@example.com:443?obfs=salamander&obfs-password=xyz&sni=example.com#Hy2");

        Assert.NotNull(link);
        Assert.Equal(ProtocolKind.Hysteria2, link!.Protocol);
        Assert.Equal("letmein", link.Password);
        Assert.True(link.Tls);
        Assert.Equal("salamander", link.ObfsType);
        Assert.Equal("xyz", link.ObfsPassword);
        Assert.Equal("example.com", link.ServerName);
    }

    [Fact]
    public void Parses_tuic_uuid_and_password()
    {
        var link = ShareLinkParser.Parse("tuic://uuid-123:secret@example.com:443?sni=example.com#TUIC");

        Assert.NotNull(link);
        Assert.Equal(ProtocolKind.Tuic, link!.Protocol);
        Assert.Equal("uuid-123", link.Uuid);
        Assert.Equal("secret", link.Password);
        Assert.True(link.Tls);
    }

    [Fact]
    public void Parses_trojan_password_and_grpc()
    {
        var link = ShareLinkParser.Parse(
            "trojan://letmein@example.com:443?type=grpc&serviceName=grpcsvc&sni=example.com#Trojan");

        Assert.NotNull(link);
        Assert.Equal(ProtocolKind.Trojan, link!.Protocol);
        Assert.Equal("letmein", link.Password);
        Assert.Equal("gRPC", link.Transport);
        Assert.Equal("grpcsvc", link.ServiceName);
    }

    [Fact]
    public void Parses_base64_subscription_with_multiple_links()
    {
        var text = string.Join('\n',
            "vless://uuid-1@a.example.com:443?security=tls#A",
            "trojan://pw@b.example.com:443?security=tls#B");

        var links = ShareLinkParser.ParseMany(Base64(text));

        Assert.Equal(2, links.Count);
        Assert.Equal("A", links[0].Name);
        Assert.Equal("B", links[1].Name);
    }

    [Fact]
    public void Rejects_garbage()
    {
        Assert.Empty(ShareLinkParser.ParseMany("not a link"));
        Assert.Empty(ShareLinkParser.ParseMany(""));
        Assert.Empty(ShareLinkParser.ParseMany("vmess://%%%"));
    }
}
