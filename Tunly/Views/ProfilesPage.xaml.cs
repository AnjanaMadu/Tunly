using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Tunly.Core;
using Tunly.Models;
using Tunly.Services;
using Tunly.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tunly.Views;

public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesPage()
    {
        ViewModel = new ProfilesViewModel();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += (_, _) => ProfileList.SelectedItem = AppServices.Connection.ActiveProfile;
    }

    void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ViewModel.SearchText = sender.Text;
    }

    void OnSortChanged(object sender, SelectionChangedEventArgs e) => ViewModel.SortIndex = SortBox.SelectedIndex;

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is Profile profile) ViewModel.SetActive(profile);
    }

    void OnListDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is Profile profile) _ = ShowEditorAsync(profile);
    }

    void OnAddProfile(object sender, RoutedEventArgs e) => _ = ShowEditorAsync(null);

    async void OnTestAll(object sender, RoutedEventArgs e)
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
        ViewModel.SetActive(profiles[^1]);
        AppServices.Logs.Info(profiles.Count == 1
            ? $"Profile imported from clipboard: {profiles[0].Name}."
            : $"{profiles.Count} profiles imported from the clipboard.");
    }

    async Task ShowEditorAsync(Profile? existing)
    {
        var dialog = new ProfileEditorDialog(existing) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;

        if (existing is null)
        {
            AppServices.Profiles.Add(dialog.Result);
            AppServices.Logs.Info($"Profile added: {dialog.Result.Name}.");
            ViewModel.SetActive(dialog.Result);
        }
        else
        {
            existing.CopyFrom(dialog.Result);
            AppServices.Profiles.Save();
            AppServices.Logs.Info($"Profile updated: {existing.Name}.");
        }

        ViewModel.Refresh();
        ProfileList.SelectedItem = existing ?? dialog.Result;
    }

    void OnRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not Profile profile) return;

        var flyout = new MenuFlyout();

        var edit = new MenuFlyoutItem { Text = "Edit", Icon = new FontIcon { Glyph = "\uE70F" } };
        edit.Click += (_, _) => _ = ShowEditorAsync(profile);

        var activate = new MenuFlyoutItem { Text = "Set as active", Icon = new FontIcon { Glyph = "\uE73E" } };
        activate.Click += (_, _) => ViewModel.SetActive(profile);

        var copy = new MenuFlyoutItem { Text = "Copy address", Icon = new FontIcon { Glyph = "\uE8C8" } };
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(profile.Endpoint);
            Clipboard.SetContent(package);
        };

        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
        delete.Click += (_, _) => _ = ConfirmDeleteAsync(profile);

        flyout.Items.Add(edit);
        flyout.Items.Add(activate);
        flyout.Items.Add(copy);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);

        flyout.ShowAt(element);
    }

    async Task ConfirmDeleteAsync(Profile profile)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete profile?",
            Content = new TextBlock
            {
                Text = $"\"{profile.Name}\" will be removed from this device.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary) ViewModel.Delete(profile);
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
