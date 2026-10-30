using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tunly.Core;
using Windows.UI;

namespace Tunly.Services;

/// <summary>Applies appearance settings (theme, backdrop, surface colour) to the shell.</summary>
public static class ThemeService
{
    static readonly SolidColorBrush DarkSurface = new(Color.FromArgb(255, 32, 32, 32));
    static readonly SolidColorBrush LightSurface = new(Color.FromArgb(255, 243, 243, 243));

    public static void Apply(Window window)
    {
        var settings = AppServices.Settings;

        window.SystemBackdrop = settings.Backdrop switch
        {
            "Mica" => new MicaBackdrop(),
            "Acrylic" => new DesktopAcrylicBackdrop(),
            _ => null
        };

        if (window.Content is not Panel root) return;

        root.RequestedTheme = settings.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        var useSystemTheme = settings.Theme == "System";
        var systemDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var isDark = settings.Theme == "Dark" || (useSystemTheme && systemDark);

        root.Background = settings.Backdrop == "Solid"
            ? (isDark ? DarkSurface : LightSurface)
            : new SolidColorBrush(Colors.Transparent);
    }

    public static void ApplyAlwaysOnTop(Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = AppServices.Settings.AlwaysOnTop;
        }
    }
}
