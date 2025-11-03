using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using System;

namespace AeroDriver.UI.Controls;

/// <summary>
/// Atlassian-inspired progress indicator with different variants
/// </summary>
[SupportedOSPlatform("windows")]
public enum ProgressVariant
{
    Linear,
    Circular
}

[SupportedOSPlatform("windows")]
public class ProgressIndicator : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressIndicator, double>(nameof(Value), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ProgressIndicator, double>(nameof(Maximum), 100.0);

    public static readonly StyledProperty<ProgressVariant> VariantProperty =
        AvaloniaProperty.Register<ProgressIndicator, ProgressVariant>(nameof(Variant), ProgressVariant.Linear);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ProgressIndicator, string>(nameof(Label));

    public static readonly StyledProperty<bool> ShowPercentageProperty =
        AvaloniaProperty.Register<ProgressIndicator, bool>(nameof(ShowPercentage), true);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, Maximum));
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, Math.Max(0, value));
    }

    public ProgressVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool ShowPercentage
    {
        get => GetValue(ShowPercentageProperty);
        set => SetValue(ShowPercentageProperty, value);
    }

    public double Percentage => Maximum > 0 ? (Value / Maximum) * 100 : 0;

    static ProgressIndicator()
    {
        AffectsRender<ProgressIndicator>(ValueProperty, MaximumProperty, VariantProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Variant == ProgressVariant.Linear)
        {
            RenderLinearProgress(context);
        }
        else
        {
            RenderCircularProgress(context);
        }
    }

    private void RenderLinearProgress(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var progressWidth = bounds.Width * Percentage / 100;

        // Background track
        var trackRect = new Rect(bounds.X, bounds.Y + bounds.Height / 2 - 2, bounds.Width, 4);
        context.DrawRectangle(
            new SolidColorBrush(DesignTokens.Neutral.N30),
            null,
            trackRect,
            DesignTokens.BorderRadius.Radius050,
            DesignTokens.BorderRadius.Radius050
        );

        // Progress bar
        if (progressWidth > 0)
        {
            var progressRect = new Rect(bounds.X, bounds.Y + bounds.Height / 2 - 2, progressWidth, 4);
            context.DrawRectangle(
                new SolidColorBrush(DesignTokens.SemanticColors.Primary),
                null,
                progressRect,
                DesignTokens.BorderRadius.Radius050,
                DesignTokens.BorderRadius.Radius050
            );
        }
    }

    private void RenderCircularProgress(DrawingContext context)
    {
        var centerX = Bounds.Width / 2;
        var centerY = Bounds.Height / 2;
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 2;

        if (radius <= 0) return;

        var backgroundPen = new Pen(new SolidColorBrush(DesignTokens.Neutral.N30), 3);
        var progressPen = new Pen(new SolidColorBrush(DesignTokens.SemanticColors.Primary), 3);

        // Background circle
        context.DrawEllipse(null, backgroundPen, new Point(centerX, centerY), radius, radius);

        // Progress arc
        if (Percentage > 0)
        {
            var angle = Percentage / 100 * 360;
            var startAngle = -90; // Start from top
            var endAngle = startAngle + angle;

            // Draw arc (simplified - in real implementation would use proper arc drawing)
            var startPoint = new Point(
                centerX + radius * Math.Cos(startAngle * Math.PI / 180),
                centerY + radius * Math.Sin(startAngle * Math.PI / 180)
            );
            var endPoint = new Point(
                centerX + radius * Math.Cos(endAngle * Math.PI / 180),
                centerY + radius * Math.Sin(endAngle * Math.PI / 180)
            );

            // For simplicity, draw a line. Real implementation would use ArcSegment
            if (Percentage < 100)
            {
                context.DrawLine(progressPen, startPoint, endPoint);
            }
        }
    }
}

/// <summary>
/// Progress indicator with integrated label and percentage display
/// </summary>
[SupportedOSPlatform("windows")]
public class LabeledProgressIndicator : UserControl
{
    private readonly ProgressIndicator _progressIndicator;
    private readonly TextBlock _labelText;
    private readonly TextBlock _percentageText;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LabeledProgressIndicator, double>(nameof(Value), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LabeledProgressIndicator, double>(nameof(Maximum), 100.0);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledProgressIndicator, string>(nameof(Label), "Progress");

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public LabeledProgressIndicator()
    {
        var layout = new StackPanel
        {
            Spacing = DesignTokens.Spacing.Space050,
            Orientation = Orientation.Vertical
        };

        // Label
        _labelText = new TextBlock
        {
            FontFamily = DesignTokens.Typography.FontFamily,
            FontSize = DesignTokens.Typography.FontSize.Size14,
            FontWeight = DesignTokens.Typography.FontWeight.Medium,
            Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text)
        };

        // Progress bar with percentage
        var progressContainer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        _progressIndicator = new ProgressIndicator
        {
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        _percentageText = new TextBlock
        {
            FontFamily = DesignTokens.Typography.FontFamily,
            FontSize = DesignTokens.Typography.FontSize.Size12,
            FontWeight = DesignTokens.Typography.FontWeight.Normal,
            Foreground = new SolidColorBrush(DesignTokens.SemanticColors.TextSubtle),
            Margin = new Thickness(DesignTokens.Spacing.Space100, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(_progressIndicator, 0);
        Grid.SetColumn(_percentageText, 1);

        progressContainer.Children.Add(_progressIndicator);
        progressContainer.Children.Add(_percentageText);

        layout.Children.Add(_labelText);
        layout.Children.Add(progressContainer);

        Content = layout;

        // Update bindings
        PropertyChanged += (sender, args) =>
        {
            if (args.Property == ValueProperty)
            {
                _progressIndicator.Value = Value;
                UpdatePercentageText();
            }
            else if (args.Property == MaximumProperty)
            {
                _progressIndicator.Maximum = Maximum;
                UpdatePercentageText();
            }
            else if (args.Property == LabelProperty)
            {
                _labelText.Text = Label;
            }
        };

        // Initial values
        _labelText.Text = Label;
        _progressIndicator.Value = Value;
        _progressIndicator.Maximum = Maximum;
        UpdatePercentageText();
    }

    private void UpdatePercentageText()
    {
        var percentage = Maximum > 0 ? (Value / Maximum) * 100 : 0;
        _percentageText.Text = $"{percentage:F0}%";
    }
}
