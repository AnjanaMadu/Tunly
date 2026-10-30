using Microsoft.UI.Xaml.Media;
using System;
using System.Text.Json.Serialization;

namespace Tunly.Models;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Message { get; init; } = "";
    public string Source { get; init; } = "app";

    [JsonIgnore] public string TimeText => Timestamp.ToString("HH:mm:ss");
    [JsonIgnore] public string LevelText => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO"
    };

    [JsonIgnore] public Brush LevelBrush => Level switch
    {
        LogLevel.Error => StatusBrushes.Danger,
        LogLevel.Warn => StatusBrushes.Warning,
        LogLevel.Debug => StatusBrushes.Accent,
        LogLevel.Trace => StatusBrushes.Muted,
        _ => StatusBrushes.Success
    };
}
