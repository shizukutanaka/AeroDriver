using System.Runtime.Versioning;

namespace AeroDriver.Core.UI;

/// <summary>
/// Accessibility utilities following WCAG 2.1 guidelines
/// </summary>
[SupportedOSPlatform("windows")]
public static class AccessibilityHelper
{
    /// <summary>
    /// Check if color contrast meets WCAG AA standards (4.5:1 for normal text)
    /// </summary>
    public static bool MeetsContrastRatio(string foreground, string background, bool largeText = false)
    {
        var ratio = CalculateContrastRatio(foreground, background);
        var minimumRatio = largeText ? 3.0 : 4.5; // WCAG AA standards

        return ratio >= minimumRatio;
    }

    /// <summary>
    /// Calculate relative luminance of a color
    /// </summary>
    private static double CalculateRelativeLuminance(string hexColor)
    {
        // Remove # if present
        hexColor = hexColor.TrimStart('#');

        var r = Convert.ToInt32(hexColor.Substring(0, 2), 16) / 255.0;
        var g = Convert.ToInt32(hexColor.Substring(2, 2), 16) / 255.0;
        var b = Convert.ToInt32(hexColor.Substring(4, 2), 16) / 255.0;

        // Apply gamma correction
        r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>
    /// Calculate contrast ratio between two colors
    /// </summary>
    private static double CalculateContrastRatio(string color1, string color2)
    {
        var l1 = CalculateRelativeLuminance(color1);
        var l2 = CalculateRelativeLuminance(color2);

        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Generate ARIA label for status indicators
    /// </summary>
    public static string GetAriaLabel(string context, StatusLevel level)
    {
        var statusText = level switch
        {
            StatusLevel.Success => "success",
            StatusLevel.Warning => "warning",
            StatusLevel.Error => "error",
            StatusLevel.Info => "information",
            _ => "neutral"
        };

        return $"{context}: {statusText}";
    }

    /// <summary>
    /// Generate accessible description for interactive elements
    /// </summary>
    public static string GetAccessibleDescription(string elementName, string action, string? consequence = null)
    {
        var description = $"{elementName} button. {action}.";
        if (!string.IsNullOrEmpty(consequence))
        {
            description += $" {consequence}.";
        }
        return description;
    }

    /// <summary>
    /// Validate focus order for keyboard navigation
    /// </summary>
    public static bool ValidateFocusOrder(int tabIndex)
    {
        // Tab index should be >= 0 for focusable elements
        // 0 = natural order, >0 = explicit order
        return tabIndex >= 0;
    }

    /// <summary>
    /// Get keyboard shortcut hint
    /// </summary>
    public static string GetKeyboardHint(string key, bool ctrl = false, bool alt = false, bool shift = false)
    {
        var modifiers = new List<string>();
        if (ctrl) modifiers.Add("Ctrl");
        if (alt) modifiers.Add("Alt");
        if (shift) modifiers.Add("Shift");

        var shortcut = modifiers.Any() ? $"{string.Join("+", modifiers)}+{key}" : key;
        return $"Keyboard shortcut: {shortcut}";
    }

    /// <summary>
    /// Screen reader friendly time format
    /// </summary>
    public static string FormatTimeForScreenReader(DateTime time)
    {
        return time.ToString("MMMM dd, yyyy 'at' h:mm tt");
    }

    /// <summary>
    /// Screen reader friendly number format
    /// </summary>
    public static string FormatNumberForScreenReader(int number, string? unit = null)
    {
        var text = number.ToString("N0");
        if (!string.IsNullOrEmpty(unit))
        {
            text += $" {unit}";
        }
        return text;
    }
}
