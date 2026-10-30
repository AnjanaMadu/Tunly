using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tunly.Core.Models;

namespace Tunly.Core;

/// <summary>Minimal client for the sing-box Clash API.</summary>
public sealed class ClashApiClient : IDisposable
{
    public const string DefaultDelayUrl = "http://www.gstatic.com/generate_204";

    readonly HttpClient _http;

    public ClashApiClient(ClashEndpoint endpoint)
    {
        Endpoint = endpoint;

        var handler = new SocketsHttpHandler
        {
            // The app may run with the Windows system proxy pointed at itself; never proxy loopback API calls.
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = endpoint.BaseAddress,
            Timeout = TimeSpan.FromSeconds(10)
        };

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Secret);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Tunly/1.0");
    }

    public ClashEndpoint Endpoint { get; }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync("/version", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("/version", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return document.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
    }

    /// <summary>Streams the Clash <c>/traffic</c> feed (one sample per second, bytes/s).</summary>
    public async IAsyncEnumerable<TrafficSample> StreamTrafficAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/traffic");
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line is null) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            TrafficSample sample;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var up = root.TryGetProperty("up", out var upElement) ? upElement.GetInt64() : 0;
                var down = root.TryGetProperty("down", out var downElement) ? downElement.GetInt64() : 0;
                sample = new TrafficSample(up, down);
            }
            catch (JsonException)
            {
                continue;
            }

            yield return sample;
        }
    }

    /// <summary>Switches the selector outbound to another proxy tag.</summary>
    public async Task SelectAsync(string selectorTag, string outboundTag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/proxies/{Uri.EscapeDataString(selectorTag)}")
        {
            Content = JsonContent.Create(new { name = outboundTag })
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new EngineException(
                $"The core rejected the outbound switch ({(int)response.StatusCode} {response.ReasonPhrase}). {Trim(body)}");
        }
    }

    /// <summary>Measures the delay of one outbound. Returns null when the probe fails.</summary>
    public async Task<int?> GetDelayAsync(
        string outboundTag,
        string url = DefaultDelayUrl,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        var path = $"/proxies/{Uri.EscapeDataString(outboundTag)}/delay" +
                   $"?url={Uri.EscapeDataString(url)}&timeout={timeoutMs}";

        try
        {
            using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.TryGetProperty("delay", out var delay) && delay.TryGetInt32(out var value))
                return value;

            return null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    static string Trim(string body) =>
        body.Length <= 300 ? body : body[..300] + "…";

    public void Dispose() => _http.Dispose();
}
