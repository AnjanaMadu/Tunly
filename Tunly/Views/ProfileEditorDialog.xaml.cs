using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tunly.Models;
using Tunly.Services;

namespace Tunly.Views;

public sealed partial class ProfileEditorDialog : ContentDialog
{
    readonly bool _isNew;
    readonly Profile _source;

    public Profile? Result { get; private set; }

    public ProfileEditorDialog(Profile? existing)
    {
        InitializeComponent();

        _isNew = existing is null;
        _source = existing?.Clone() ?? new Profile { Name = "New profile" };
        Title = _isNew ? "Add profile" : "Edit profile";

        Populate();
    }

    void Populate()
    {
        NameBox.Text = _source.Name;
        AddressBox.Text = _source.Address;
        PortBox.Value = _source.Port <= 0 ? 443 : _source.Port;
        SecretBox.Password = _source.Secret;
        SniBox.Text = _source.Sni;
        TlsSwitch.IsOn = _source.Tls;

        MethodBox.Text = _source.Method;
        UuidBox.Text = _source.Uuid;
        FlowBox.Text = _source.Flow;
        PathBox.Text = string.IsNullOrWhiteSpace(_source.TransportPath) ? "/" : _source.TransportPath;
        TransportHostBox.Text = _source.TransportHost;
        ServiceNameBox.Text = _source.ServiceName;
        AlpnBox.Text = _source.Alpn;
        FingerprintBox.Text = _source.Fingerprint;
        InsecureSwitch.IsOn = _source.Insecure;
        RealityKeyBox.Text = _source.RealityPublicKey;
        RealitySidBox.Text = _source.RealityShortId;
        ObfsTypeBox.Text = _source.ObfsType;
        ObfsPasswordBox.Text = _source.ObfsPassword;

        SelectByContent(ProtocolBox, _source.Protocol);
        SelectByContent(TransportBox, _source.Transport);
    }

    static void SelectByContent(ComboBox box, string value)
    {
        var match = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Content as string, value, StringComparison.OrdinalIgnoreCase));
        box.SelectedItem = match ?? box.Items.FirstOrDefault();
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
    }

    void OnAdvancedToggled(object sender, RoutedEventArgs e)
    {
        var raw = AdvancedSwitch.IsOn;
        RawBox.Visibility = raw ? Visibility.Visible : Visibility.Collapsed;

        if (raw && string.IsNullOrWhiteSpace(RawBox.Text))
        {
            var protocol = SelectedText(ProtocolBox).ToLowerInvariant();
            var credentialKey = protocol is "vless" or "vmess" or "tuic" ? "uuid" : "password";
            var credential = protocol == "tuic" ? UuidBox.Text : SecretBox.Password;
            var path = string.IsNullOrWhiteSpace(PathBox.Text) ? "/" : PathBox.Text;

            var outbound = new JsonObject
            {
                ["type"] = protocol,
                ["tag"] = NameBox.Text,
                ["server"] = AddressBox.Text,
                ["server_port"] = (int)PortBox.Value,
                [credentialKey] = credential,
                ["tls"] = new JsonObject
                {
                    ["enabled"] = TlsSwitch.IsOn,
                    ["server_name"] = SniBox.Text
                }
            };

            var transportType = SelectedText(TransportBox) switch
            {
                "WebSocket" => "ws",
                "gRPC" => "grpc",
                "HTTPUpgrade" => "httpupgrade",
                "QUIC" => "quic",
                _ => ""
            };

            if (transportType.Length > 0)
            {
                outbound["transport"] = new JsonObject
                {
                    ["type"] = transportType,
                    ["path"] = path
                };
            }

            RawBox.Text = outbound.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            });
        }
    }

    void OnParseLink(object sender, RoutedEventArgs e)
    {
        var profile = ProfileImporter.Parse(LinkBox.Text);
        if (profile is null)
        {
            ShowError("The link could not be parsed. Supported schemes: vless, vmess, trojan, ss, hysteria2, tuic.");
            return;
        }

        ErrorBar.IsOpen = false;
        NameBox.Text = profile.Name;
        AddressBox.Text = profile.Address;
        PortBox.Value = profile.Port;
        SecretBox.Password = profile.Secret;
        SniBox.Text = profile.Sni;
        TlsSwitch.IsOn = profile.Tls;
        MethodBox.Text = profile.Method;
        UuidBox.Text = profile.Uuid;
        FlowBox.Text = profile.Flow;
        PathBox.Text = string.IsNullOrWhiteSpace(profile.TransportPath) ? "/" : profile.TransportPath;
        TransportHostBox.Text = profile.TransportHost;
        ServiceNameBox.Text = profile.ServiceName;
        AlpnBox.Text = profile.Alpn;
        FingerprintBox.Text = profile.Fingerprint;
        InsecureSwitch.IsOn = profile.Insecure;
        RealityKeyBox.Text = profile.RealityPublicKey;
        RealitySidBox.Text = profile.RealityShortId;
        ObfsTypeBox.Text = profile.ObfsType;
        ObfsPasswordBox.Text = profile.ObfsPassword;
        SelectByContent(ProtocolBox, profile.Protocol);
        SelectByContent(TransportBox, profile.Transport);
    }

    void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text.Trim();
        var address = AddressBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Enter a name for this profile.");
            args.Cancel = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            ShowError("Enter a server address.");
            args.Cancel = true;
            return;
        }

        var result = _source.Clone();
        result.Id = _isNew ? Guid.NewGuid().ToString("N") : _source.Id;
        result.Name = name;
        result.Address = address;
        result.Port = (int)PortBox.Value;
        result.Secret = SecretBox.Password;
        result.Sni = SniBox.Text.Trim();
        result.Tls = TlsSwitch.IsOn;
        result.Protocol = SelectedText(ProtocolBox);
        result.Transport = SelectedText(TransportBox);

        result.Method = MethodBox.Text.Trim();
        result.Uuid = UuidBox.Text.Trim();

        // The form keeps the VLESS/VMess UUID in the "UUID / password" box; keep the
        // dedicated field in sync so edits are honored by the config generator.
        if (result.Protocol is "VLESS" or "VMess")
            result.Uuid = result.Secret;
        result.Flow = FlowBox.Text.Trim();
        result.TransportPath = string.IsNullOrWhiteSpace(PathBox.Text) ? "/" : PathBox.Text.Trim();
        result.TransportHost = TransportHostBox.Text.Trim();
        result.ServiceName = ServiceNameBox.Text.Trim();
        result.Alpn = AlpnBox.Text.Trim();
        result.Fingerprint = FingerprintBox.Text.Trim();
        result.Insecure = InsecureSwitch.IsOn;
        result.RealityPublicKey = RealityKeyBox.Text.Trim();
        result.RealityShortId = RealitySidBox.Text.Trim();
        result.ObfsType = ObfsTypeBox.Text.Trim();
        result.ObfsPassword = ObfsPasswordBox.Text.Trim();
        result.RawOutbound = "";

        if (AdvancedSwitch.IsOn && !TryApplyRaw(result, out var error))
        {
            ShowError(error);
            args.Cancel = true;
            return;
        }

        if (AdvancedSwitch.IsOn)
            result.RawOutbound = RawBox.Text.Trim();

        ErrorBar.IsOpen = false;
        Result = result;
    }

    bool TryApplyRaw(Profile profile, out string error)
    {
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(RawBox.Text);
            var root = doc.RootElement;

            if (root.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.String)
                profile.Address = server.GetString() ?? profile.Address;

            if (root.TryGetProperty("server_port", out var port) && port.TryGetInt32(out var portValue))
                profile.Port = portValue;

            if (root.TryGetProperty("tag", out var tag) && tag.ValueKind == JsonValueKind.String)
            {
                var tagValue = tag.GetString();
                if (!string.IsNullOrWhiteSpace(tagValue)) profile.Name = tagValue;
            }

            if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                profile.Protocol = type.GetString()?.ToUpperInvariant() ?? profile.Protocol;

            if (root.TryGetProperty("uuid", out var uuid) && uuid.ValueKind == JsonValueKind.String)
            {
                profile.Secret = uuid.GetString() ?? profile.Secret;
                profile.Uuid = profile.Secret;
            }

            if (root.TryGetProperty("password", out var password) && password.ValueKind == JsonValueKind.String)
                profile.Secret = password.GetString() ?? profile.Secret;

            if (root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
                profile.Method = method.GetString() ?? profile.Method;

            if (root.TryGetProperty("flow", out var flow) && flow.ValueKind == JsonValueKind.String)
                profile.Flow = flow.GetString() ?? profile.Flow;

            if (root.TryGetProperty("tls", out var tls) && tls.ValueKind == JsonValueKind.Object)
            {
                if (tls.TryGetProperty("enabled", out var enabled) &&
                    (enabled.ValueKind == JsonValueKind.True || enabled.ValueKind == JsonValueKind.False))
                {
                    profile.Tls = enabled.GetBoolean();
                }

                if (tls.TryGetProperty("server_name", out var sni) && sni.ValueKind == JsonValueKind.String)
                    profile.Sni = sni.GetString() ?? profile.Sni;

                if (tls.TryGetProperty("insecure", out var insecure) &&
                    (insecure.ValueKind == JsonValueKind.True || insecure.ValueKind == JsonValueKind.False))
                {
                    profile.Insecure = insecure.GetBoolean();
                }

                if (tls.TryGetProperty("alpn", out var alpn) && alpn.ValueKind == JsonValueKind.Array)
                {
                    profile.Alpn = string.Join(",",
                        alpn.EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString()));
                }

                if (tls.TryGetProperty("utls", out var utls) && utls.ValueKind == JsonValueKind.Object &&
                    utls.TryGetProperty("fingerprint", out var fingerprint) &&
                    fingerprint.ValueKind == JsonValueKind.String)
                {
                    profile.Fingerprint = fingerprint.GetString() ?? profile.Fingerprint;
                }

                if (tls.TryGetProperty("reality", out var reality) && reality.ValueKind == JsonValueKind.Object)
                {
                    if (reality.TryGetProperty("public_key", out var publicKey) &&
                        publicKey.ValueKind == JsonValueKind.String)
                    {
                        profile.RealityPublicKey = publicKey.GetString() ?? profile.RealityPublicKey;
                    }

                    if (reality.TryGetProperty("short_id", out var shortId) &&
                        shortId.ValueKind == JsonValueKind.String)
                    {
                        profile.RealityShortId = shortId.GetString() ?? profile.RealityShortId;
                    }
                }
            }

            if (root.TryGetProperty("obfs", out var obfs) && obfs.ValueKind == JsonValueKind.Object)
            {
                if (obfs.TryGetProperty("type", out var obfsType) && obfsType.ValueKind == JsonValueKind.String)
                    profile.ObfsType = obfsType.GetString() ?? profile.ObfsType;

                if (obfs.TryGetProperty("password", out var obfsPassword) && obfsPassword.ValueKind == JsonValueKind.String)
                    profile.ObfsPassword = obfsPassword.GetString() ?? profile.ObfsPassword;
            }

            if (root.TryGetProperty("transport", out var transport) && transport.ValueKind == JsonValueKind.Object)
            {
                if (transport.TryGetProperty("type", out var transportType) && transportType.ValueKind == JsonValueKind.String)
                {
                    profile.Transport = transportType.GetString()?.ToLowerInvariant() switch
                    {
                        "ws" => "WebSocket",
                        "grpc" => "gRPC",
                        "httpupgrade" => "HTTPUpgrade",
                        "quic" => "QUIC",
                        _ => "TCP"
                    };
                }

                if (transport.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                    profile.TransportPath = path.GetString() ?? profile.TransportPath;

                if (transport.TryGetProperty("host", out var host) && host.ValueKind == JsonValueKind.String)
                    profile.TransportHost = host.GetString() ?? profile.TransportHost;

                if (transport.TryGetProperty("service_name", out var service) && service.ValueKind == JsonValueKind.String)
                    profile.ServiceName = service.GetString() ?? profile.ServiceName;

                if (transport.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object &&
                    headers.TryGetProperty("Host", out var headerHost) && headerHost.ValueKind == JsonValueKind.String)
                {
                    profile.TransportHost = headerHost.GetString() ?? profile.TransportHost;
                }
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"The raw JSON is not valid: {ex.Message}";
            return false;
        }
    }

    static string SelectedText(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content as string ?? "VLESS";

    void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
