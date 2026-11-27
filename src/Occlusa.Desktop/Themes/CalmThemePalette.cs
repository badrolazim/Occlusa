using System.Windows;
using System.Windows.Media;

namespace Occlusa.Desktop.Themes;

/// <summary>
/// Centralized Calm Dental App Color System tokens (60-30-10 Core + Semantic Palette).
/// Resolves frozen brushes and colors from <c>Themes/UniformTheme.xaml</c> when running inside WPF,
/// with exact palette fallbacks for headless or design-time execution.
/// </summary>
public static class CalmThemePalette
{
    // Core Palette (60-30-10)
    public static readonly Color BgCanvasColor = ParseColor("#F7FAFA");
    public static readonly Color BgSurfaceColor = ParseColor("#FFFFFF");
    public static readonly Color BgSubtleColor = ParseColor("#EDF3F3");
    public static readonly Color BorderDefaultColor = ParseColor("#D8E3E3");
    public static readonly Color TextPrimaryColor = ParseColor("#182929");
    public static readonly Color TextSecondaryColor = ParseColor("#566B6B");
    public static readonly Color BrandPrimaryColor = ParseColor("#069494");
    public static readonly Color BrandHoverColor = ParseColor("#057878");
    public static readonly Color BrandTintColor = ParseColor("#E3F4F4");

    // Semantic Palette (Calm State & Feedback)
    public static readonly Color StatusSuccessColor = ParseColor("#2B825B");
    public static readonly Color StatusSuccessBgColor = ParseColor("#EAF6F0");
    public static readonly Color StatusInfoColor = ParseColor("#2E6FA3");
    public static readonly Color StatusInfoBgColor = ParseColor("#EBF3FA");
    public static readonly Color StatusWarningColor = ParseColor("#C47B1B");
    public static readonly Color StatusWarningBgColor = ParseColor("#FDF4E7");
    public static readonly Color StatusErrorColor = ParseColor("#C44545");
    public static readonly Color StatusErrorBgColor = ParseColor("#FBECEC");

    // Frozen Fallback Brushes
    private static readonly SolidColorBrush BgCanvasFallback = FreezeBrush(BgCanvasColor);
    private static readonly SolidColorBrush BgSurfaceFallback = FreezeBrush(BgSurfaceColor);
    private static readonly SolidColorBrush BgSubtleFallback = FreezeBrush(BgSubtleColor);
    private static readonly SolidColorBrush BorderDefaultFallback = FreezeBrush(BorderDefaultColor);
    private static readonly SolidColorBrush TextPrimaryFallback = FreezeBrush(TextPrimaryColor);
    private static readonly SolidColorBrush TextSecondaryFallback = FreezeBrush(TextSecondaryColor);
    private static readonly SolidColorBrush BrandPrimaryFallback = FreezeBrush(BrandPrimaryColor);
    private static readonly SolidColorBrush BrandHoverFallback = FreezeBrush(BrandHoverColor);
    private static readonly SolidColorBrush BrandTintFallback = FreezeBrush(BrandTintColor);

    private static readonly SolidColorBrush StatusSuccessFallback = FreezeBrush(StatusSuccessColor);
    private static readonly SolidColorBrush StatusSuccessBgFallback = FreezeBrush(StatusSuccessBgColor);
    private static readonly SolidColorBrush StatusInfoFallback = FreezeBrush(StatusInfoColor);
    private static readonly SolidColorBrush StatusInfoBgFallback = FreezeBrush(StatusInfoBgColor);
    private static readonly SolidColorBrush StatusWarningFallback = FreezeBrush(StatusWarningColor);
    private static readonly SolidColorBrush StatusWarningBgFallback = FreezeBrush(StatusWarningBgColor);
    private static readonly SolidColorBrush StatusErrorFallback = FreezeBrush(StatusErrorColor);
    private static readonly SolidColorBrush StatusErrorBgFallback = FreezeBrush(StatusErrorBgColor);

    public static Brush BgCanvasBrush => ResolveBrush("BgCanvasBrush", BgCanvasFallback);
    public static Brush BgSurfaceBrush => ResolveBrush("BgSurfaceBrush", BgSurfaceFallback);
    public static Brush BgSubtleBrush => ResolveBrush("BgSubtleBrush", BgSubtleFallback);
    public static Brush BorderDefaultBrush => ResolveBrush("BorderDefaultBrush", BorderDefaultFallback);
    public static Brush TextPrimaryBrush => ResolveBrush("TextPrimaryBrush", TextPrimaryFallback);
    public static Brush TextSecondaryBrush => ResolveBrush("TextSecondaryBrush", TextSecondaryFallback);
    public static Brush BrandPrimaryBrush => ResolveBrush("BrandPrimaryBrush", BrandPrimaryFallback);
    public static Brush BrandHoverBrush => ResolveBrush("BrandHoverBrush", BrandHoverFallback);
    public static Brush BrandTintBrush => ResolveBrush("BrandTintBrush", BrandTintFallback);

    public static Brush StatusSuccessBrush => ResolveBrush("StatusSuccessBrush", StatusSuccessFallback);
    public static Brush StatusSuccessBgBrush => ResolveBrush("StatusSuccessBgBrush", StatusSuccessBgFallback);
    public static Brush StatusInfoBrush => ResolveBrush("StatusInfoBrush", StatusInfoFallback);
    public static Brush StatusInfoBgBrush => ResolveBrush("StatusInfoBgBrush", StatusInfoBgFallback);
    public static Brush StatusWarningBrush => ResolveBrush("StatusWarningBrush", StatusWarningFallback);
    public static Brush StatusWarningBgBrush => ResolveBrush("StatusWarningBgBrush", StatusWarningBgFallback);
    public static Brush StatusErrorBrush => ResolveBrush("StatusErrorBrush", StatusErrorFallback);
    public static Brush StatusErrorBgBrush => ResolveBrush("StatusErrorBgBrush", StatusErrorBgFallback);

    private static Brush ResolveBrush(string resourceKey, SolidColorBrush fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return fallback;
    }

    private static Color ParseColor(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex)!;

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

