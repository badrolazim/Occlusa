using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Occlusa.Desktop.Themes;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Kreloses-style native vector tooth control combining anatomical crown + multi-root silhouettes
/// with a 5-surface (M, O/I, D, B, L) occlusal cross aligned along the bite plane.
/// </summary>
public partial class AnatomicalToothControl : UserControl
{
    private static Brush EnamelWhite => CalmThemePalette.BgSurfaceBrush;
    private static Brush RootIvory => CalmThemePalette.StatusWarningBgBrush;
    private static Brush CariesRed => CalmThemePalette.StatusErrorBrush;
    private static Brush CariesSoft => CalmThemePalette.StatusErrorBgBrush;
    private static Brush FillingBlue => CalmThemePalette.StatusInfoBrush;
    private static Brush FillingSoft => CalmThemePalette.StatusInfoBgBrush;
    private static Brush CrownAccent => CalmThemePalette.StatusSuccessBrush;
    private static Brush CrownCapFill => CalmThemePalette.StatusSuccessBgBrush;
    private static Brush RctWarning => CalmThemePalette.StatusWarningBrush;
    private static Brush ImplantTeal => CalmThemePalette.BrandPrimaryBrush;
    private static Brush ImplantCrownFill => CalmThemePalette.BrandTintBrush;
    private static Brush ExtractSoft => CalmThemePalette.StatusErrorBgBrush;
    private static Brush MissingGray => CalmThemePalette.BorderDefaultBrush;
    private static Brush OutlineDark => CalmThemePalette.TextPrimaryBrush;
    private static Brush OutlineRoot => CalmThemePalette.TextSecondaryBrush;

    private static Brush StageDoneBg => CalmThemePalette.StatusSuccessBgBrush;
    private static Brush StageDoneFg => CalmThemePalette.StatusSuccessBrush;
    private static Brush StageAcptBg => CalmThemePalette.StatusInfoBgBrush;
    private static Brush StageAcptFg => CalmThemePalette.StatusInfoBrush;
    private static Brush StagePropBg => CalmThemePalette.StatusWarningBgBrush;
    private static Brush StagePropFg => CalmThemePalette.StatusWarningBrush;
    private static Brush StageBaseBg => CalmThemePalette.BgSubtleBrush;
    private static Brush StageBaseFg => CalmThemePalette.TextSecondaryBrush;

    public static readonly DependencyProperty ToothNumberProperty =
        DependencyProperty.Register(nameof(ToothNumber), typeof(int), typeof(AnatomicalToothControl),
            new PropertyMetadata(16, OnVisualStateChanged));

    public static readonly DependencyProperty ConditionProperty =
        DependencyProperty.Register(nameof(Condition), typeof(ToothCondition), typeof(AnatomicalToothControl),
            new PropertyMetadata(ToothCondition.Healthy, OnVisualStateChanged));

    public static readonly DependencyProperty SurfacesProperty =
        DependencyProperty.Register(nameof(Surfaces), typeof(string), typeof(AnatomicalToothControl),
            new PropertyMetadata(string.Empty, OnVisualStateChanged));

    public static readonly DependencyProperty StageCodeProperty =
        DependencyProperty.Register(nameof(StageCode), typeof(string), typeof(AnatomicalToothControl),
            new PropertyMetadata("-", OnVisualStateChanged));

    public AnatomicalToothControl()
    {
        InitializeComponent();
        RefreshVisuals();
    }

    public int ToothNumber
    {
        get => (int)GetValue(ToothNumberProperty);
        set => SetValue(ToothNumberProperty, value);
    }

    public ToothCondition Condition
    {
        get => (ToothCondition)GetValue(ConditionProperty);
        set => SetValue(ConditionProperty, value);
    }

    public string Surfaces
    {
        get => (string)GetValue(SurfacesProperty);
        set => SetValue(SurfacesProperty, value);
    }

    public string StageCode
    {
        get => (string)GetValue(StageCodeProperty);
        set => SetValue(StageCodeProperty, value);
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnatomicalToothControl ctrl)
        {
            ctrl.RefreshVisuals();
        }
    }

    private void RefreshVisuals()
    {
        int tooth = ToothNumber;
        var geom = ToothAnatomyCatalog.Get(tooth);
        var cond = Condition;
        var normSurf = OdontogramHelper.NormalizeSurfaces(Surfaces);
        var stage = StageCode ?? "-";

        ToothNumberText.Text = tooth.ToString();

        // Orient rows toward the occlusal bite plane:
        // Upper arch: Number (Row 0) -> Anatomy roots-up (Row 1) -> Occlusal Cross (Row 2)
        // Lower arch: Occlusal Cross (Row 0) -> Anatomy roots-down (Row 1) -> Number (Row 2)
        if (geom.IsUpperArch)
        {
            Grid.SetRow(HeaderBlock, 0);
            Grid.SetRow(OcclusalBox, 2);
        }
        else
        {
            Grid.SetRow(OcclusalBox, 0);
            Grid.SetRow(HeaderBlock, 2);
        }

        AnatomyCanvas.Width = geom.CanvasWidth;
        AnatomyCanvas.Height = geom.CanvasHeight;
        AnatomyTransform.ScaleX = geom.IsMirroredHorizontally ? -1.0 : 1.0;
        AnatomyTransform.ScaleY = geom.IsUpperArch ? 1.0 : -1.0;

        GumLine.X2 = geom.CanvasWidth - 2.0;
        GumLine.Y1 = geom.CejY;
        GumLine.Y2 = geom.CejY;

        RootsPath.Data = geom.RootsOnly;
        CrownPath.Data = geom.CrownCap;
        MissingGhostPath.Data = geom.FullSilhouette;
        RootCanalPath.Data = geom.RootCanals;
        ImplantPath.Data = geom.ImplantFixture;

        Canvas.SetLeft(CrownLesionDot, (geom.CanvasWidth * 0.5) - 7.5);
        Canvas.SetTop(CrownLesionDot, geom.CejY + 8.0);

        bool isMissing = cond == ToothCondition.Missing;
        bool isImplant = cond == ToothCondition.Implant;
        bool isCrown = cond == ToothCondition.Crown;
        bool isRct = cond == ToothCondition.RootCanal;
        bool isExtract = cond == ToothCondition.ExtractionNeeded;
        bool isCaries = cond == ToothCondition.Caries;
        bool isFilling = cond == ToothCondition.Filling;

        MissingGhostPath.Visibility = isMissing ? Visibility.Visible : Visibility.Collapsed;
        RootsPath.Visibility = (isMissing || isImplant) ? Visibility.Collapsed : Visibility.Visible;
        ImplantPath.Visibility = isImplant ? Visibility.Visible : Visibility.Collapsed;
        CrownPath.Visibility = isMissing ? Visibility.Collapsed : Visibility.Visible;
        RootCanalPath.Visibility = isRct ? Visibility.Visible : Visibility.Collapsed;
        ExtractionCross.Visibility = isExtract ? Visibility.Visible : Visibility.Collapsed;
        CrownLesionDot.Visibility = (isCaries || isFilling) ? Visibility.Visible : Visibility.Collapsed;

        RootsPath.Fill = RootIvory;
        RootsPath.Stroke = OutlineRoot;

        if (isCrown)
        {
            CrownPath.Fill = CrownCapFill;
            CrownPath.Stroke = CrownAccent;
        }
        else if (isImplant)
        {
            CrownPath.Fill = ImplantCrownFill;
            CrownPath.Stroke = ImplantTeal;
        }
        else if (isCaries)
        {
            CrownPath.Fill = CariesSoft;
            CrownPath.Stroke = CariesRed;
            CrownLesionDot.Fill = CariesRed;
        }
        else if (isFilling)
        {
            CrownPath.Fill = FillingSoft;
            CrownPath.Stroke = FillingBlue;
            CrownLesionDot.Fill = FillingBlue;
        }
        else if (isExtract)
        {
            CrownPath.Fill = ExtractSoft;
            CrownPath.Stroke = CariesRed;
        }
        else
        {
            CrownPath.Fill = EnamelWhite;
            CrownPath.Stroke = OutlineDark;
        }

        // 5-Surface Occlusal Cross Mapping (Midline-Aware Mesial vs Distal!)
        // In patient-facing dental chart:
        // Right quadrants (Q1, Q4, Q5, Q8): Left polygon = Distal ('D'), Right polygon = Mesial ('M') (towards midline!)
        // Left quadrants (Q2, Q3, Q6, Q7): Left polygon = Mesial ('M') (towards midline!), Right polygon = Distal ('D')
        bool isLeftQuadrant = OdontogramHelper.IsMirroredLeftQuadrant(tooth);
        char leftSurfChar = isLeftQuadrant ? 'M' : 'D';
        char rightSurfChar = isLeftQuadrant ? 'D' : 'M';
        // Upper arch: Top polygon = Buccal ('B'), Bottom = Lingual/Palatal ('L')
        // Lower arch: Top polygon (towards bite) = Lingual ('L'), Bottom (outer lip/cheek) = Buccal ('B')
        char topSurfChar = geom.IsUpperArch ? 'B' : 'L';
        char bottomSurfChar = geom.IsUpperArch ? 'L' : 'B';

        OcclusalBox.Opacity = isMissing ? 0.25 : 1.0;
        Brush activeSurfBrush = cond switch
        {
            ToothCondition.Caries => CariesRed,
            ToothCondition.Filling => FillingBlue,
            ToothCondition.Crown => CrownAccent,
            ToothCondition.RootCanal => RctWarning,
            ToothCondition.Implant => ImplantTeal,
            ToothCondition.ExtractionNeeded => CariesRed,
            _ => EnamelWhite
        };

        bool colorAllCrown = isCrown;
        SurfBuccal.Fill = ShouldFillSurface(cond, normSurf, topSurfChar, colorAllCrown) ? activeSurfBrush : EnamelWhite;
        SurfLingual.Fill = ShouldFillSurface(cond, normSurf, bottomSurfChar, colorAllCrown) ? activeSurfBrush : EnamelWhite;
        SurfLeft.Fill = ShouldFillSurface(cond, normSurf, leftSurfChar, colorAllCrown) ? activeSurfBrush : EnamelWhite;
        SurfRight.Fill = ShouldFillSurface(cond, normSurf, rightSurfChar, colorAllCrown) ? activeSurfBrush : EnamelWhite;
        SurfOcclusal.Fill = ShouldFillSurface(cond, normSurf, 'O', colorAllCrown || isRct) ? activeSurfBrush : (isMissing ? MissingGray : EnamelWhite);

        // Stage Pill
        if (string.IsNullOrWhiteSpace(stage) || stage == "-")
        {
            StagePill.Visibility = Visibility.Collapsed;
        }
        else
        {
            StagePill.Visibility = Visibility.Visible;
            StageText.Text = stage;
            (StagePill.Background, StageText.Foreground) = stage switch
            {
                "DONE" => (StageDoneBg, StageDoneFg),
                "ACPT" => (StageAcptBg, StageAcptFg),
                "PROP" => (StagePropBg, StagePropFg),
                _ => (StageBaseBg, StageBaseFg)
            };
        }
    }

    private static bool ShouldFillSurface(ToothCondition cond, string normSurfaces, char target, bool forceAll)
    {
        if (cond is ToothCondition.Healthy or ToothCondition.Missing)
        {
            return false;
        }

        if (forceAll)
        {
            return true;
        }

        if (string.IsNullOrEmpty(normSurfaces))
        {
            // Default center occlusal highlight when condition is set without specific surface letters
            return target == 'O' && cond is ToothCondition.Caries or ToothCondition.Filling;
        }

        return normSurfaces.Contains(target);
    }
}

