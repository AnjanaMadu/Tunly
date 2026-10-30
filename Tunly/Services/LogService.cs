using System;
using System.Collections.ObjectModel;
using Tunly.Models;

namespace Tunly.Services;

public sealed class LogService
{
    public const int MaxEntries = 2000;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public event EventHandler<LogEntry>? EntryAdded;

    public void Add(LogLevel level, string message, string source = "app")
    {
        var entry = new LogEntry { Level = level, Message = message, Source = source };
        Entries.Add(entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        EntryAdded?.Invoke(this, entry);
    }

    public void Trace(string message, string source = "app") => Add(LogLevel.Trace, message, source);
    public void Debug(string message, string source = "app") => Add(LogLevel.Debug, message, source);
    public void Info(string message, string source = "app") => Add(LogLevel.Info, message, source);
    public void Warn(string message, string source = "app") => Add(LogLevel.Warn, message, source);
    public void Error(string message, string source = "app") => Add(LogLevel.Error, message, source);

    public void Clear() => Entries.Clear();
}
