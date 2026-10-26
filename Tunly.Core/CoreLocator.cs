using System.Diagnostics;
using Tunly.Core.Models;

namespace Tunly.Core;

public sealed record CoreLocation(string ExecutablePath, bool HasWintun);

/// <summary>Finds the sing-box executable and reads its version.</summary>
public static class CoreLocator
{
    public static CoreLocation? Locate(string configuredPath)
    {
        foreach (var candidate in Candidates(configuredPath))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    var executable = ResolveShim(full);
                    return new CoreLocation(executable, HasWintun(executable));
                }
            }
            catch (Exception)
            {
                // Ignore malformed paths and keep looking.
            }
        }

        return null;
    }

    public static string? LocatePath(string configuredPath) => Locate(configuredPath)?.ExecutablePath;

    /// <summary>Finds a core shipped next to the app in <c>core\sing-box.exe</c>.</summary>
    public static CoreLocation? LocateBundled(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory)) return null;

        try
        {
            var path = Path.GetFullPath(Path.Combine(baseDirectory, "core", "sing-box.exe"));
            return File.Exists(path) ? new CoreLocation(path, HasWintun(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool HasWintun(string executablePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrEmpty(directory) &&
                File.Exists(Path.Combine(directory, "wintun.dll")))
            {
                return true;
            }

            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(system) &&
                File.Exists(Path.Combine(system, "wintun.dll")))
            {
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Scoop (and similar) put a launcher shim on PATH. Running through the shim hides
    /// the real process, so resolve it to the actual sing-box.exe when possible.
    /// </summary>
    static string ResolveShim(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) ||
                !Path.GetFileName(directory).Equals("shims", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            var root = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(root)) return path;

            var real = Path.Combine(root, "apps", "sing-box", "current", "sing-box.exe");
            return File.Exists(real) ? real : path;
        }
        catch (Exception)
        {
            return path;
        }
    }

    static IEnumerable<string> Candidates(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            yield return configuredPath;

        if (Directory.Exists(AppPaths.CoreDirectory))
        {
            foreach (var directory in Directory
                .EnumerateDirectories(AppPaths.CoreDirectory)
                .OrderByDescending(Path.GetFileName))
            {
                yield return Path.Combine(directory, "sing-box.exe");
            }
        }

        yield return Path.Combine(AppPaths.CoreDirectory, "sing-box.exe");

        // The portable release ships an official core under <app>\core.
        var bundled = LocateBundled(AppContext.BaseDirectory);
        if (bundled is not null) yield return bundled.ExecutablePath;

        yield return Path.Combine(AppContext.BaseDirectory, "sing-box.exe");

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0) continue;
            yield return Path.Combine(trimmed, "sing-box.exe");
        }
    }

    /// <summary>Runs <c>sing-box version</c> and parses the result.</summary>
    public static async Task<CoreVersion> ReadVersionAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath))
            throw new EngineException($"sing-box was not found at \"{executablePath}\".");

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("version");

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
                throw new EngineException("Could not start the sing-box process.");
        }
        catch (Exception ex) when (ex is not EngineException)
        {
            throw new EngineException($"Could not run \"{executablePath}\": {ex.Message}", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new EngineException("sing-box did not report its version in time.");
        }

        var output = (await stdout) + "\n" + (await stderr);
        var version = CoreVersion.TryParse(output);
        if (version is null)
        {
            throw new EngineException(
                $"Could not determine the sing-box version. Output: {FirstLines(output, 4)}");
        }

        return version;
    }

    public static async Task<CoreVersion> RequireSupportedAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var version = await ReadVersionAsync(executablePath, cancellationToken);
        if (!version.AtLeast(1, 12))
        {
            throw new EngineException(
                $"sing-box 1.12 or newer is required, but {version} was found. " +
                "Update the core in Settings → Core & logs.");
        }

        return version;
    }

    static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Ignore.
        }
    }

    static string FirstLines(string text, int count) =>
        string.Join(" | ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(count));
}
