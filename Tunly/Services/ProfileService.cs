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
        foreach (var p in Items) p.LatencyMs = null;
        logs.Info("Testing latency…");

        var connected = AppServices.Connection.IsConnected;
        using var gate = new SemaphoreSlim(4);

        var probes = Items.ToList().Select(async profile =>
        {
            await gate.WaitAsync();
            try
            {
                // Raw TCP probes are only meaningful while the tunnel is down; when it is
                // up, the Clash API measures the outbound itself.
                var delay = connected
                    ? await AppServices.Connection.TestDelayAsync(profile.Id)
                    : await LatencyProbe.TcpAsync(profile.Address, profile.Port, 3000);

                profile.LatencyMs = delay;

                if (delay is int value)
                    logs.Debug($"Latency {profile.Name}: {value} ms");
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
