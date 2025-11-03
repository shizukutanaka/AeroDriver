// Design Tokens based on Atlassian Design System
// Provides consistent colors, spacing, typography, and elevation for UI components

using Avalonia.Media;
using System.Collections.Generic;

namespace AeroDriver.UI;

/// <summary>
/// Atlassian-inspired design tokens for consistent UI styling
/// </summary>
public static class DesignTokens
{
    // ===== COLORS =====

    // Neutral Colors (Grays)
    public static class Neutral
    {
        public static readonly Color N0 = Color.Parse("#FFFFFF");
        public static readonly Color N10 = Color.Parse("#FAFBFC");
        public static readonly Color N20 = Color.Parse("#F4F5F7");
        public static readonly Color N30 = Color.Parse("#EBECF0");
        public static readonly Color N40 = Color.Parse("#DFE1E6");
        public static readonly Color N50 = Color.Parse("#C1C7D0");
        public static readonly Color N60 = Color.Parse("#B3BAC5");
        public static readonly Color N70 = Color.Parse("#A5ADBA");
        public static readonly Color N80 = Color.Parse("#97A0AF");
        public static readonly Color N90 = Color.Parse("#8993A4");
        public static readonly Color N100 = Color.Parse("#7A869A");
        public static readonly Color N200 = Color.Parse("#6B778C");
        public static readonly Color N300 = Color.Parse("#5E6C84");
        public static readonly Color N400 = Color.Parse("#505F79");
        public static readonly Color N500 = Color.Parse("#42526E");
        public static readonly Color N600 = Color.Parse("#344563");
        public static readonly Color N700 = Color.Parse("#253858");
        public static readonly Color N800 = Color.Parse("#172B4D");
        public static readonly Color N900 = Color.Parse("#091E42");
    }

    // Blue Colors (Primary)
    public static class Blue
    {
        public static readonly Color B50 = Color.Parse("#DEEBFF");
        public static readonly Color B75 = Color.Parse("#B3D4FF");
        public static readonly Color B100 = Color.Parse("#4C9AFF");
        public static readonly Color B200 = Color.Parse("#2684FF");
        public static readonly Color B300 = Color.Parse("#0065FF");
        public static readonly Color B400 = Color.Parse("#0052CC");
        public static readonly Color B500 = Color.Parse("#0747A6");
        public static readonly Color B600 = Color.Parse("#0F3663");
        public static readonly Color B700 = Color.Parse("#0C2745");
        public static readonly Color B800 = Color.Parse("#0A1F35");
        public static readonly Color B900 = Color.Parse("#081225");
    }

    // Green Colors (Success)
    public static class Green
    {
        public static readonly Color G50 = Color.Parse("#E3FCEF");
        public static readonly Color G75 = Color.Parse("#ABF5D1");
        public static readonly Color G100 = Color.Parse("#79F2C0");
        public static readonly Color G200 = Color.Parse("#57D9A3");
        public static readonly Color G300 = Color.Parse("#36B37E");
        public static readonly Color G400 = Color.Parse("#00875A");
        public static readonly Color G500 = Color.Parse("#006644");
        public static readonly Color G600 = Color.Parse("#00552E");
        public static readonly Color G700 = Color.Parse("#004224");
        public static readonly Color G800 = Color.Parse("#003518");
        public static readonly Color G900 = Color.Parse("#002A11");
    }

    // Yellow Colors (Warning)
    public static class Yellow
    {
        public static readonly Color Y50 = Color.Parse("#FFF7D6");
        public static readonly Color Y75 = Color.Parse("#FFF0B3");
        public static readonly Color Y100 = Color.Parse("#FFE380");
        public static readonly Color Y200 = Color.Parse("#FFC400");
        public static readonly Color Y300 = Color.Parse("#FFAB00");
        public static readonly Color Y400 = Color.Parse("#FF991F");
        public static readonly Color Y500 = Color.Parse("#FF8B00");
        public static readonly Color Y600 = Color.Parse("#FF7F00");
        public static readonly Color Y700 = Color.Parse("#E87200");
        public static readonly Color Y800 = Color.Parse("#C65F00");
        public static readonly Color Y900 = Color.Parse("#A44700");
    }

    // Red Colors (Danger/Error)
    public static class Red
    {
        public static readonly Color R50 = Color.Parse("#FFEBE6");
        public static readonly Color R75 = Color.Parse("#FFBDAD");
        public static readonly Color R100 = Color.Parse("#FF8F73");
        public static readonly Color R200 = Color.Parse("#FF7452");
        public static readonly Color R300 = Color.Parse("#FF5630");
        public static readonly Color R400 = Color.Parse("#DE350B");
        public static readonly Color R500 = Color.Parse("#BF2600");
        public static readonly Color R600 = Color.Parse("#A41F00");
        public static readonly Color R700 = Color.Parse("#8B1C00");
        public static readonly Color R800 = Color.Parse("#731700");
        public static readonly Color R900 = Color.Parse("#5B1300");
    }

    // Purple Colors (Information)
    public static class Purple
    {
        public static readonly Color P50 = Color.Parse("#EAE6FF");
        public static readonly Color P75 = Color.Parse("#C0B6F2");
        public static readonly Color P100 = Color.Parse("#998DD9");
        public static readonly Color P200 = Color.Parse("#8777D9");
        public static readonly Color P300 = Color.Parse("#6554C0");
        public static readonly Color P400 = Color.Parse("#5243AA");
        public static readonly Color P500 = Color.Parse("#403294");
        public static readonly Color P600 = Color.Parse("#352C7A");
        public static readonly Color P700 = Color.Parse("#2B2457");
        public static readonly Color P800 = Color.Parse("#211C46");
        public static readonly Color P900 = Color.Parse("#1A1535");
    }

    // ===== SPACING =====

    public static class Spacing
    {
        public static readonly double Space025 = 2;  // 2px
        public static readonly double Space050 = 4;  // 4px
        public static readonly double Space075 = 6;  // 6px
        public static readonly double Space100 = 8;  // 8px
        public static readonly double Space150 = 12; // 12px
        public static readonly double Space200 = 16; // 16px
        public static readonly double Space250 = 20; // 20px
        public static readonly double Space300 = 24; // 24px
        public static readonly double Space400 = 32; // 32px
        public static readonly double Space500 = 40; // 40px
        public static readonly double Space600 = 48; // 48px
        public static readonly double Space800 = 64; // 64px
        public static readonly double Space1000 = 80; // 80px
    }

    // ===== TYPOGRAPHY =====

    public static class Typography
    {
        // Font families
        public static readonly string FontFamily = "Segoe UI, system-ui, -apple-system, sans-serif";
        public static readonly string FontFamilyMonospace = "SF Mono, Monaco, 'Cascadia Code', 'Roboto Mono', Consolas, 'Courier New', monospace";

        // Font sizes
        public static class FontSize
        {
            public static readonly double Size11 = 11;
            public static readonly double Size12 = 12;
            public static readonly double Size14 = 14;
            public static readonly double Size16 = 16;
            public static readonly double Size18 = 18;
            public static readonly double Size20 = 20;
            public static readonly double Size24 = 24;
            public static readonly double Size29 = 29;
            public static readonly double Size35 = 35;
            public static readonly double Size41 = 41;
            public static readonly double Size48 = 48;
        }

        // Font weights
        public static class FontWeight
        {
            public static readonly Avalonia.Media.FontWeight Normal = Avalonia.Media.FontWeight.Normal;
            public static readonly Avalonia.Media.FontWeight Medium = Avalonia.Media.FontWeight.Medium;
            public static readonly Avalonia.Media.FontWeight SemiBold = Avalonia.Media.FontWeight.SemiBold;
            public static readonly Avalonia.Media.FontWeight Bold = Avalonia.Media.FontWeight.Bold;
        }

        // Line heights
        public static class LineHeight
        {
            public static readonly double Height100 = 1.0;
            public static readonly double Height120 = 1.2;
            public static readonly double Height133 = 1.33;
            public static readonly double Height150 = 1.5;
            public static readonly double Height160 = 1.6;
        }
    }

    // ===== ELEVATION (Shadows) =====

    public static class Elevation
    {
        public static readonly BoxShadows Shadow100 = new BoxShadows(
            new BoxShadow { OffsetX = 0, OffsetY = 1, Blur = 1, Color = Color.Parse("#091E4208") });
        public static readonly BoxShadows Shadow200 = new BoxShadows(
            new BoxShadow { OffsetX = 0, OffsetY = 2, Blur = 1, Color = Color.Parse("#091E420F") });
        public static readonly BoxShadows Shadow300 = new BoxShadows(
            new BoxShadow { OffsetX = 0, OffsetY = 1, Blur = 3, Color = Color.Parse("#091E4214") });
        public static readonly BoxShadows Shadow400 = new BoxShadows(
            new BoxShadow { OffsetX = 0, OffsetY = 2, Blur = 4, Color = Color.Parse("#091E4221") });
        public static readonly BoxShadows Shadow500 = new BoxShadows(
            new BoxShadow { OffsetX = 0, OffsetY = 1, Blur = 5, Color = Color.Parse("#091E4229") });
        public static readonly BoxShadows Shadow600 = new BoxShadows(
            new BoxShadow { OffsetX = 0, OffsetY = 1, Blur = 8, Color = Color.Parse("#091E423D") });
    }

    // ===== BORDER RADIUS =====

    public static class BorderRadius
    {
        public static readonly double Radius050 = 2;
        public static readonly double Radius075 = 3;
        public static readonly double Radius100 = 4;
        public static readonly double Radius150 = 6;
        public static readonly double Radius200 = 8;
        public static readonly double Radius300 = 12;
        public static readonly double Radius400 = 16;
        public static readonly double RadiusCircle = 50; // For pill shapes
    }

    // ===== SEMANTIC COLORS =====

    public static class SemanticColors
    {
        public static readonly Color Background = Neutral.N0;
        public static readonly Color Surface = Neutral.N10;
        public static readonly Color SurfaceHovered = Neutral.N20;
        public static readonly Color SurfacePressed = Neutral.N30;

        public static readonly Color Text = Neutral.N900;
        public static readonly Color TextSubtle = Neutral.N500;
        public static readonly Color TextDisabled = Neutral.N400;

        public static readonly Color Border = Neutral.N40;
        public static readonly Color BorderFocused = Blue.B200;

        public static readonly Color Primary = Blue.B400;
        public static readonly Color PrimaryHovered = Blue.B300;
        public static readonly Color PrimaryPressed = Blue.B500;

        public static readonly Color Success = Green.G400;
        public static readonly Color Warning = Yellow.Y400;
        public static readonly Color Danger = Red.R400;
        public static readonly Color Info = Purple.P400;
    }

    // ===== COMPONENT SPECIFIC TOKENS =====

    public static class Button
    {
        public static readonly double MinWidth = 80;
        public static readonly double Height = 32;
        public static readonly double HeightLarge = 40;
        public static readonly double BorderRadius = BorderRadius.Radius100;
        public static readonly double PaddingHorizontal = Spacing.Space150;
        public static readonly double PaddingVertical = Spacing.Space050;
    }

    public static class Input
    {
        public static readonly double Height = 32;
        public static readonly double BorderRadius = BorderRadius.Radius100;
        public static readonly double BorderWidth = 1;
        public static readonly double BorderWidthFocused = 2;
    }

    public static class Card
    {
        public static readonly double BorderRadius = BorderRadius.Radius200;
        public static readonly double Padding = Spacing.Space200;
        public static readonly BoxShadows Shadow = Elevation.Shadow100;
    }
}
