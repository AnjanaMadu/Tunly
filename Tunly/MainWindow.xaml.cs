using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Tunly.Core;
using Tunly.Models;
using Tunly.Services;
using Tunly.ViewModels;
using Tunly.Views;
using Windows.Graphics;

namespace Tunly;

public sealed partial class MainWindow : Window
{
    readonly DispatcherQueueTimer _boundsTimer;
    readonly ShellViewModel _viewModel = new();

    TrayIconService? _trayIcon;
    bool _realClose;

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.DataContext = _viewModel;

        Title = "Tunly";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        }
        catch (Exception)
        {
            // The icon is cosmetic; never block startup.
        }

        _boundsTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _boundsTimer.Interval = TimeSpan.FromMilliseconds(400);
        _boundsTimer.IsRepeating = false;
        _boundsTimer.Tick += (_, _) => SaveWindowState();

        ConfigureWindow();
        ApplyPaneMode();
        ThemeService.Apply(this);
        ThemeService.ApplyAlwaysOnTop(this);

        AppServices.Settings.PropertyChanged += OnSettingsChanged;
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnAppWindowClosing;

        if (AppServices.Settings.CloseToTray) CreateTrayIcon();

        Closed += (_, _) =>
        {
            SaveWindowState();
            DisposeTrayIcon();
            _ = AppServices.Connection.ShutdownAsync();
        };

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    void ConfigureWindow()
    {
        var settings = AppServices.Settings;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 880;
            presenter.PreferredMinimumHeight = 620;
        }

        if (settings.RememberWindow && settings.WindowWidth >= 880 && settings.WindowHeight >= 620)
        {
            AppWindow.Resize(new SizeInt32((int)settings.WindowWidth, (int)settings.WindowHeight));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1400, 780));
        }

        if (settings.WindowMaximized || settings.LaunchMaximized)
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter2) presenter2.Maximize();
        }
    }

    void ApplyPaneMode()
    {
        if (AppServices.Settings.CompactPane)
        {
            Nav.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
        }
        else if (Nav.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftCompact)
        {
            Nav.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
        }
    }

    void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Theme):
            case nameof(AppSettings.Backdrop):
                ThemeService.Apply(this);
                break;
            case nameof(AppSettings.AlwaysOnTop):
                ThemeService.ApplyAlwaysOnTop(this);
                break;
            case nameof(AppSettings.CompactPane):
                ApplyPaneMode();
                break;
            case nameof(AppSettings.LaunchMaximized):
                if (AppServices.Settings.LaunchMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
                break;
            case nameof(AppSettings.CloseToTray):
                if (AppServices.Settings.CloseToTray) CreateTrayIcon();
                else DisposeTrayIcon();
                break;
        }
    }

    // ---------------- System tray ----------------

    void CreateTrayIcon()
    {
        if (_trayIcon is not null) return;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            var tray = TrayIconService.Create(
                "Tunly",
                File.Exists(iconPath) ? iconPath : null,
                Environment.ProcessPath);

            if (tray is null)
            {
                AppServices.Logs.Warn("The notification-area icon is unavailable.");
                return;
            }

            tray.ActivateRequested += ShowFromTray;
            tray.ExitRequested += ExitFromTray;
            _trayIcon = tray;
        }
        catch (Exception ex)
        {
            AppServices.Logs.Warn($"Could not create the notification-area icon: {ex.Message}");
        }
    }

    void DisposeTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        Activate();
        AppServices.Logs.Info("Tunly resumed from the notification area.");
    }

    void ExitFromTray()
    {
        AppServices.Logs.Info("Tunly exited from the notification area.");
        _realClose = true;
        DisposeTrayIcon();
        Close();
    }

    void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Only park in the tray when the icon actually exists; otherwise the
        // app would otherwise vanish with no way back in.
        if (_realClose || !AppServices.Settings.CloseToTray || _trayIcon is null) return;

        args.Cancel = true;
        sender.Hide();
        AppServices.Logs.Info("Tunly keeps running in the notification area.");
    }

    void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange && !args.DidPositionChange) return;
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    void SaveWindowState()
    {
        var settings = AppServices.Settings;
        if (!settings.RememberWindow) return;

        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            settings.WindowMaximized = true;
            return;
        }

        var size = AppWindow.Size;
        settings.WindowMaximized = false;
        if (size.Width >= 880 && size.Height >= 620)
        {
            settings.WindowWidth = size.Width;
            settings.WindowHeight = size.Height;
        }
    }

    // ---------------- Navigation ----------------

    void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }) Navigate(tag);
    }

    public void NavigateTo(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));

        if (item is not null) Nav.SelectedItem = item;
        else Navigate(tag);
    }

    void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "profiles" => typeof(ProfilesPage),
            "logs" => typeof(LogsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        if (ContentFrame.CurrentSourcePageType == pageType) return;
        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }
}
