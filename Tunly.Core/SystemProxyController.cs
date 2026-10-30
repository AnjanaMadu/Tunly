using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Win32;

namespace Tunly.Core;

/// <summary>
/// Applies and restores the Windows (WinINET) system proxy while the tunnel is running.
/// A backup file is written before the first change so a crash cannot leave the
/// machine pointed at a dead port.
/// </summary>
public sealed class SystemProxyController
{
    const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    const int InternetOptionRefresh = 37;
    const int InternetOptionSettingsChanged = 39;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    readonly string _backupPath;

    public SystemProxyController(string backupPath)
    {
        _backupPath = backupPath;
    }

    public bool HasBackup => File.Exists(_backupPath);

    public void Enable(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        if (!HasBackup)
            Snapshot();

        using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Internet Settings registry key.");

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{port}");
        key.SetValue("ProxyOverride", "<local>");

        Notify();
    }

    public void Disable()
    {
        if (!HasBackup) return;

        ProxyBackup? backup = null;
        try
        {
            backup = JsonSerializer.Deserialize<ProxyBackup>(File.ReadAllText(_backupPath), Json);
        }
        catch (Exception)
        {
            // Fall back to disabling the proxy rather than leaving the machine pointed at a dead port.
        }

        using (var key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey, writable: true))
        {
            if (key is not null)
            {
                if (backup is null)
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }
                else
                {
                    if (backup.ProxyEnable is int enabled)
                        key.SetValue("ProxyEnable", enabled, RegistryValueKind.DWord);

                    RestoreString(key, "ProxyServer", backup.ProxyServer);
                    RestoreString(key, "ProxyOverride", backup.ProxyOverride);
                }
            }
        }

        try { File.Delete(_backupPath); }
        catch (IOException) { /* Leave the backup for the next start. */ }

        Notify();
    }

    /// <summary>Restores a backup left behind by a previous crash.</summary>
    public void RestoreIfStale()
    {
        if (!HasBackup) return;

        try { Disable(); }
        catch (Exception) { /* Never block startup on proxy cleanup. */ }
    }

    void Snapshot()
    {
        ProxyBackup backup;

        using (var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: false))
        {
            backup = new ProxyBackup(
                ReadInt(key, "ProxyEnable"),
                key?.GetValue("ProxyServer") as string,
                key?.GetValue("ProxyOverride") as string);
        }

        var directory = Path.GetDirectoryName(_backupPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(_backupPath, JsonSerializer.Serialize(backup, Json));
    }

    static int? ReadInt(RegistryKey? key, string name)
    {
        var value = key?.GetValue(name);
        return value switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    static void RestoreString(RegistryKey key, string name, string? value)
    {
        if (value is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value, RegistryValueKind.String);
    }

    static void Notify()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int bufferLength);

    sealed record ProxyBackup(int? ProxyEnable, string? ProxyServer, string? ProxyOverride);
}
