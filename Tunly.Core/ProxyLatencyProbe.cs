using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tunly.Core.Models;
using Tunly.Core.Support;

namespace Tunly.Core;

/// <summary>
/// Measures a profile end to end: runs a temporary sing-box with just that
/// outbound behind a local mixed inbound, then times a real HTTP round trip
/// through the tunnel. No TUN adapter, so it does not need elevation.
/// </summary>
public static class ProxyLatencyProbe
{
    public const string ProbeUrl = "http://www.gstatic.com/generate_204";

    public static async Task<int?> ProbeAsync(
        OutboundSpec profile,
        string corePath,
        string configDirectory,
        CancellationToken cancellationToken = default)
    {
        var location = CoreLocator.Locate(corePath);
        if (location is null) return null;

        var errors = profile.Validate();
        if (errors.Count > 0) return null;

        // One shared deadline for core startup plus the round trip itself.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        var port = NetPorts.GetFreeTcpPort();
        var configPath = Path.Combine(configDirectory, $"probe-{profile.Id}.json");
        Directory.CreateDirectory(configDirectory);
        AtomicFile.WriteAllText(configPath, SingBoxConfigFactory.Serialize(
            SingBoxConfigFactory.BuildProbeConfig(profile, port)));

        var process = new SingBoxProcess();
        try
        {
            process.Start(location.ExecutablePath, configPath, configDirectory);

            if (!await WaitForInboundAsync(process, port, timeout.Token).ConfigureAwait(false))
                return null;

            using var http = new HttpClient(new SocketsHttpHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            })
            {
                Timeout = TimeSpan.FromSeconds(8)
            };

            var stopwatch = Stopwatch.StartNew();
            using var response = await http.GetAsync(ProbeUrl, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NoContent)
                return null;

            stopwatch.Stop();
            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            try { await process.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception) { /* Best effort. */ }

            try { File.Delete(configPath); } catch (Exception) { /* Best effort. */ }
        }
    }

    /// <summary>Only accepts the 204 that <c>generate_204</c> answers with; anything else counts as a failure.</summary>
    static async Task<bool> WaitForInboundAsync(SingBoxProcess process, int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!process.IsRunning) return false;

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (SocketException)
            {
                // Inbound not up yet.
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }
}
