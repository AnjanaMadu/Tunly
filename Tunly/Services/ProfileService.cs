using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tunly.Core;
using Tunly.Core.Models;
using Tunly.Models;

namespace Tunly.Services;

public sealed class ProfileService
{
    readonly JsonSerializerOptions _json = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };

    public ObservableCollection<Profile> Items { get; } = new();

    string ProfilesPath => Path.Combine(AppServices.SettingsService.AppDataDirectory, "profiles.json");

    public void Load()
    {
        try
        {
            if (File.Exists(ProfilesPath))
            {
                var loaded = JsonSerializer.Deserialize<ObservableCollection<Profile>>(File.ReadAllText(ProfilesPath), _json);
                if (loaded is not null)
                {
                    foreach (var p in loaded) Items.Add(p);
                }
            }
        }
        catch
        {
            // Ignore unreadable profile data; the user can add profiles again.
        }

        Items.CollectionChanged += (_, _) => Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppServices.SettingsService.AppDataDirectory);
            File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(Items, _json));
        }
        catch
        {
            // Ignore persistence failures.
        }
    }

    public Profile Add(Profile profile)
    {
        Items.Add(profile);
        return profile;
    }

    public void Remove(Profile profile) => Items.Remove(profile);

    public async Task TestAllAsync(LogService logs)
    {
        foreach (var p in Items) { p.LatencyMs = null; p.LatencyMode = ""; }

        var realDelay = AppServices.Settings.LatencyTest == "RealDelay";
        logs.Info(realDelay ? "Testing real delay…" : "Testing TCP latency…");

        var connected = AppServices.Connection.IsConnected;
        using var gate = new SemaphoreSlim(realDelay ? (connected ? 4 : 2) : 8);

        var probeDirectory = AppPaths.ResolveConfigDirectory(AppServices.Settings.ConfigDirectory);
        var corePath = AppServices.Settings.CorePath;

        var probes = Items.ToList().Select(async profile =>
        {
            await gate.WaitAsync();
            try
            {
                OutboundSpec spec;
                try
                {
                    spec = ProfileMapper.ToSpec(profile);
                }
                catch (Exception ex)
                {
                    logs.Warn($"Latency test skipped for {profile.Name}: {ex.Message}");
                    return;
                }

                int? delay;
                string mode;

                if (realDelay)
                {
                    mode = "RealDelay";

                    if (connected)
                    {
                        // The tunnel is up: the core measures the outbound itself.
                        delay = await AppServices.Connection.TestDelayAsync(profile.Id);
                    }
                    else
                    {
                        // Tunnel is down: run each profile briefly in proxy mode
                        // and time a real request through it, so the figure
                        // covers the whole tunnel. Also proves the server is
                        // actually reachable end to end.
                        delay = await ProxyLatencyProbe.ProbeAsync(spec, corePath, probeDirectory);
                    }
                }
                else
                {
                    mode = "TcpPing";

                    // A bare TCP connect to the server address: fast, direct,
                    // and with no tunnel involvement. For CDN-fronted
                    // addresses this measures the nearest edge node, not the
                    // server behind it.
                    delay = await TcpPing.MeasureAsync(spec.Server, spec.Port);
                }

                profile.LatencyMs = delay;
                profile.LatencyMode = delay is int ? mode : "";

                if (delay is int value)
                    logs.Debug($"Latency {profile.Name}: {value} ms ({mode})");
                else
                    logs.Warn($"Latency test failed for {profile.Name}.");
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(probes);
        Save();
        logs.Info("Latency test finished.");
    }
}
