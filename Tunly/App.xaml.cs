using Microsoft.UI.Xaml;
using Tunly.Core;
using Tunly.Services;

namespace Tunly;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            AppServices.Logs.Error(e.Exception.ToString(), "app");

            // The job object kills sing-box when this process dies, but ask nicely first.
            _ = AppServices.Connection.ShutdownAsync();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.EnsureInitialized();

        var window = new MainWindow();
        MainWindow = window;
        window.Activate();

        AppServices.Connection.RestoreSession();
    }
}
