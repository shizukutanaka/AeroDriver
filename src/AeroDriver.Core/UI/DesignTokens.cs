using System.Runtime.Versioning;

namespace AeroDriver.Core.UI;

/// <summary>
/// Atlassian-inspired design tokens for consistent UI styling
/// </summary>
[SupportedOSPlatform("windows")]
public static class DesignTokens
{
    // Spacing System (4px base unit)
    public static class Spacing
    {
        public const int XSmall = 4;
        public const int Small = 8;
        public const int Medium = 12;
        public const int Large = 16;
        public const int XLarge = 24;
        public const int XXLarge = 32;
        public const int XXXLarge = 48;
    }

    // Color System (Semantic colors)
    public static class Colors
    {
        // Neutral
        public const string TextPrimary = "#172B4D";      // Dark text
        public const string TextSecondary = "#5E6C84";    // Muted text
        public const string TextDisabled = "#A5ADBA";     // Disabled text
        public const string BackgroundPrimary = "#FFFFFF"; // Main background
        public const string BackgroundSecondary = "#F4F5F7"; // Secondary background
        public const string Border = "#DFE1E6";           // Border color

        // Status colors
        public const string Success = "#00875A";          // Green
        public const string SuccessLight = "#E3FCEF";
        public const string Warning = "#FF991F";          // Orange
        public const string WarningLight = "#FFF0E0";
        public const string Error = "#DE350B";            // Red
        public const string ErrorLight = "#FFEBE6";
        public const string Info = "#0065FF";             // Blue
        public const string InfoLight = "#DEEBFF";

        // Accent
        public const string Primary = "#0052CC";          // Primary action
        public const string PrimaryHover = "#0747A6";
        public const string PrimaryActive = "#003884";
    }

    // Typography
    public static class Typography
    {
        public const int H1Size = 29;
        public const int H2Size = 24;
        public const int H3Size = 20;
        public const int H4Size = 16;
        public const int BodySize = 14;
        public const int SmallSize = 12;
        public const int XSmallSize = 11;

        public const string FontFamily = "Segoe UI, -apple-system, BlinkMacSystemFont, sans-serif";
        public const string FontFamilyMonospace = "Consolas, Monaco, 'Courier New', monospace";

        public const int WeightNormal = 400;
        public const int WeightMedium = 500;
        public const int WeightBold = 600;
    }

    // Border Radius
    public static class BorderRadius
    {
        public const int Small = 3;
        public const int Medium = 8;
        public const int Large = 12;
        public const int Round = 16;
    }

    // Elevation (Box shadows)
    public static class Elevation
    {
        public const string Card = "0 1px 1px rgba(9,30,66,0.25), 0 0 1px rgba(9,30,66,0.31)";
        public const string Raised = "0 4px 8px -2px rgba(9,30,66,0.25), 0 0 1px rgba(9,30,66,0.31)";
        public const string Overlay = "0 8px 16px -4px rgba(9,30,66,0.25), 0 0 1px rgba(9,30,66,0.31)";
    }

    // Icon sizes
    public static class IconSize
    {
        public const int Small = 16;
        public const int Medium = 20;
        public const int Large = 24;
        public const int XLarge = 32;
    }

    // Animation durations (milliseconds)
    public static class Animation
    {
        public const int Fast = 100;
        public const int Normal = 200;
        public const int Slow = 300;
    }
}
