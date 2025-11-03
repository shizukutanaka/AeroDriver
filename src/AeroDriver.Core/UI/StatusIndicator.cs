using System.Runtime.Versioning;

namespace AeroDriver.Core.UI;

/// <summary>
/// Status indicator for driver and system states
/// </summary>
[SupportedOSPlatform("windows")]
public enum StatusLevel
{
    Success,
    Warning,
    Error,
    Info,
    Neutral
}

[SupportedOSPlatform("windows")]
public static class StatusIndicator
{
    public static string GetIcon(StatusLevel level) => level switch
    {
        StatusLevel.Success => "✓",
        StatusLevel.Warning => "⚠",
        StatusLevel.Error => "✕",
        StatusLevel.Info => "ℹ",
        StatusLevel.Neutral => "•",
        _ => "•"
    };

    public static string GetColor(StatusLevel level) => level switch
    {
        StatusLevel.Success => DesignTokens.Colors.Success,
        StatusLevel.Warning => DesignTokens.Colors.Warning,
        StatusLevel.Error => DesignTokens.Colors.Error,
        StatusLevel.Info => DesignTokens.Colors.Info,
        _ => DesignTokens.Colors.TextSecondary
    };

    public static ConsoleColor GetConsoleColor(StatusLevel level) => level switch
    {
        StatusLevel.Success => ConsoleColor.Green,
        StatusLevel.Warning => ConsoleColor.Yellow,
        StatusLevel.Error => ConsoleColor.Red,
        StatusLevel.Info => ConsoleColor.Cyan,
        _ => ConsoleColor.Gray
    };

    public static string FormatStatus(string message, StatusLevel level, bool includeIcon = true)
    {
        var icon = includeIcon ? GetIcon(level) + " " : "";
        return $"{icon}{message}";
    }

    public static void WriteColoredStatus(string message, StatusLevel level, bool includeIcon = true)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = GetConsoleColor(level);
            if (includeIcon)
                Console.Write(GetIcon(level) + " ");
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }
}
