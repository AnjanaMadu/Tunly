using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using Tunly.Core;
using Tunly.Models;
using Tunly.Services;

namespace Tunly.ViewModels;

public sealed class LogsViewModel : ObservableObject
{
    string _searchText = "";
    int _levelIndex = 2;
    bool _autoScroll = true;

    public LogService Logs { get; } = AppServices.Logs;
    public ObservableCollection<LogEntry> Visible { get; } = new();

    public LogsViewModel()
    {
        AppServices.Logs.EntryAdded += OnEntryAdded;
        AppServices.Logs.Entries.CollectionChanged += (_, _) =>
        {
            if (AppServices.Logs.Entries.Count == 0) Refresh();
        };
        Refresh();
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Refresh(); }
    }

    /// <summary>Minimum level: 0 = All, 1 = Debug, 2 = Info, 3 = Warning, 4 = Error.</summary>
    public int LevelIndex
    {
        get => _levelIndex;
        set { if (SetProperty(ref _levelIndex, value)) Refresh(); }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public bool IsEmpty => Visible.Count == 0;
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ListVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    public string CountText => $"{Visible.Count} entries";

    public void Refresh()
    {
        Visible.Clear();
        foreach (var entry in AppServices.Logs.Entries)
        {
            if (Matches(entry)) Visible.Add(entry);
        }
        RaiseSummary();
    }

    void OnEntryAdded(object? sender, LogEntry entry)
    {
        if (!Matches(entry)) return;
        Visible.Add(entry);
        RaiseSummary();
    }

    bool Matches(LogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(_searchText) &&
            !entry.Message.Contains(_searchText, System.StringComparison.OrdinalIgnoreCase) &&
            !entry.Source.Contains(_searchText, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var minimum = LevelIndex switch
        {
            1 => LogLevel.Debug,
            2 => LogLevel.Info,
            3 => LogLevel.Warn,
            4 => LogLevel.Error,
            _ => LogLevel.Trace
        };

        return entry.Level >= minimum;
    }

    void RaiseSummary()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(ListVisibility));
        OnPropertyChanged(nameof(CountText));
    }
}
