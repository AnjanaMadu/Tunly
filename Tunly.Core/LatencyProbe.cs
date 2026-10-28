using System.Diagnostics;
using System.Net.Sockets;

namespace Tunly.Core;

/// <summary>Measures raw connection latency to a server endpoint.</summary>
public static class LatencyProbe
{
    public static async Task<int?> TcpAsync(
        string host,
        int port,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            return null;

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
