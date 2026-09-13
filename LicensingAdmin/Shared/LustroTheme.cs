using MudBlazor;

namespace LicensingAdmin.Shared;

/// <summary>
/// Dark glassmorphism palette/typography, styled after the "Lustro" design reference
/// (indigo/pink/cyan accents on a near-black ink background, Space Grotesk + DM Sans +
/// JetBrains Mono). Glass surface effects (blur/translucency) live in site.css since
/// MudTheme has no backdrop-filter equivalent.
/// </summary>
public static class LustroTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Black = "#0a0e1a",
            Background = "#0a0e1a",
            BackgroundGray = "#0d1220",
            Surface = "#0d1220",
            DrawerBackground = "#0d1220",
            DrawerText = "rgba(255,255,255,0.7)",
            DrawerIcon = "rgba(255,255,255,0.7)",
            AppbarBackground = "#0d1220",
            AppbarText = "#ffffff",
            Primary = "#6366f1",
            PrimaryContrastText = "#ffffff",
            Secondary = "#ec4899",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#a5b4fc",
            Info = "#22d3ee",
            Success = "#4ade80",
            Warning = "#fbbf24",
            Error = "#f87171",
            TextPrimary = "#ffffff",
            TextSecondary = "rgba(255,255,255,0.7)",
            TextDisabled = "rgba(255,255,255,0.3)",
            ActionDefault = "rgba(255,255,255,0.5)",
            ActionDisabled = "rgba(255,255,255,0.26)",
            ActionDisabledBackground = "rgba(255,255,255,0.12)",
            LinesDefault = "rgba(255,255,255,0.1)",
            LinesInputs = "rgba(255,255,255,0.22)",
            TableLines = "rgba(255,255,255,0.1)",
            Divider = "rgba(255,255,255,0.1)",
            DividerLight = "rgba(255,255,255,0.06)",
            OverlayDark = "rgba(10,14,26,0.6)",
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = ["DM Sans", "sans-serif"],
            },
            H1 = new H1 { FontFamily = ["Space Grotesk", "sans-serif"] },
            H2 = new H2 { FontFamily = ["Space Grotesk", "sans-serif"] },
            H3 = new H3 { FontFamily = ["Space Grotesk", "sans-serif"] },
            H4 = new H4 { FontFamily = ["Space Grotesk", "sans-serif"] },
            H5 = new H5 { FontFamily = ["Space Grotesk", "sans-serif"] },
            H6 = new H6 { FontFamily = ["Space Grotesk", "sans-serif"] },
            Button = new Button { FontFamily = ["Space Grotesk", "sans-serif"] },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "16px",
            DrawerWidthLeft = "240px",
        },
    };
}
