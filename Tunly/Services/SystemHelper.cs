using System.Diagnostics;
using System.Security.Principal;

namespace Tunly.Services;

public static class SystemHelper
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = System.Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas"
            });
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch
        {
            // The user dismissed the UAC prompt.
        }
    }

    public static void OpenFolder(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public static void OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
