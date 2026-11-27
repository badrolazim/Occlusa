using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Occlusa.Desktop.Themes;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Pinned clinical safety banner displaying patient identity, MyKad IC verification status,
/// and high-visibility allergy/anticoagulant/diabetes/pregnancy alerts.
/// Uses standalone DependencyProperties with zero external model dependencies.
/// </summary>
public partial class MedicalAlertsBanner : UserControl
{
    public static readonly DependencyProperty PatientNameProperty = DependencyProperty.Register(
        nameof(PatientName),
        typeof(string),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(string.Empty, OnBannerInputChanged));

    public static readonly DependencyProperty IcNumberProperty = DependencyProperty.Register(
        nameof(IcNumber),
        typeof(string),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(string.Empty, OnBannerInputChanged));

    public static readonly DependencyProperty IsMyKadVerifiedProperty = DependencyProperty.Register(
        nameof(IsMyKadVerified),
        typeof(bool),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(false, OnBannerInputChanged));

    public static readonly DependencyProperty AllergiesProperty = DependencyProperty.Register(
        nameof(Allergies),
        typeof(string),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(string.Empty, OnBannerInputChanged));

    public static readonly DependencyProperty OnAnticoagulantsProperty = DependencyProperty.Register(
        nameof(OnAnticoagulants),
        typeof(bool),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(false, OnBannerInputChanged));

    public static readonly DependencyProperty HasDiabetesProperty = DependencyProperty.Register(
        nameof(HasDiabetes),
        typeof(bool),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(false, OnBannerInputChanged));

    public static readonly DependencyProperty IsPregnantProperty = DependencyProperty.Register(
        nameof(IsPregnant),
        typeof(bool),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(false, OnBannerInputChanged));

    public static readonly DependencyProperty AlertNotesProperty = DependencyProperty.Register(
        nameof(AlertNotes),
        typeof(string),
        typeof(MedicalAlertsBanner),
        new PropertyMetadata(string.Empty, OnBannerInputChanged));

    public static readonly DependencyProperty HasPatientProperty = DependencyProperty.Register(
        nameof(HasPatient), typeof(bool), typeof(MedicalAlertsBanner), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowIdleStateProperty = DependencyProperty.Register(
        nameof(ShowIdleState), typeof(bool), typeof(MedicalAlertsBanner), new PropertyMetadata(true));

    public static readonly DependencyProperty HasIcNumberProperty = DependencyProperty.Register(
        nameof(HasIcNumber), typeof(bool), typeof(MedicalAlertsBanner), new PropertyMetadata(false));

    public static readonly DependencyProperty HasAllergiesProperty = DependencyProperty.Register(
        nameof(HasAllergies), typeof(bool), typeof(MedicalAlertsBanner), new PropertyMetadata(false));

    public static readonly DependencyProperty HasAlertNotesProperty = DependencyProperty.Register(
        nameof(HasAlertNotes), typeof(bool), typeof(MedicalAlertsBanner), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowClearBadgeProperty = DependencyProperty.Register(
        nameof(ShowClearBadge), typeof(bool), typeof(MedicalAlertsBanner), new PropertyMetadata(false));

    public static readonly DependencyProperty FormattedIcTextProperty = DependencyProperty.Register(
        nameof(FormattedIcText), typeof(string), typeof(MedicalAlertsBanner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FormattedAllergyTextProperty = DependencyProperty.Register(
        nameof(FormattedAllergyText), typeof(string), typeof(MedicalAlertsBanner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FormattedAlertNotesTextProperty = DependencyProperty.Register(
        nameof(FormattedAlertNotesText), typeof(string), typeof(MedicalAlertsBanner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AlertNotesForegroundProperty = DependencyProperty.Register(
        nameof(AlertNotesForeground), typeof(Brush), typeof(MedicalAlertsBanner), new PropertyMetadata(CalmThemePalette.TextSecondaryBrush));

    public static readonly DependencyProperty AlertNotesFontWeightProperty = DependencyProperty.Register(
        nameof(AlertNotesFontWeight), typeof(FontWeight), typeof(MedicalAlertsBanner), new PropertyMetadata(FontWeights.Normal));

    public static readonly DependencyProperty FullAlertNotesToolTipProperty = DependencyProperty.Register(
        nameof(FullAlertNotesToolTip), typeof(string), typeof(MedicalAlertsBanner), new PropertyMetadata((string?)null));

    public static readonly DependencyProperty FullAllergyToolTipProperty = DependencyProperty.Register(
        nameof(FullAllergyToolTip), typeof(string), typeof(MedicalAlertsBanner), new PropertyMetadata((string?)null));

    public static readonly DependencyProperty MyKadBadgeTextProperty = DependencyProperty.Register(
        nameof(MyKadBadgeText), typeof(string), typeof(MedicalAlertsBanner), new PropertyMetadata("Manual IC"));

    public static readonly DependencyProperty MyKadBadgeBackgroundProperty = DependencyProperty.Register(
        nameof(MyKadBadgeBackground), typeof(Brush), typeof(MedicalAlertsBanner), new PropertyMetadata(CalmThemePalette.BgSubtleBrush));

    public static readonly DependencyProperty MyKadBadgeBorderProperty = DependencyProperty.Register(
        nameof(MyKadBadgeBorder), typeof(Brush), typeof(MedicalAlertsBanner), new PropertyMetadata(CalmThemePalette.BorderDefaultBrush));

    public static readonly DependencyProperty MyKadBadgeForegroundProperty = DependencyProperty.Register(
        nameof(MyKadBadgeForeground), typeof(Brush), typeof(MedicalAlertsBanner), new PropertyMetadata(CalmThemePalette.TextSecondaryBrush));

    public static readonly DependencyProperty BannerBackgroundProperty = DependencyProperty.Register(
        nameof(BannerBackground), typeof(Brush), typeof(MedicalAlertsBanner), new PropertyMetadata(CalmThemePalette.BgSubtleBrush));

    public static readonly DependencyProperty BannerBorderBrushProperty = DependencyProperty.Register(
        nameof(BannerBorderBrush), typeof(Brush), typeof(MedicalAlertsBanner), new PropertyMetadata(CalmThemePalette.BorderDefaultBrush));

    public static readonly DependencyProperty BannerAccentBrushProperty = DependencyProperty.Register(
        nameof(BannerAccentBrush), typeof(Brush), typeof(MedicalAlertsBanner), new PropertyMetadata(CalmThemePalette.BorderDefaultBrush));

    public MedicalAlertsBanner()
    {
        InitializeComponent();
        RefreshVisualState();
    }

    public string PatientName
    {
        get => (string)GetValue(PatientNameProperty);
        set => SetValue(PatientNameProperty, value);
    }

    public string IcNumber
    {
        get => (string)GetValue(IcNumberProperty);
        set => SetValue(IcNumberProperty, value);
    }

    public bool IsMyKadVerified
    {
        get => (bool)GetValue(IsMyKadVerifiedProperty);
        set => SetValue(IsMyKadVerifiedProperty, value);
    }

    public string Allergies
    {
        get => (string)GetValue(AllergiesProperty);
        set => SetValue(AllergiesProperty, value);
    }

    public bool OnAnticoagulants
    {
        get => (bool)GetValue(OnAnticoagulantsProperty);
        set => SetValue(OnAnticoagulantsProperty, value);
    }

    public bool HasDiabetes
    {
        get => (bool)GetValue(HasDiabetesProperty);
        set => SetValue(HasDiabetesProperty, value);
    }

    public bool IsPregnant
    {
        get => (bool)GetValue(IsPregnantProperty);
        set => SetValue(IsPregnantProperty, value);
    }

    public string AlertNotes
    {
        get => (string)GetValue(AlertNotesProperty);
        set => SetValue(AlertNotesProperty, value);
    }

    public bool HasPatient
    {
        get => (bool)GetValue(HasPatientProperty);
        private set => SetValue(HasPatientProperty, value);
    }

    public bool ShowIdleState
    {
        get => (bool)GetValue(ShowIdleStateProperty);
        private set => SetValue(ShowIdleStateProperty, value);
    }

    public bool HasIcNumber
    {
        get => (bool)GetValue(HasIcNumberProperty);
        private set => SetValue(HasIcNumberProperty, value);
    }

    public bool HasAllergies
    {
        get => (bool)GetValue(HasAllergiesProperty);
        private set => SetValue(HasAllergiesProperty, value);
    }

    public bool HasAlertNotes
    {
        get => (bool)GetValue(HasAlertNotesProperty);
        private set => SetValue(HasAlertNotesProperty, value);
    }

    public bool ShowClearBadge
    {
        get => (bool)GetValue(ShowClearBadgeProperty);
        private set => SetValue(ShowClearBadgeProperty, value);
    }

    public string FormattedIcText
    {
        get => (string)GetValue(FormattedIcTextProperty);
        private set => SetValue(FormattedIcTextProperty, value);
    }

    public string FormattedAllergyText
    {
        get => (string)GetValue(FormattedAllergyTextProperty);
        private set => SetValue(FormattedAllergyTextProperty, value);
    }

    public string FormattedAlertNotesText
    {
        get => (string)GetValue(FormattedAlertNotesTextProperty);
        private set => SetValue(FormattedAlertNotesTextProperty, value);
    }

    public Brush AlertNotesForeground
    {
        get => (Brush)GetValue(AlertNotesForegroundProperty);
        private set => SetValue(AlertNotesForegroundProperty, value);
    }

    public FontWeight AlertNotesFontWeight
    {
        get => (FontWeight)GetValue(AlertNotesFontWeightProperty);
        private set => SetValue(AlertNotesFontWeightProperty, value);
    }

    public string? FullAlertNotesToolTip
    {
        get => (string?)GetValue(FullAlertNotesToolTipProperty);
        private set => SetValue(FullAlertNotesToolTipProperty, value);
    }

    public string? FullAllergyToolTip
    {
        get => (string?)GetValue(FullAllergyToolTipProperty);
        private set => SetValue(FullAllergyToolTipProperty, value);
    }

    public string MyKadBadgeText
    {
        get => (string)GetValue(MyKadBadgeTextProperty);
        private set => SetValue(MyKadBadgeTextProperty, value);
    }

    public Brush MyKadBadgeBackground
    {
        get => (Brush)GetValue(MyKadBadgeBackgroundProperty);
        private set => SetValue(MyKadBadgeBackgroundProperty, value);
    }

    public Brush MyKadBadgeBorder
    {
        get => (Brush)GetValue(MyKadBadgeBorderProperty);
        private set => SetValue(MyKadBadgeBorderProperty, value);
    }

    public Brush MyKadBadgeForeground
    {
        get => (Brush)GetValue(MyKadBadgeForegroundProperty);
        private set => SetValue(MyKadBadgeForegroundProperty, value);
    }

    public Brush BannerBackground
    {
        get => (Brush)GetValue(BannerBackgroundProperty);
        private set => SetValue(BannerBackgroundProperty, value);
    }

    public Brush BannerBorderBrush
    {
        get => (Brush)GetValue(BannerBorderBrushProperty);
        private set => SetValue(BannerBorderBrushProperty, value);
    }

    public Brush BannerAccentBrush
    {
        get => (Brush)GetValue(BannerAccentBrushProperty);
        private set => SetValue(BannerAccentBrushProperty, value);
    }

    private static void OnBannerInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MedicalAlertsBanner)d).RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        bool hasPatient = !string.IsNullOrWhiteSpace(PatientName);
        HasPatient = hasPatient;
        ShowIdleState = !hasPatient;

        bool hasIc = !string.IsNullOrWhiteSpace(IcNumber);
        HasIcNumber = hasIc;
        FormattedIcText = hasIc ? $"IC: {IcNumber.Trim()}" : string.Empty;

        string trimmedAllergy = (Allergies ?? string.Empty).Trim();
        bool hasAllergy = !string.IsNullOrWhiteSpace(trimmedAllergy)
                          && !trimmedAllergy.Equals("None", StringComparison.OrdinalIgnoreCase)
                          && !trimmedAllergy.Equals("Nil", StringComparison.OrdinalIgnoreCase)
                          && !trimmedAllergy.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                          && !trimmedAllergy.Equals("-", StringComparison.OrdinalIgnoreCase);

        HasAllergies = hasAllergy;
        FormattedAllergyText = hasAllergy ? $"ALLERGY: {CompactSingleLine(trimmedAllergy, 36)}" : string.Empty;
        FullAllergyToolTip = hasAllergy ? $"ALLERGY: {trimmedAllergy}" : null;

        string trimmedNotes = (AlertNotes ?? string.Empty).Trim();
        HasAlertNotes = hasPatient && !string.IsNullOrWhiteSpace(trimmedNotes);

        if (IsMyKadVerified)
        {
            MyKadBadgeText = "✓ MyKad Verified";
            MyKadBadgeBackground = CalmThemePalette.StatusSuccessBgBrush;
            MyKadBadgeBorder = CalmThemePalette.StatusSuccessBrush;
            MyKadBadgeForeground = CalmThemePalette.StatusSuccessBrush;
        }
        else
        {
            MyKadBadgeText = "Manual IC (Unverified)";
            MyKadBadgeBackground = CalmThemePalette.StatusWarningBgBrush;
            MyKadBadgeBorder = CalmThemePalette.StatusWarningBrush;
            MyKadBadgeForeground = CalmThemePalette.StatusWarningBrush;
        }

        if (!hasPatient)
        {
            ShowClearBadge = false;
            FormattedAlertNotesText = string.Empty;
            FullAlertNotesToolTip = null;
            AlertNotesForeground = CalmThemePalette.TextSecondaryBrush;
            AlertNotesFontWeight = FontWeights.Normal;
            BannerBackground = CalmThemePalette.BgSubtleBrush;
            BannerBorderBrush = CalmThemePalette.BorderDefaultBrush;
            BannerAccentBrush = CalmThemePalette.BorderDefaultBrush;
            return;
        }

        bool anyCriticalAlert = hasAllergy || OnAnticoagulants || HasDiabetes || IsPregnant || HasAlertNotes;
        ShowClearBadge = !anyCriticalAlert;

        if (HasAlertNotes)
        {
            FormattedAlertNotesText = $"Clinical Safety Note: {CompactSingleLine(trimmedNotes, 130)}";
            FullAlertNotesToolTip = $"Clinical Safety Note: {trimmedNotes}";
            AlertNotesForeground = CalmThemePalette.StatusErrorBrush;
            AlertNotesFontWeight = FontWeights.SemiBold;
        }
        else if (anyCriticalAlert)
        {
            FormattedAlertNotesText = "Clinical Safety Note: Review highlighted medical alert flags above before treatment.";
            FullAlertNotesToolTip = null;
            AlertNotesForeground = CalmThemePalette.StatusErrorBrush;
            AlertNotesFontWeight = FontWeights.Normal;
        }
        else
        {
            FormattedAlertNotesText = "Clinical Safety Note: None recorded — standard clinical precautions apply.";
            FullAlertNotesToolTip = null;
            AlertNotesForeground = CalmThemePalette.StatusSuccessBrush;
            AlertNotesFontWeight = FontWeights.Normal;
        }

        if (anyCriticalAlert)
        {
            BannerBackground = CalmThemePalette.StatusErrorBgBrush;
            BannerBorderBrush = CalmThemePalette.StatusErrorBrush;
            BannerAccentBrush = CalmThemePalette.StatusErrorBrush;
        }
        else
        {
            BannerBackground = CalmThemePalette.BgSurfaceBrush;
            BannerBorderBrush = CalmThemePalette.BorderDefaultBrush;
            BannerAccentBrush = CalmThemePalette.StatusSuccessBrush;
        }
    }

    private static string CompactSingleLine(string text, int maxLength)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', words);
        int limit = Math.Max(8, maxLength);
        if (normalized.Length <= limit)
        {
            return normalized;
        }

        var built = new List<string>();
        int currentLen = 0;

        foreach (var word in words)
        {
            int addedLen = built.Count == 0 ? word.Length : word.Length + 1;
            if (currentLen + addedLen > limit)
            {
                break;
            }

            built.Add(word);
            currentLen += addedLen;
        }

        return built.Count > 0 ? string.Join(' ', built) : words[0][..limit];
    }
}
