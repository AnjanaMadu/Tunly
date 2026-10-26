using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tunly.Core.Models;

namespace Tunly.Core;

public sealed record CoreInstallProgress(string Phase, double Fraction);

public sealed record CoreInstallResult(string ExecutablePath, CoreVersion Version);

/// <summary>Downloads the latest official sing-box Windows build from GitHub Releases.</summary>
public sealed class CoreInstaller
{
    const string LatestReleaseUrl = "https://api.github.com/repos/SagerNet/sing-box/releases/latest";

    public async Task<CoreInstallResult> InstallLatestAsync(
        string destinationRoot,
        IProgress<CoreInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationRoot);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Tunly/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        progress?.Report(new CoreInstallProgress("Checking the latest release", 0.02));

        using var releaseResponse = await http
            .GetAsync(LatestReleaseUrl, cancellationToken)
            .ConfigureAwait(false);

        if (!releaseResponse.IsSuccessStatusCode)
        {
            var body = await releaseResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var hint = (int)releaseResponse.StatusCode == 403
                ? "GitHub is rate limiting the request. Try again later or set the core path manually."
                : $"GitHub returned {(int)releaseResponse.StatusCode} {releaseResponse.ReasonPhrase}.";
            throw new EngineException($"{hint} {Trim(body)}");
        }

        using var release = JsonDocument.Parse(
            await releaseResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? throw new EngineException("The release has no tag name.");
        var versionText = tag.TrimStart('v', 'V');

        var assetName = $"sing-box-{versionText}-windows-{ArchitectureName()}.zip";
        string? downloadUrl = null;

        if (release.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) continue;

                downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new EngineException($"Release {tag} does not contain {assetName}.");

        var downloadPath = Path.Combine(destinationRoot, "sing-box-download.zip");
        await DownloadAsync(http, downloadUrl, downloadPath, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new CoreInstallProgress("Extracting", 0.9));

        var extractDirectory = Path.Combine(destinationRoot, "extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(downloadPath, extractDirectory, overwriteFiles: true);

            var sourceExe = Directory
                .EnumerateFiles(extractDirectory, "sing-box.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new EngineException("The downloaded archive does not contain sing-box.exe.");

            var sourceDirectory = Path.GetDirectoryName(sourceExe)!;
            var installDirectory = Path.Combine(destinationRoot, versionText);
            Directory.CreateDirectory(installDirectory);

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var destination = Path.Combine(installDirectory, Path.GetFileName(file));
                File.Copy(file, destination, overwrite: true);
            }

            var executable = Path.Combine(installDirectory, "sing-box.exe");
            progress?.Report(new CoreInstallProgress("Verifying", 0.96));

            var version = await CoreLocator
                .ReadVersionAsync(executable, cancellationToken)
                .ConfigureAwait(false);

            if (!version.AtLeast(1, 12))
                throw new EngineException($"The downloaded core is too old ({version}). sing-box 1.12+ is required.");

            progress?.Report(new CoreInstallProgress("Done", 1.0));
            return new CoreInstallResult(executable, version);
        }
        finally
        {
            TryDelete(downloadPath);
            TryDeleteDirectory(extractDirectory);
        }
    }

    static async Task DownloadAsync(
        HttpClient http,
        string url,
        string destination,
        IProgress<CoreInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long copied = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;

            if (total is > 0)
            {
                var fraction = 0.05 + 0.85 * copied / total.Value;
                progress?.Report(new CoreInstallProgress("Downloading", fraction));
            }
        }
    }

    static string ArchitectureName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "386",
        _ => throw new EngineException(
            $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}.")
    };

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }

    static string Trim(string body) => body.Length <= 300 ? body : body[..300] + "…";
}
