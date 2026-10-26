namespace Tunly.Core;

/// <summary>Well-known locations used by the engine.</summary>
public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tunly");

    public static string DefaultConfigDirectory => Path.Combine(AppDataDirectory, "config");

    public static string ResolveConfigDirectory(string configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultConfigDirectory : configured;

    public static string ResolveConfigFile(string configured) =>
        Path.Combine(ResolveConfigDirectory(configured), "config.json");

    public static string CoreDirectory => Path.Combine(AppDataDirectory, "core");

    public static string LogFile => Path.Combine(AppDataDirectory, "sing-box.log");

    public static string ProxyBackupFile => Path.Combine(AppDataDirectory, "proxy-backup.json");
}
