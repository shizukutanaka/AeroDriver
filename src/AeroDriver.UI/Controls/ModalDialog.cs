using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace AeroDriver.UI.Controls;

/// <summary>
/// Atlassian-inspired modal dialog for confirmations and actions
/// </summary>
[SupportedOSPlatform("windows")]
public class ModalDialog : Window
{
    private readonly TaskCompletionSource<bool?> _result = new();

    public Task<bool?> ShowDialogAsync() => _result.Task;

    public ModalDialog(string title, string message, string? primaryButtonText = null,
                      string? secondaryButtonText = null, string? cancelButtonText = null)
    {
        Title = title;
        Width = 400;
        Height = 200;
        MinWidth = 300;
        MinHeight = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        // Overlay/Backdrop
        Background = new SolidColorBrush(Color.Parse("#00000080")); // Semi-transparent black

        // Main content container
        var mainContainer = new Border
        {
            Background = new SolidColorBrush(DesignTokens.SemanticColors.Background),
            BorderBrush = new SolidColorBrush(DesignTokens.SemanticColors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Radius200),
            Padding = new Thickness(DesignTokens.Spacing.Space200),
            BoxShadow = DesignTokens.Elevation.Shadow400,
            Margin = new Thickness(DesignTokens.Spacing.Space200)
        };

        var layout = new StackPanel
        {
            Spacing = DesignTokens.Spacing.Space200
        };

        // Title
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = DesignTokens.Typography.FontSize.Size18,
            FontWeight = DesignTokens.Typography.FontWeight.SemiBold,
            Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Message
        var messageText = new TextBlock
        {
            Text = message,
            FontSize = DesignTokens.Typography.FontSize.Size14,
            Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = DesignTokens.Spacing.Space100,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        // Cancel button (if provided)
        if (!string.IsNullOrEmpty(cancelButtonText))
        {
            var cancelButton = new ActionButton(cancelButtonText, ButtonVariant.Tertiary)
            {
                MinWidth = 80
            };
            cancelButton.Click += (s, e) =>
            {
                _result.TrySetResult(null);
                Close();
            };
            buttonPanel.Children.Add(cancelButton);
        }

        // Secondary button (if provided)
        if (!string.IsNullOrEmpty(secondaryButtonText))
        {
            var secondaryButton = new ActionButton(secondaryButtonText, ButtonVariant.Secondary)
            {
                MinWidth = 80
            };
            secondaryButton.Click += (s, e) =>
            {
                _result.TrySetResult(false);
                Close();
            };
            buttonPanel.Children.Add(secondaryButton);
        }

        // Primary button (default if no text provided)
        var primaryText = primaryButtonText ?? "OK";
        var primaryButton = new ActionButton(primaryText, ButtonVariant.Primary)
        {
            MinWidth = 80
        };
        primaryButton.Click += (s, e) =>
        {
            _result.TrySetResult(true);
            Close();
        };
        buttonPanel.Children.Add(primaryButton);

        // Focus primary button by default
        primaryButton.Focus();

        layout.Children.Add(titleText);
        layout.Children.Add(messageText);
        layout.Children.Add(buttonPanel);

        mainContainer.Child = layout;
        Content = mainContainer;

        // Handle escape key
        KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                _result.TrySetResult(null);
                Close();
            }
        };

        // Handle window close
        Closed += (s, e) =>
        {
            if (!_result.Task.IsCompleted)
            {
                _result.TrySetResult(null);
            }
        };
    }
}

/// <summary>
/// Specialized confirmation dialog
/// </summary>
[SupportedOSPlatform("windows")]
public class ConfirmationDialog : ModalDialog
{
    public ConfirmationDialog(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel")
        : base(title, message, confirmText, null, cancelText)
    {
    }
}

/// <summary>
/// Specialized error dialog
/// </summary>
[SupportedOSPlatform("windows")]
public class ErrorDialog : ModalDialog
{
    public ErrorDialog(string title, string message, string okText = "OK")
        : base(title, message, okText)
    {
        // Style as error dialog
        if (Content is Border mainContainer && mainContainer.Child is StackPanel layout)
        {
            // Add error icon (simplified - would use actual icon in real implementation)
            var errorIndicator = new Border
            {
                Background = new SolidColorBrush(DesignTokens.SemanticColors.Danger),
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, DesignTokens.Spacing.Space100, 0)
            };

            var errorText = new TextBlock
            {
                Text = "!",
                FontSize = DesignTokens.Typography.FontSize.Size20,
                FontWeight = DesignTokens.Typography.FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            errorIndicator.Child = errorText;
            layout.Insert(0, errorIndicator);
        }
    }
}

/// <summary>
/// Progress dialog for long-running operations
/// </summary>
[SupportedOSPlatform("windows")]
public class ProgressDialog : Window
{
    private readonly LabeledProgressIndicator _progressIndicator;

    public ProgressDialog(string title, string message)
    {
        Title = title;
        Width = 400;
        Height = 150;
        MinWidth = 350;
        MinHeight = 120;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        // Main content
        var mainContainer = new Border
        {
            Background = new SolidColorBrush(DesignTokens.SemanticColors.Background),
            BorderBrush = new SolidColorBrush(DesignTokens.SemanticColors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Radius200),
            Padding = new Thickness(DesignTokens.Spacing.Space200),
            BoxShadow = DesignTokens.Elevation.Shadow400,
            Margin = new Thickness(DesignTokens.Spacing.Space200)
        };

        var layout = new StackPanel
        {
            Spacing = DesignTokens.Spacing.Space150
        };

        // Message
        var messageText = new TextBlock
        {
            Text = message,
            FontSize = DesignTokens.Typography.FontSize.Size14,
            Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Progress indicator
        _progressIndicator = new LabeledProgressIndicator
        {
            Label = "Progress"
        };

        layout.Children.Add(messageText);
        layout.Children.Add(_progressIndicator);

        mainContainer.Child = layout;
        Content = mainContainer;
    }

    public void UpdateProgress(double value, double maximum = 100.0)
    {
        _progressIndicator.Value = value;
        _progressIndicator.Maximum = maximum;
    }

    public void UpdateLabel(string label)
    {
        _progressIndicator.Label = label;
    }
}
