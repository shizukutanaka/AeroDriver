using System.Runtime.Versioning;
using System.Text;
using AeroDriver.Core.UI;

namespace AeroDriver.CLI;

/// <summary>
/// Enhanced CLI output with Atlassian Design System principles
/// </summary>
[SupportedOSPlatform("windows")]
public static class EnhancedCliOutput
{
    private static readonly int ConsoleWidth = Math.Max(Console.WindowWidth, 80);

    #region Page Layout

    /// <summary>
    /// Display a page header with branding
    /// </summary>
    public static void WritePageHeader(string title, string subtitle = "")
    {
        Console.Clear();
        Console.WriteLine();
        WriteBox(title, subtitle);
        Console.WriteLine();
    }

    /// <summary>
    /// Display section header with separator
    /// </summary>
    public static void WriteSectionHeader(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"▸ {title}");
        Console.ResetColor();
        Console.WriteLine(new string('─', Math.Min(title.Length + 2, 60)));
        Console.WriteLine();
    }

    #endregion

    #region Cards and Panels

    /// <summary>
    /// Display information card (Atlassian-style)
    /// </summary>
    public static void WriteCard(string title, Dictionary<string, string> data, StatusLevel status = StatusLevel.Neutral)
    {
        var icon = StatusIndicator.GetIcon(status);
        var color = StatusIndicator.GetConsoleColor(status);

        Console.ForegroundColor = color;
        Console.WriteLine($"╔═ {icon} {title}");
        Console.ResetColor();

        foreach (var item in data)
        {
            Console.WriteLine($"║  {item.Key.PadRight(20)}: {item.Value}");
        }

        Console.ForegroundColor = color;
        Console.WriteLine("╚" + new string('═', 40));
        Console.ResetColor();
        Console.WriteLine();
    }

    /// <summary>
    /// Display status panel with multiple items
    /// </summary>
    public static void WriteStatusPanel(string title, List<(string label, string value, StatusLevel status)> items)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"╔═══ {title} " + new string('═', Math.Max(40 - title.Length, 10)));
        Console.ResetColor();

        foreach (var (label, value, status) in items)
        {
            var icon = StatusIndicator.GetIcon(status);
            var color = StatusIndicator.GetConsoleColor(status);

            Console.Write("║ ");
            Console.ForegroundColor = color;
            Console.Write($"{icon} ");
            Console.ResetColor();
            Console.WriteLine($"{label.PadRight(25)}: {value}");
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("╚" + new string('═', 50));
        Console.ResetColor();
        Console.WriteLine();
    }

    #endregion

    #region Tables

    /// <summary>
    /// Display data table with automatic column sizing
    /// </summary>
    public static void WriteTable(string[] headers, List<string[]> rows, int[]? columnWidths = null)
    {
        if (headers.Length == 0 || rows.Count == 0) return;

        // Calculate column widths
        var widths = columnWidths ?? CalculateColumnWidths(headers, rows);

        // Header
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("┌");
        for (int i = 0; i < headers.Length; i++)
        {
            Console.Write(new string('─', widths[i] + 2));
            if (i < headers.Length - 1) Console.Write("┬");
        }
        Console.WriteLine("┐");

        // Header row
        Console.Write("│ ");
        for (int i = 0; i < headers.Length; i++)
        {
            Console.Write(headers[i].PadRight(widths[i]));
            Console.Write(i < headers.Length - 1 ? " │ " : " │");
        }
        Console.WriteLine();

        // Separator
        Console.Write("├");
        for (int i = 0; i < headers.Length; i++)
        {
            Console.Write(new string('─', widths[i] + 2));
            if (i < headers.Length - 1) Console.Write("┼");
        }
        Console.WriteLine("┤");
        Console.ResetColor();

        // Data rows
        foreach (var row in rows)
        {
            Console.Write("│ ");
            for (int i = 0; i < headers.Length && i < row.Length; i++)
            {
                var value = row[i].Length > widths[i]
                    ? row[i].Substring(0, widths[i] - 3) + "..."
                    : row[i];
                Console.Write(value.PadRight(widths[i]));
                Console.Write(i < headers.Length - 1 ? " │ " : " │");
            }
            Console.WriteLine();
        }

        // Footer
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("└");
        for (int i = 0; i < headers.Length; i++)
        {
            Console.Write(new string('─', widths[i] + 2));
            if (i < headers.Length - 1) Console.Write("┴");
        }
        Console.WriteLine("┘");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static int[] CalculateColumnWidths(string[] headers, List<string[]> rows, int maxWidth = 40)
    {
        var widths = new int[headers.Length];

        for (int i = 0; i < headers.Length; i++)
        {
            widths[i] = headers[i].Length;
            foreach (var row in rows)
            {
                if (i < row.Length && row[i].Length > widths[i])
                {
                    widths[i] = Math.Min(row[i].Length, maxWidth);
                }
            }
        }

        return widths;
    }

    #endregion

    #region Progress and Loading

    /// <summary>
    /// Display progress bar with percentage
    /// </summary>
    public static void WriteProgressBar(string label, int current, int total, int barWidth = 40)
    {
        var percentage = Math.Min(100, (int)((double)current / total * 100));
        var filled = (int)((double)current / total * barWidth);
        var empty = barWidth - filled;

        Console.Write($"\r{label.PadRight(20)} [");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(new string('█', filled));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('░', empty));
        Console.ResetColor();

        Console.Write($"] {percentage}% ({current}/{total})");

        if (current >= total)
        {
            Console.WriteLine(" ✓");
        }
    }

    /// <summary>
    /// Display spinner for indeterminate operations
    /// </summary>
    public static async Task<T> ShowSpinnerAsync<T>(string message, Task<T> task)
    {
        var spinner = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var index = 0;
        var startTime = DateTime.Now;

        Console.CursorVisible = false;
        try
        {
            while (!task.IsCompleted)
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                Console.Write($"\r{spinner[index]} {message} ({elapsed:F1}s)");
                index = (index + 1) % spinner.Length;
                await Task.Delay(100);
            }

            var totalTime = (DateTime.Now - startTime).TotalSeconds;
            Console.Write($"\r✓ {message} ({totalTime:F1}s)".PadRight(60));
            Console.WriteLine();

            return await task;
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    #endregion

    #region Notifications and Alerts

    /// <summary>
    /// Display inline notification
    /// </summary>
    public static void WriteNotification(string message, StatusLevel level = StatusLevel.Info)
    {
        var icon = StatusIndicator.GetIcon(level);
        var color = StatusIndicator.GetConsoleColor(level);

        Console.ForegroundColor = color;
        Console.Write($"{icon} ");
        Console.ResetColor();
        Console.WriteLine(message);
        Console.WriteLine();
    }

    /// <summary>
    /// Display prominent alert box
    /// </summary>
    public static void WriteAlert(string title, string message, StatusLevel level = StatusLevel.Warning)
    {
        var icon = StatusIndicator.GetIcon(level);
        var color = StatusIndicator.GetConsoleColor(level);
        var width = Math.Min(ConsoleWidth - 4, 70);

        Console.ForegroundColor = color;
        Console.WriteLine();
        Console.WriteLine("┏" + new string('━', width) + "┓");
        Console.Write("┃ " + icon + " ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(title.PadRight(width - 3));
        Console.ForegroundColor = color;
        Console.WriteLine("┃");
        Console.WriteLine("┣" + new string('━', width) + "┫");
        Console.ResetColor();

        // Word wrap message
        var lines = WrapText(message, width - 2);
        foreach (var line in lines)
        {
            Console.ForegroundColor = color;
            Console.Write("┃ ");
            Console.ResetColor();
            Console.Write(line.PadRight(width - 2));
            Console.ForegroundColor = color;
            Console.WriteLine(" ┃");
        }

        Console.WriteLine("┗" + new string('━', width) + "┛");
        Console.ResetColor();
        Console.WriteLine();
    }

    #endregion

    #region Lists and Items

    /// <summary>
    /// Display bullet list with icons
    /// </summary>
    public static void WriteList(string title, List<(string text, StatusLevel status)> items, int maxItems = 10)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"▸ {title}");
        Console.ResetColor();

        var displayItems = items.Take(maxItems).ToList();
        foreach (var (text, status) in displayItems)
        {
            var icon = StatusIndicator.GetIcon(status);
            var color = StatusIndicator.GetConsoleColor(status);

            Console.Write("  ");
            Console.ForegroundColor = color;
            Console.Write($"{icon} ");
            Console.ResetColor();
            Console.WriteLine(text);
        }

        if (items.Count > maxItems)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  ... and {items.Count - maxItems} more");
            Console.ResetColor();
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Display key-value pairs
    /// </summary>
    public static void WriteKeyValuePairs(Dictionary<string, string> data, string? sectionTitle = null)
    {
        if (sectionTitle != null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"▸ {sectionTitle}");
            Console.ResetColor();
        }

        foreach (var (key, value) in data)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"  {key.PadRight(25)}: ");
            Console.ResetColor();
            Console.WriteLine(value);
        }

        Console.WriteLine();
    }

    #endregion

    #region Utilities

    private static void WriteBox(string title, string subtitle = "")
    {
        var width = Math.Min(ConsoleWidth - 4, 60);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔" + new string('═', width) + "╗");
        Console.Write("║ ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(title.PadRight(width - 2));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(" ║");

        if (!string.IsNullOrEmpty(subtitle))
        {
            Console.Write("║ ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(subtitle.PadRight(width - 2));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" ║");
        }

        Console.WriteLine("╚" + new string('═', width) + "╝");
        Console.ResetColor();
    }

    private static List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(' ');
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            if (currentLine.Length + word.Length + 1 > maxWidth)
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
            }

            if (currentLine.Length > 0)
                currentLine.Append(' ');
            currentLine.Append(word);
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        return lines;
    }

    #endregion

    #region Dashboard Components

    /// <summary>
    /// Display system dashboard overview
    /// </summary>
    public static void WriteDashboard(
        string systemName,
        Dictionary<string, string> stats,
        List<(string label, string value, StatusLevel status)> healthItems,
        List<(string text, StatusLevel status)> recentActivity)
    {
        WritePageHeader(systemName, "System Overview");

        // Stats cards
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("System Statistics");
        Console.ResetColor();
        WriteKeyValuePairs(stats);

        // Health status
        WriteStatusPanel("System Health", healthItems);

        // Recent activity
        if (recentActivity.Any())
        {
            WriteList("Recent Activity", recentActivity, 5);
        }
    }

    #endregion
}
