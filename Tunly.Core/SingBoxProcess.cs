using System.Diagnostics;
using System.Text;
using Tunly.Core.Models;
using Tunly.Core.Support;

namespace Tunly.Core;

/// <summary>Owns one <c>sing-box run</c> child process and streams its output.</summary>
public sealed class SingBoxProcess : IAsyncDisposable
{
    const int MaxRecentLines = 200;

    readonly object _gate = new();
    readonly Queue<string> _recent = new();
    Process? _process;
    NativeJob? _job;
    volatile bool _stopping;

    public event EventHandler<EngineLog>? LogReceived;
    public event EventHandler<EngineExit>? Exited;

    public bool IsRunning
    {
        get
        {
            var process = _process;
            try
            {
                return process is not null && !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public IReadOnlyList<string> RecentLines
    {
        get
        {
            lock (_gate) return _recent.ToArray();
        }
    }

    public void Start(string executablePath, string configPath, string workingDirectory)
    {
        if (!File.Exists(executablePath))
            throw new EngineException($"sing-box was not found at \"{executablePath}\".");

        _stopping = false;
        Directory.CreateDirectory(workingDirectory);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) => HandleLine(args.Data);
        process.ErrorDataReceived += (_, args) => HandleLine(args.Data);
        process.Exited += (_, _) =>
        {
            if (_stopping) return;

            var exitCode = -1;
            try { exitCode = process.ExitCode; } catch (InvalidOperationException) { }

            Exited?.Invoke(this, new EngineExit(exitCode, RecentLines));
        };

        try
        {
            if (!process.Start())
                throw new EngineException("Could not start the sing-box process.");
        }
        catch (Exception ex) when (ex is not EngineException)
        {
            throw new EngineException($"Could not start \"{executablePath}\": {ex.Message}", ex);
        }

        // Kill the whole tree if this app exits without stopping the tunnel.
        _job = new NativeJob();
        _job.Assign(process.Handle);

        _process = process;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    void HandleLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var trimmed = line.TrimEnd();

        lock (_gate)
        {
            _recent.Enqueue(trimmed);
            while (_recent.Count > MaxRecentLines) _recent.Dequeue();
        }

        var level = ParseLevel(trimmed);
        LogReceived?.Invoke(this, new EngineLog(level, trimmed, trimmed));
    }

    internal static EngineLogLevel ParseLevel(string line)
    {
        // sing-box log lines look like:
        //   +0800 2026-10-30 12:00:00 INFO router: ...
        if (line.Contains("FATAL", StringComparison.Ordinal) ||
            line.Contains("ERROR", StringComparison.Ordinal) ||
            line.Contains("ERR ", StringComparison.Ordinal))
        {
            return EngineLogLevel.Error;
        }

        if (line.Contains("WARN", StringComparison.Ordinal))
            return EngineLogLevel.Warn;

        if (line.Contains("DEBUG", StringComparison.Ordinal))
            return EngineLogLevel.Debug;

        if (line.Contains("TRACE", StringComparison.Ordinal))
            return EngineLogLevel.Trace;

        return EngineLogLevel.Info;
    }

    /// <summary>Runs <c>sing-box check</c> against a config file.</summary>
    public static async Task<string?> CheckConfigAsync(
        string executablePath,
        string configPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add("check");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
                throw new EngineException("Could not run sing-box check.");
        }
        catch (Exception ex) when (ex is not EngineException)
        {
            throw new EngineException($"Could not run sing-box check: {ex.Message}", ex);
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Ignore.
            }

            throw new EngineException("sing-box check did not finish in time.");
        }

        var text = string.Join(Environment.NewLine, new[] { await output, await error }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));

        return process.ExitCode == 0 ? null : text;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        var process = _process;
        if (process is null) return;

        _stopping = true;

        try
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception) { /* Already exiting. */ }

                using var cancellation = new CancellationTokenSource(timeout);
                try { await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* Job cleanup below. */ }
            }
        }
        catch (InvalidOperationException)
        {
            // Process already gone.
        }
        finally
        {
            // Closing the job handle terminates anything that survived the kill.
            _job?.Dispose();
            _job = null;

            try { process.Dispose(); }
            catch (Exception) { /* Ignore. */ }

            _process = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }
}
