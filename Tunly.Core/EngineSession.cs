using System.Security.Cryptography;
using Tunly.Core.Models;
using Tunly.Core.Support;

namespace Tunly.Core;

/// <summary>
/// One sing-box run: config generation, validation, process supervision,
/// Clash API readiness and traffic pumping.
/// </summary>
public sealed class EngineSession : IAsyncDisposable
{
    readonly object _gate = new();

    SingBoxProcess? _process;
    ClashApiClient? _clash;
    CancellationTokenSource? _trafficCts;
    Task? _trafficPump;

    volatile bool _stopping;
    volatile bool _started;

    public ClashEndpoint? Endpoint { get; private set; }
    public CoreVersion? Version { get; private set; }
    public string? CorePath { get; private set; }
    public string? ConfigPath { get; private set; }
    public bool HasWintun { get; private set; }

    public bool IsRunning => _process?.IsRunning == true;

    public IReadOnlyList<string> RecentLines => _process?.RecentLines ?? Array.Empty<string>();

    public event EventHandler<TrafficSample>? TrafficUpdated;
    public event EventHandler<EngineLog>? LogReceived;
    public event EventHandler<EngineExit>? Exited;

    public async Task StartAsync(EngineStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await StopAsync().ConfigureAwait(false);

        var options = request.Options;

        var location = CoreLocator.Locate(options.CorePath)
            ?? throw new EngineException(
                "sing-box was not found. Set the core path in Settings → Core & logs or install it from PATH.");

        var version = await CoreLocator
            .RequireSupportedAsync(location.ExecutablePath, cancellationToken)
            .ConfigureAwait(false);

        if (request.Profiles.Count == 0)
            throw new EngineException("No profiles are configured. Add a profile before connecting.");

        SingBoxConfigFactory.ValidateProfiles(request.Profiles);

        var active = request.Profiles.FirstOrDefault(p => p.Id == request.ActiveProfileId)
                     ?? request.Profiles[0];

        var clashPort = options.ClashPort > 0 && NetPorts.IsTcpPortFree(options.ClashPort)
            ? options.ClashPort
            : NetPorts.GetFreeTcpPort();

        var endpoint = new ClashEndpoint("127.0.0.1", clashPort, NewSecret());

        var configDirectory = AppPaths.ResolveConfigDirectory(options.ConfigDirectory);
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, "config.json");

        var config = SingBoxConfigFactory.Build(
            request.Profiles, active.Id, options, endpoint, version);

        AtomicFile.WriteAllText(configPath, SingBoxConfigFactory.Serialize(config));

        var checkError = await SingBoxProcess
            .CheckConfigAsync(location.ExecutablePath, configPath, cancellationToken)
            .ConfigureAwait(false);

        if (checkError is not null)
        {
            throw new EngineException(
                "sing-box rejected the generated configuration:" +
                Environment.NewLine +
                Tail(checkError, 12));
        }

        var process = new SingBoxProcess();
        process.LogReceived += (_, log) => LogReceived?.Invoke(this, log);
        process.Exited += OnProcessExited;

        try
        {
            process.Start(location.ExecutablePath, configPath, configDirectory);
        }
        catch
        {
            await process.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var clash = new ClashApiClient(endpoint);

        lock (_gate)
        {
            _process = process;
            _clash = clash;
            Endpoint = endpoint;
            Version = version;
            CorePath = location.ExecutablePath;
            ConfigPath = configPath;
            HasWintun = location.HasWintun;
        }

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            var ready = false;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!process.IsRunning)
                {
                    throw new EngineException(
                        "sing-box exited during startup." + Environment.NewLine +
                        Tail(string.Join(Environment.NewLine, process.RecentLines), 12));
                }

                if (await clash.IsReadyAsync(cancellationToken).ConfigureAwait(false))
                {
                    ready = true;
                    break;
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            if (!ready)
                throw new EngineException("The Clash API did not become ready within 20 seconds.");

            await clash.SelectAsync(SingBoxConfigFactory.SelectorTag, active.Tag, cancellationToken)
                .ConfigureAwait(false);

            _started = true;
            StartTrafficPump(clash);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    void StartTrafficPump(ClashApiClient clash)
    {
        var cts = new CancellationTokenSource();
        _trafficCts = cts;

        _trafficPump = Task.Run(async () =>
        {
            try
            {
                await foreach (var sample in clash.StreamTrafficAsync(cts.Token).ConfigureAwait(false))
                {
                    TrafficUpdated?.Invoke(this, sample);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke(this,
                    new EngineLog(EngineLogLevel.Warn, $"Traffic monitor stopped: {ex.Message}", ex.Message));
            }
        }, CancellationToken.None);
    }

    void OnProcessExited(object? sender, EngineExit exit)
    {
        if (_stopping || !_started) return;
        _started = false;
        Exited?.Invoke(this, exit);
    }

    public async Task SwitchOutboundAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var clash = _clash;
        if (clash is null || !IsRunning)
            throw new EngineException("The tunnel is not running.");

        await clash.SelectAsync(SingBoxConfigFactory.SelectorTag, $"profile:{profileId}", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int?> TestDelayAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var clash = _clash;
        if (clash is null || !IsRunning) return null;

        return await clash.GetDelayAsync($"profile:{profileId}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        _stopping = true;
        _started = false;

        var cts = _trafficCts;
        var pump = _trafficPump;
        _trafficCts = null;
        _trafficPump = null;

        if (cts is not null)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }

            if (pump is not null)
                await Task.WhenAny(pump, Task.Delay(2000)).ConfigureAwait(false);

            cts.Dispose();
        }

        SingBoxProcess? process;
        ClashApiClient? clash;

        lock (_gate)
        {
            process = _process;
            clash = _clash;
            _process = null;
            _clash = null;
            Endpoint = null;
            Version = null;
            CorePath = null;
            ConfigPath = null;
            HasWintun = false;
        }

        if (process is not null)
            await process.DisposeAsync().ConfigureAwait(false);

        clash?.Dispose();

        _stopping = false;
    }

    static string NewSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    static string Tail(string text, int lines) =>
        string.Join(Environment.NewLine, text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .TakeLast(lines));

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
