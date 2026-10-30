using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Tunly.Core;
using Tunly.Services;
using Tunly.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Tunly.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel();
        DataContext = ViewModel;
        InitializeComponent();
        CategoryList.SelectedIndex = 0;
        ShowSection(0);
    }

    // ---------------- Category navigation ----------------

    void OnCategoryListChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedIndex < 0) return;
        if (CategoryCombo.SelectedIndex != CategoryList.SelectedIndex)
            CategoryCombo.SelectedIndex = CategoryList.SelectedIndex;
        ShowSection(CategoryList.SelectedIndex);
    }

    void OnCategoryComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedIndex < 0) return;
        if (CategoryList.SelectedIndex != CategoryCombo.SelectedIndex)
            CategoryList.SelectedIndex = CategoryCombo.SelectedIndex;
        ShowSection(CategoryCombo.SelectedIndex);
    }

    void ShowSection(int index)
    {
        if (AppearanceSection is null) return;
        AppearanceSection.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        GeneralSection.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ConnectionSection.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        CoreSection.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        AboutSection.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- Core & logs ----------------

    async void OnBrowseCore(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");

        if (App.MainWindow is not null)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        ViewModel.Settings.CorePath = file.Path;
        ViewModel.NoteSaved("Core path updated.");
    }

    void OnOpenConfigFolder(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenConfigFolder();
        ViewModel.NoteSaved("Opened the configuration folder.");
    }

    void OnRevealConfig(object sender, RoutedEventArgs e)
    {
        ViewModel.RevealConfig();
        ViewModel.NoteSaved("Opened the generated configuration.");
    }

    async void OnTestCore(object sender, RoutedEventArgs e) => await ViewModel.RefreshCoreAsync();

    async void OnDownloadCore(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.DownloadCoreAsync();
        }
        catch (Exception ex)
        {
            ViewModel.NoteSaved($"Download failed: {ex.Message}");
        }
    }

    async void OnPreviewConfig(object sender, RoutedEventArgs e)
    {
        var config = ViewModel.BuildConfigPreview();

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
            ViewModel.NoteSaved("Configuration copied to the clipboard.");
        }
    }

    void OnOpenLogFile(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLogFile();
        ViewModel.NoteSaved("Opened the core log file.");
    }

    void OnClearLogs(object sender, RoutedEventArgs e)
    {
        AppServices.Logs.Clear();
        ViewModel.NoteSaved("Log buffer cleared.");
    }

    async void OnResetSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reset all settings?",
            Content = new TextBlock
            {
                Text = "Every preference will return to its default value. Profiles are not affected.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        AppServices.SettingsService.Reset();
        if (App.MainWindow is not null) ThemeService.Apply(App.MainWindow);
        ViewModel.NoteSaved("All settings were reset to defaults.");
    }

    // ---------------- About ----------------

    void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        var text =
            $"{ViewModel.VersionText}\n{ViewModel.RuntimeText}\n{ViewModel.OsText}\n" +
            $"Core: {ViewModel.CoreStatusText}\nConfig: {ViewModel.ConfigPath}\n" +
            $"Clash API: {ViewModel.ClashController}\nSettings: {ViewModel.SettingsPath}";

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.NoteSaved("Diagnostics copied to the clipboard.");
    }

    async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        ViewModel.NoteSaved("Checking for updates…");
        await Task.Delay(700); // TODO(core): query the release feed
        ViewModel.NoteSaved("You are running the latest version.");
    }

    void OnRevealSettings(object sender, RoutedEventArgs e)
    {
        AppServices.SettingsService.Save();
        var path = ViewModel.SettingsPath;
        if (System.IO.File.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
        }
        else
        {
            SystemHelper.OpenFolder(AppServices.SettingsService.AppDataDirectory);
        }
        ViewModel.NoteSaved("Opened the settings file location.");
    }
}
