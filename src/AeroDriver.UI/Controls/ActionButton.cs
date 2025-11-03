using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace AeroDriver.UI.Controls;

/// <summary>
/// Atlassian-inspired button with proper visual states and accessibility
/// </summary>
[SupportedOSPlatform("windows")]
public enum ButtonVariant
{
    Primary,
    Secondary,
    Tertiary,
    Danger
}

[SupportedOSPlatform("windows")]
public class ActionButton : Button
{
    public static readonly StyledProperty<ButtonVariant> VariantProperty =
        AvaloniaProperty.Register<ActionButton, ButtonVariant>(nameof(Variant));

    public ButtonVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public ActionButton()
    {
        InitializeButton();
    }

    public ActionButton(string text, ButtonVariant variant = ButtonVariant.Primary)
    {
        Content = text;
        Variant = variant;
        InitializeButton();
    }

    private void InitializeButton()
    {
        // Apply design tokens
        MinWidth = DesignTokens.Button.MinWidth;
        Height = DesignTokens.Button.Height;
        CornerRadius = new CornerRadius(DesignTokens.Button.BorderRadius);
        Padding = new Thickness(
            DesignTokens.Button.PaddingHorizontal,
            DesignTokens.Button.PaddingVertical,
            DesignTokens.Button.PaddingHorizontal,
            DesignTokens.Button.PaddingVertical
        );

        FontFamily = DesignTokens.Typography.FontFamily;
        FontSize = DesignTokens.Typography.FontSize.Size14;
        FontWeight = DesignTokens.Typography.FontWeight.Medium;

        // Visual states and transitions
        var style = new Style();

        // Default state
        style.Setters.Add(new Setter(BackgroundProperty, GetBackgroundBrush(Variant)));
        style.Setters.Add(new Setter(ForegroundProperty, GetForegroundBrush(Variant)));
        style.Setters.Add(new Setter(BorderBrushProperty, GetBorderBrush(Variant)));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(Variant == ButtonVariant.Tertiary ? 1 : 0)));

        // Hover state
        var hoverTrigger = new Selector().OfType<ActionButton>().Class(":pointerover");
        style.Triggers.Add(new Trigger
        {
            Property = IsPointerOverProperty,
            Value = true,
            Setters =
            {
                new Setter(BackgroundProperty, GetBackgroundBrush(Variant, true)),
                new Setter(ForegroundProperty, GetForegroundBrush(Variant, true)),
                new Setter(BorderBrushProperty, GetBorderBrush(Variant, true))
            }
        });

        // Pressed state
        style.Triggers.Add(new Trigger
        {
            Property = IsPressedProperty,
            Value = true,
            Setters =
            {
                new Setter(BackgroundProperty, GetBackgroundBrush(Variant, false, true)),
                new Setter(ForegroundProperty, GetForegroundBrush(Variant, false, true)),
                new Setter(BorderBrushProperty, GetBorderBrush(Variant, false, true))
            }
        });

        // Disabled state
        style.Triggers.Add(new Trigger
        {
            Property = IsEnabledProperty,
            Value = false,
            Setters =
            {
                new Setter(BackgroundProperty, new SolidColorBrush(DesignTokens.Neutral.N30)),
                new Setter(ForegroundProperty, new SolidColorBrush(DesignTokens.Neutral.N400)),
                new Setter(BorderBrushProperty, new SolidColorBrush(DesignTokens.Neutral.N30)),
                new Setter(OpacityProperty, 0.6)
            }
        });

        Styles.Add(style);

        // Accessibility
        Focusable = true;
    }

    private static IBrush GetBackgroundBrush(ButtonVariant variant, bool isHovered = false, bool isPressed = false)
    {
        return variant switch
        {
            ButtonVariant.Primary => new SolidColorBrush(
                isPressed ? DesignTokens.SemanticColors.PrimaryPressed :
                isHovered ? DesignTokens.SemanticColors.PrimaryHovered :
                DesignTokens.SemanticColors.Primary),

            ButtonVariant.Secondary => new SolidColorBrush(
                isPressed ? DesignTokens.Neutral.N40 :
                isHovered ? DesignTokens.Neutral.N30 :
                DesignTokens.Neutral.N20),

            ButtonVariant.Tertiary => Brushes.Transparent,

            ButtonVariant.Danger => new SolidColorBrush(
                isPressed ? DesignTokens.Red.R500 :
                isHovered ? DesignTokens.Red.R300 :
                DesignTokens.Red.R400),

            _ => new SolidColorBrush(DesignTokens.Neutral.N50)
        };
    }

    private static IBrush GetForegroundBrush(ButtonVariant variant, bool isHovered = false, bool isPressed = false)
    {
        return variant switch
        {
            ButtonVariant.Primary => Brushes.White,
            ButtonVariant.Secondary => new SolidColorBrush(DesignTokens.SemanticColors.Text),
            ButtonVariant.Tertiary => new SolidColorBrush(
                isPressed ? DesignTokens.SemanticColors.PrimaryPressed :
                isHovered ? DesignTokens.SemanticColors.PrimaryHovered :
                DesignTokens.SemanticColors.Primary),
            ButtonVariant.Danger => Brushes.White,
            _ => new SolidColorBrush(DesignTokens.SemanticColors.Text)
        };
    }

    private static IBrush GetBorderBrush(ButtonVariant variant, bool isHovered = false, bool isPressed = false)
    {
        return variant switch
        {
            ButtonVariant.Tertiary => new SolidColorBrush(
                isPressed ? DesignTokens.SemanticColors.PrimaryPressed :
                isHovered ? DesignTokens.SemanticColors.PrimaryHovered :
                DesignTokens.SemanticColors.Border),
            _ => Brushes.Transparent
        };
    }
}
