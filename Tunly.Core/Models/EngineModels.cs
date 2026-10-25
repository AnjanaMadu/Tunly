using System.Text.RegularExpressions;

namespace Tunly.Core.Models;

public sealed record ClashEndpoint(string Host, int Port, string Secret)
{
    public string Controller => $"{Host}:{Port}";
    public Uri BaseAddress => new($"http://{Host}:{Port}");
}

public readonly record struct TrafficSample(long UpBytesPerSecond, long DownBytesPerSecond);

public enum EngineLogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error
}

public sealed record EngineLog(EngineLogLevel Level, string Message, string Raw);

public sealed record EngineExit(int ExitCode, IReadOnlyList<string> RecentLines);

public sealed record EngineStartRequest(
    IReadOnlyList<OutboundSpec> Profiles,
    string ActiveProfileId,
    TunOptions Options);

public sealed class EngineException : Exception
{
    public EngineException(string message) : base(message) { }
    public EngineException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Version of a sing-box binary, parsed from <c>sing-box version</c>.</summary>
public sealed partial record CoreVersion(int Major, int Minor, int Patch, string Raw)
{
    public bool AtLeast(int major, int minor) =>
        Major > major || (Major == major && Minor >= minor);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static CoreVersion? TryParse(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        var match = VersionPattern().Match(output);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return null;
        }

        return new CoreVersion(major, minor, patch, firstLine);
    }

    [GeneratedRegex(@"(?<!\d)(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
