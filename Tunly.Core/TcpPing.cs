using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Tunly.Core;

/// <summary>
/// Times a raw TCP three-way handshake to a server address and port. This is a
/// fast, direct network latency check that involves no tunnel. Note the caveat:
/// for CDN-fronted addresses (for example Cloudflare), the handshake terminates
/// at the nearest edge node, so the figure measures the edge rather than the
/// server behind it.
/// </summary>
public static class TcpPing
{
    public const int DefaultTimeoutMs = 2000;

    /// <summary>
    /// Connects to <c>server:port</c> and returns the elapsed time in
    /// milliseconds, or null when the address is unusable or unreachable
    /// inside <paramref name="timeoutMs"/>.
    /// </summary>
    public static async Task<int?> MeasureAsync(
        string server,
        int port,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        if (port is < 1 or > 65535) return null;

        // One shared deadline for DNS resolution plus the TCP connect.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(server, timeout.Token).ConfigureAwait(false);
            if (addresses.Length == 0) return null;

            // IPv4 first: IPv6 can be unreachable depending on the environment.
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses[0];

            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            var stopwatch = Stopwatch.StartNew();
            await socket.ConnectAsync(new IPEndPoint(address, port), timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
