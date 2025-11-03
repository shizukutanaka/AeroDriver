using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;

namespace AeroDriver.UI.Controls;

/// <summary>
/// Status indicator using Atlassian-inspired lozenge/badge design
/// </summary>
[SupportedOSPlatform("windows")]
public enum StatusType
{
    Success,
    Warning,
    Error,
    Info,
    Neutral,
    New,
    Removed
}

[SupportedOSPlatform("windows")]
public class StatusBadge : Border
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Text));

    public static readonly StyledProperty<StatusType> StatusTypeProperty =
        AvaloniaProperty.Register<StatusBadge, StatusType>(nameof(StatusType));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusType StatusType
    {
        get => GetValue(StatusTypeProperty);
        set => SetValue(StatusTypeProperty, value);
    }

    public StatusBadge()
    {
        InitializeBadge();
    }

    public StatusBadge(string text, StatusType statusType = StatusType.Neutral)
    {
        Text = text;
        StatusType = statusType;
        InitializeBadge();
    }

    private void InitializeBadge()
    {
        // Layout
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(DesignTokens.Spacing.Space050);
        Padding = new Thickness(DesignTokens.Spacing.Space075, DesignTokens.Spacing.Space025);
        CornerRadius = new CornerRadius(DesignTokens.BorderRadius.RadiusCircle); // Pill shape

        // Background and border
        Background = GetBackgroundBrush(StatusType);
        BorderBrush = GetBorderBrush(StatusType);
        BorderThickness = new Thickness(0);

        // Text content
        var textBlock = new TextBlock
        {
            Text = Text,
            FontFamily = DesignTokens.Typography.FontFamily,
            FontSize = DesignTokens.Typography.FontSize.Size12,
            FontWeight = DesignTokens.Typography.FontWeight.Medium,
            Foreground = GetForegroundBrush(StatusType),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0)
        };

        Child = textBlock;

        // Update appearance when properties change
        PropertyChanged += (sender, args) =>
        {
            if (args.Property == TextProperty && Child is TextBlock tb)
            {
                tb.Text = Text;
            }
            else if (args.Property == StatusTypeProperty)
            {
                Background = GetBackgroundBrush(StatusType);
                BorderBrush = GetBorderBrush(StatusType);
                if (Child is TextBlock tb2)
                {
                    tb2.Foreground = GetForegroundBrush(StatusType);
                }
            }
        };
    }

    private static IBrush GetBackgroundBrush(StatusType statusType)
    {
        return statusType switch
        {
            StatusType.Success => new SolidColorBrush(DesignTokens.Green.G100),
            StatusType.Warning => new SolidColorBrush(DesignTokens.Yellow.Y100),
            StatusType.Error => new SolidColorBrush(DesignTokens.Red.R100),
            StatusType.Info => new SolidColorBrush(DesignTokens.Purple.P100),
            StatusType.New => new SolidColorBrush(DesignTokens.Blue.B100),
            StatusType.Removed => new SolidColorBrush(DesignTokens.Neutral.N100),
            StatusType.Neutral => new SolidColorBrush(DesignTokens.Neutral.N30),
            _ => new SolidColorBrush(DesignTokens.Neutral.N30)
        };
    }

    private static IBrush GetForegroundBrush(StatusType statusType)
    {
        return statusType switch
        {
            StatusType.Success => new SolidColorBrush(DesignTokens.Green.G700),
            StatusType.Warning => new SolidColorBrush(DesignTokens.Yellow.Y700),
            StatusType.Error => new SolidColorBrush(DesignTokens.Red.R700),
            StatusType.Info => new SolidColorBrush(DesignTokens.Purple.P700),
            StatusType.New => new SolidColorBrush(DesignTokens.Blue.B700),
            StatusType.Removed => new SolidColorBrush(DesignTokens.Neutral.N700),
            StatusType.Neutral => new SolidColorBrush(DesignTokens.Neutral.N600),
            _ => new SolidColorBrush(DesignTokens.Neutral.N600)
        };
    }

    private static IBrush GetBorderBrush(StatusType statusType)
    {
        // Subtle borders for better definition
        return statusType switch
        {
            StatusType.Success => new SolidColorBrush(DesignTokens.Green.G200),
            StatusType.Warning => new SolidColorBrush(DesignTokens.Yellow.Y200),
            StatusType.Error => new SolidColorBrush(DesignTokens.Red.R200),
            StatusType.Info => new SolidColorBrush(DesignTokens.Purple.P200),
            StatusType.New => new SolidColorBrush(DesignTokens.Blue.B200),
            StatusType.Removed => new SolidColorBrush(DesignTokens.Neutral.N200),
            StatusType.Neutral => new SolidColorBrush(DesignTokens.Neutral.N40),
            _ => new SolidColorBrush(DesignTokens.Neutral.N40)
        };
    }
}

/// <summary>
/// Driver status specific badge with predefined statuses
/// </summary>
[SupportedOSPlatform("windows")]
public class DriverStatusBadge : StatusBadge
{
    public DriverStatusBadge(string status) : base(GetStatusText(status), GetStatusType(status)) { }

    private static string GetStatusText(string status)
    {
        return status.ToLower() switch
        {
            "up-to-date" or "updated" or "current" => "Up to date",
            "outdated" or "old" => "Outdated",
            "error" or "failed" => "Error",
            "installing" or "updating" => "Updating",
            "unknown" => "Unknown",
            _ => status
        };
    }

    private static StatusType GetStatusType(string status)
    {
        return status.ToLower() switch
        {
            "up-to-date" or "updated" or "current" => StatusType.Success,
            "outdated" or "old" => StatusType.Warning,
            "error" or "failed" => StatusType.Error,
            "installing" or "updating" => StatusType.Info,
            "unknown" => StatusType.Neutral,
            _ => StatusType.Neutral
        };
    }
}
