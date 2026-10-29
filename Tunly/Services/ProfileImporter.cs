using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tunly.Core;
using Tunly.Core.Models;
using Tunly.Models;

namespace Tunly.Services;

/// <summary>Parses share links (vless, vmess, trojan, ss, hysteria2, tuic and subscriptions).</summary>
public static class ProfileImporter
{
    public static async Task<Profile?> FromClipboardAsync() =>
        (await FromClipboardManyAsync()).FirstOrDefault();

    public static async Task<IReadOnlyList<Profile>> FromClipboardManyAsync()
    {
        var view = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!view.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            return Array.Empty<Profile>();

        var text = (await view.GetTextAsync()).Trim();
        return ParseMany(text);
    }

    public static Profile? Parse(string text) => ParseMany(text).FirstOrDefault();

    public static IReadOnlyList<Profile> ParseMany(string text) =>
        ShareLinkParser.ParseMany(text).Select(ToProfile).ToList();

    static Profile ToProfile(ProfileLink link) => new()
    {
        Name = link.Name,
        Protocol = link.Protocol switch
        {
            ProtocolKind.Vless => "VLESS",
            ProtocolKind.Vmess => "VMess",
            ProtocolKind.Trojan => "Trojan",
            ProtocolKind.Shadowsocks => "Shadowsocks",
            ProtocolKind.Hysteria2 => "Hysteria2",
            ProtocolKind.Tuic => "TUIC",
            _ => "VLESS"
        },
        Address = link.Server,
        Port = link.Port,
        Secret = link.Protocol is ProtocolKind.Vless or ProtocolKind.Vmess
            ? link.Uuid
            : link.Password,
        Uuid = link.Uuid,
        Method = link.Method,
        Flow = link.Flow,
        Transport = link.Transport,
        TransportPath = link.Path,
        TransportHost = link.Host,
        ServiceName = link.ServiceName,
        Tls = link.Tls,
        Sni = link.ServerName,
        Alpn = link.Alpn,
        Fingerprint = link.Fingerprint,
        Insecure = link.Insecure,
        RealityPublicKey = link.RealityPublicKey,
        RealityShortId = link.RealityShortId,
        ObfsType = link.ObfsType,
        ObfsPassword = link.ObfsPassword
    };
}
