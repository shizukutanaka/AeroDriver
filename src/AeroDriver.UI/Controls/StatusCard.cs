using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AeroDriver.Core.UI;

namespace AeroDriver.UI.Controls;

/// <summary>
/// Atlassian-inspired status card component
/// </summary>
[SupportedOSPlatform("windows")]
public class StatusCard : Border
{
    private TextBlock _titleBlock;
    private TextBlock _valueBlock;
    private TextBlock _iconBlock;

    public StatusCard(string title, string value, StatusLevel status = StatusLevel.Neutral)
    {
        Initialize(title, value, status);
    }

    private void Initialize(string title, string value, StatusLevel status)
    {
        // Card styling (Atlassian design tokens)
        BorderBrush = GetStatusBrush(status);
        BorderThickness = new Thickness(1, 1, 1, 4); // Accent bottom border
        CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Medium);
        Background = Brushes.White;
        Padding = new Thickness(DesignTokens.Spacing.Large);
        Margin = new Thickness(DesignTokens.Spacing.Small);
        MinWidth = 200;
        MinHeight = 120;

        // Layout
        var panel = new StackPanel { Spacing = DesignTokens.Spacing.Small };

        // Icon
        _iconBlock = new TextBlock
        {
            Text = StatusIndicator.GetIcon(status),
            FontSize = 32,
            Foreground = GetStatusBrush(status),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Title
        _titleBlock = new TextBlock
        {
            Text = title,
            FontSize = DesignTokens.Typography.SmallSize,
            Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextSecondary)),
            FontWeight = FontWeight.Medium
        };

        // Value
        _valueBlock = new TextBlock
        {
            Text = value,
            FontSize = DesignTokens.Typography.H3Size,
            Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextPrimary)),
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, DesignTokens.Spacing.XSmall, 0, 0)
        };

        panel.Children.Add(_iconBlock);
        panel.Children.Add(_titleBlock);
        panel.Children.Add(_valueBlock);

        Child = panel;
    }

    public void UpdateValue(string value, StatusLevel status)
    {
        _valueBlock.Text = value;
        _iconBlock.Text = StatusIndicator.GetIcon(status);
        _iconBlock.Foreground = GetStatusBrush(status);
        BorderBrush = GetStatusBrush(status);
    }

    private static IBrush GetStatusBrush(StatusLevel status) => status switch
    {
        StatusLevel.Success => new SolidColorBrush(Color.Parse(DesignTokens.Colors.Success)),
        StatusLevel.Warning => new SolidColorBrush(Color.Parse(DesignTokens.Colors.Warning)),
        StatusLevel.Error => new SolidColorBrush(Color.Parse(DesignTokens.Colors.Error)),
        StatusLevel.Info => new SolidColorBrush(Color.Parse(DesignTokens.Colors.Info)),
        _ => new SolidColorBrush(Color.Parse(DesignTokens.Colors.Border))
    };
}
