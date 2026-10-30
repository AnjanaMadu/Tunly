using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using Tunly.Core;
using Tunly.Services;
using Tunly.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tunly.Views;

public sealed partial class LogsPage : Page
{
    public LogsViewModel ViewModel { get; }

    public LogsPage()
    {
        ViewModel = new LogsViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        ViewModel.Visible.CollectionChanged += OnVisibleChanged;
    }

    void OnVisibleChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!ViewModel.AutoScroll || e.Action != NotifyCollectionChangedAction.Add) return;

        var last = ViewModel.Visible.LastOrDefault();
        if (last is null) return;

        DispatcherQueue.TryEnqueue(() => LogList.ScrollIntoView(last));
    }

    void OnLevelChanged(object sender, SelectionChangedEventArgs e) => ViewModel.LevelIndex = LevelBox.SelectedIndex;

    void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ViewModel.SearchText = sender.Text;
    }

    void OnClear(object sender, RoutedEventArgs e) => AppServices.Logs.Clear();

    void OnCopy(object sender, RoutedEventArgs e)
    {
        var builder = new StringBuilder();
        foreach (var entry in ViewModel.Visible)
        {
            builder.Append(entry.TimeText).Append("  ").Append(entry.LevelText).Append("  ").AppendLine(entry.Message);
        }

        var package = new DataPackage();
        package.SetText(builder.ToString());
        Clipboard.SetContent(package);
    }

    void OnOpenLogFile(object sender, RoutedEventArgs e)
    {
        var directory = AppServices.SettingsService.AppDataDirectory;
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "sing-box.log");
        if (!File.Exists(file)) File.WriteAllText(file, "");
        SystemHelper.OpenFile(file);
    }
}
