using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AeroDriver.Core.UI;

namespace AeroDriver.UI.Controls;

/// <summary>
/// Atlassian-inspired notification banner
/// </summary>
[SupportedOSPlatform("windows")]
public class NotificationBanner : Border
{
    private TextBlock _messageBlock;
    private Button _closeButton;

    public event EventHandler? Dismissed;

    public NotificationBanner(string message, StatusLevel level = StatusLevel.Info, bool dismissible = true)
    {
        Initialize(message, level, dismissible);
    }

    private void Initialize(string message, StatusLevel level, bool dismissible)
    {
        // Banner styling
        var (bgColor, iconColor) = GetColors(level);
        Background = new SolidColorBrush(Color.Parse(bgColor));
        BorderBrush = new SolidColorBrush(Color.Parse(iconColor));
        BorderThickness = new Thickness(0, 0, 0, 2);
        Padding = new Thickness(DesignTokens.Spacing.Large);
        Margin = new Thickness(0, 0, 0, DesignTokens.Spacing.Medium);

        // Layout
        var panel = new DockPanel { LastChildFill = true };

        // Icon
        var icon = new TextBlock
        {
            Text = StatusIndicator.GetIcon(level),
            FontSize = DesignTokens.IconSize.Medium,
            Foreground = new SolidColorBrush(Color.Parse(iconColor)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, DesignTokens.Spacing.Medium, 0)
        };
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);

        // Close button
        if (dismissible)
        {
            _closeButton = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.Parse(iconColor)),
                FontSize = DesignTokens.Typography.H4Size,
                Padding = new Thickness(DesignTokens.Spacing.Small),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center
            };

            _closeButton.Click += (s, e) =>
            {
                IsVisible = false;
                Dismissed?.Invoke(this, EventArgs.Empty);
            };

            DockPanel.SetDock(_closeButton, Dock.Right);
            panel.Children.Add(_closeButton);
        }

        // Message
        _messageBlock = new TextBlock
        {
            Text = message,
            FontSize = DesignTokens.Typography.BodySize,
            Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextPrimary)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(_messageBlock);

        Child = panel;
    }

    public void UpdateMessage(string message, StatusLevel level = StatusLevel.Info)
    {
        _messageBlock.Text = message;

        var (bgColor, iconColor) = GetColors(level);
        Background = new SolidColorBrush(Color.Parse(bgColor));
        BorderBrush = new SolidColorBrush(Color.Parse(iconColor));
    }

    private static (string bgColor, string iconColor) GetColors(StatusLevel level) => level switch
    {
        StatusLevel.Success => (DesignTokens.Colors.SuccessLight, DesignTokens.Colors.Success),
        StatusLevel.Warning => (DesignTokens.Colors.WarningLight, DesignTokens.Colors.Warning),
        StatusLevel.Error => (DesignTokens.Colors.ErrorLight, DesignTokens.Colors.Error),
        StatusLevel.Info => (DesignTokens.Colors.InfoLight, DesignTokens.Colors.Info),
        _ => (DesignTokens.Colors.BackgroundSecondary, DesignTokens.Colors.TextSecondary)
    };
}
