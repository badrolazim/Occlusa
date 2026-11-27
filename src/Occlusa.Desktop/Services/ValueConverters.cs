using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Occlusa.Desktop.Themes;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.Services;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (Invert || string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is Visibility v && v == Visibility.Visible;
        if (Invert || string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }
        return visible;
    }
}

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasText = !string.IsNullOrWhiteSpace(value?.ToString());
        if (Invert) hasText = !hasText;
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNotNull = value != null;
        if (Invert) isNotNull = !isNotNull;
        return isNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class ToothConditionToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var condition = value is ToothCondition tc ? tc : ToothCondition.Healthy;
        string resourceKey = condition switch
        {
            ToothCondition.Caries => "ToothCariesBrush",
            ToothCondition.Filling => "ToothFillingBrush",
            ToothCondition.Crown => "ToothCrownBrush",
            ToothCondition.RootCanal => "ToothRootCanalBrush",
            ToothCondition.ExtractionNeeded => "ToothExtractionBrush",
            ToothCondition.Missing => "ToothMissingBrush",
            ToothCondition.Implant => "ToothImplantBrush",
            _ => "ToothHealthyBrush"
        };

        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return CalmThemePalette.BgSurfaceBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
