using System.Collections.Generic;
using System.Linq;
using Tunly.Core;
using Tunly.Core.Models;
using Tunly.Models;

namespace Tunly.Services;

/// <summary>
/// Builds the real sing-box configuration used by the engine (and by the preview
/// dialogs). Invalid or unsupported profiles are skipped so one bad entry does
/// not break the preview.
/// </summary>
public static class ConfigBuilder
{
    const string EmptyPreview = "{\n  \"error\": \"Add a valid profile to generate a configuration.\"\n}";

    public static string Build(Profile? profile, AppSettings settings)
    {
        var profiles = AppServices.Profiles.Items.ToList();
        if (profile is not null && profiles.All(p => p.Id != profile.Id))
            profiles.Add(profile);

        return Build(profiles, profile, settings);
    }

    public static string Build(IReadOnlyList<Profile> profiles, Profile? active, AppSettings settings)
    {
        var specs = new List<OutboundSpec>();
        foreach (var profile in profiles)
        {
            try
            {
                var spec = ProfileMapper.ToSpec(profile);
                if (spec.Validate().Count == 0) specs.Add(spec);
            }
            catch (EngineException)
            {
                // Unsupported protocol: skip it in the preview.
            }
        }

        if (specs.Count == 0) return EmptyPreview;

        var activeId = active is not null && specs.Any(s => s.Id == active.Id)
            ? active.Id
            : specs[0].Id;

        var options = TunOptionsMapper.From(settings);
        var port = settings.ClashPort > 0 ? settings.ClashPort : 9090;
        var version = AppServices.Connection.CoreVersion ?? new CoreVersion(1, 14, 4, "preview");

        var config = SingBoxConfigFactory.Build(
            specs,
            activeId,
            options,
            new ClashEndpoint("127.0.0.1", port, "preview"),
            version);

        return SingBoxConfigFactory.Serialize(config);
    }
}
