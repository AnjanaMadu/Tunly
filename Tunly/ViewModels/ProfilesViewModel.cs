using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Tunly.Core;
using Tunly.Models;

namespace Tunly.ViewModels;

public sealed class ProfilesViewModel : ObservableObject
{
    string _searchText = "";
    int _sortIndex;
    readonly ObservableCollection<Profile> _visible = new();

    public ObservableCollection<Profile> Items => AppServices.Profiles.Items;
    public ObservableCollection<Profile> Visible => _visible;

    public ProfilesViewModel()
    {
        AppServices.Profiles.Items.CollectionChanged += OnCollectionChanged;
        Refresh();
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Refresh(); }
    }

    /// <summary>0 = Name, 1 = Latency, 2 = Protocol.</summary>
    public int SortIndex
    {
        get => _sortIndex;
        set { if (SetProperty(ref _sortIndex, value)) Refresh(); }
    }

    public bool IsEmpty => _visible.Count == 0;
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ListVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    public string EmptyTitle => Items.Count == 0 ? "No profiles yet" : "No matches";
    public string EmptyDescription => Items.Count == 0
        ? "Add a profile manually or paste a share link to get started."
        : "Try a different search term or clear the filter.";

    public void Refresh()
    {
        var query = Items.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var term = _searchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Address.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Protocol.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = SortIndex switch
        {
            1 => query.OrderBy(p => p.LatencyMs ?? int.MaxValue),
            2 => query.OrderBy(p => p.Protocol).ThenBy(p => p.Name),
            _ => query.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        _visible.Clear();
        foreach (var p in query) _visible.Add(p);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(ListVisibility));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    public void Save(Profile edited, bool isNew)
    {
        if (isNew)
        {
            AppServices.Profiles.Add(edited);
            AppServices.Logs.Info($"Profile added: {edited.Name}.");
        }
        else
        {
            AppServices.Logs.Info($"Profile updated: {edited.Name}.");
        }
        Refresh();
    }

    public void Delete(Profile profile)
    {
        if (ReferenceEquals(AppServices.Connection.ActiveProfile, profile))
        {
            _ = AppServices.Connection.DisconnectAsync();
            AppServices.Connection.ActiveProfile = null;
        }
        AppServices.Profiles.Remove(profile);
        AppServices.Logs.Info($"Profile deleted: {profile.Name}.");
        Refresh();
    }

    public void SetActive(Profile profile)
    {
        AppServices.Connection.ActiveProfile = profile;
        AppServices.Logs.Info($"Active profile set to {profile.Name}.");
    }
}
