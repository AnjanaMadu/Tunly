using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Tunly.Core;
using Tunly.Services;
using Tunly.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tunly.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = new DashboardViewModel();
        DataContext = ViewModel;
        InitializeComponent();
    }

    async void OnConnectClick(object sender, RoutedEventArgs e) => await ViewModel.Connection.ToggleAsync();

    void OnDismissError(InfoBar sender, object args) => ViewModel.Connection.ClearError();

    void OnRestartAdmin(object sender, RoutedEventArgs e) => SystemHelper.RestartElevated();

    void OnManageProfiles(object sender, RoutedEventArgs e) => Navigate("profiles");

    void OnOpenLogs(object sender, RoutedEventArgs e) => Navigate("logs");

    void OnOpenSettings(object sender, RoutedEventArgs e) => Navigate("settings");

    static void Navigate(string tag)
    {
        if (App.MainWindow is MainWindow window) window.NavigateTo(tag);
    }

    async void OnTestLatency(object sender, RoutedEventArgs e)
        => await AppServices.Profiles.TestAllAsync(AppServices.Logs);

    async void OnImportLink(object sender, RoutedEventArgs e)
    {
        var profiles = await ProfileImporter.FromClipboardManyAsync();
        if (profiles.Count == 0)
        {
            await ShowMessage("Nothing to import", "The clipboard does not contain a supported share link (vless, vmess, trojan, ss, hysteria2, tuic or a subscription).");
            return;
        }

        foreach (var profile in profiles) AppServices.Profiles.Add(profile);
        ViewModel.Connection.ActiveProfile = profiles[^1];
        AppServices.Logs.Info(profiles.Count == 1
            ? $"Profile imported from clipboard: {profiles[0].Name}."
            : $"{profiles.Count} profiles imported from the clipboard.");
        Navigate("profiles");
    }

    async void OnPreviewConfig(object sender, RoutedEventArgs e)
    {
        if (AppServices.Profiles.Items.Count == 0)
        {
            await ShowMessage("No profiles", "Add a profile before previewing the configuration.");
            return;
        }

        var config = ConfigBuilder.Build(
            ViewModel.Connection.ActiveProfile ?? AppServices.Profiles.Items[0],
            ViewModel.Settings);

        var text = new TextBox
        {
            Text = config,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            Height = 380,
            IsSpellCheckEnabled = false
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Generated configuration (preview)",
            Content = text,
            PrimaryButtonText = "Copy",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            MinWidth = 560
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var package = new DataPackage();
            package.SetText(config);
            Clipboard.SetContent(package);
        }
    }

    async Task ShowMessage(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
    }
}
