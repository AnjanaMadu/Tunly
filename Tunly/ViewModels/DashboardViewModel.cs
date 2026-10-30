using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Tunly.Core;
using Tunly.Models;
using Tunly.Services;

namespace Tunly.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    public ConnectionService Connection { get; } = AppServices.Connection;
    public ObservableCollection<Profile> Profiles => AppServices.Profiles.Items;
    public AppSettings Settings => AppServices.Settings;

    public ObservableCollection<LogEntry> RecentLogs { get; } = new();

    public DashboardViewModel()
    {
        AppServices.Logs.EntryAdded += OnLogAdded;
        AppServices.Logs.Entries.CollectionChanged += OnLogsChanged;
        Connection.PropertyChanged += OnConnectionChanged;
        RefreshRecent();
    }

    void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionService.ActiveProfile))
        {
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(HasProfiles));
        }
    }

    public Profile? SelectedProfile
    {
        get => Connection.ActiveProfile;
        set
        {
            if (value is null || ReferenceEquals(value, Connection.ActiveProfile)) return;
            Connection.ActiveProfile = value;
            AppServices.Logs.Info($"Active profile set to {value.Name}.");
            OnPropertyChanged();
        }
    }

    public bool HasProfiles => Profiles.Count > 0;
    public Visibility ManageProfilesVisibility => HasProfiles ? Visibility.Collapsed : Visibility.Visible;

    public void RefreshRecent()
    {
        RecentLogs.Clear();
        foreach (var entry in System.Linq.Enumerable.Reverse(AppServices.Logs.Entries))
        {
            RecentLogs.Add(entry);
            if (RecentLogs.Count >= 6) break;
        }
    }

    void OnLogAdded(object? sender, LogEntry entry)
    {
        RecentLogs.Insert(0, entry);
        while (RecentLogs.Count > 6) RecentLogs.RemoveAt(RecentLogs.Count - 1);
    }

    void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (AppServices.Logs.Entries.Count == 0) RecentLogs.Clear();
    }
}
