using System.Windows;
using System.Windows.Controls;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Proportional row-and-lane canvas panel arranging appointment blocks without scrollbars.
/// Supports multi-row durations (e.g. 15m = 1 row, 2h = 8 rows) and split overlap lanes.
/// </summary>
public sealed class ScheduleTimelinePanel : Panel
{
    public static readonly DependencyProperty TotalRowsProperty = DependencyProperty.Register(
        nameof(TotalRows),
        typeof(int),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(36, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty HorizontalInsetProperty = DependencyProperty.Register(
        nameof(HorizontalInset),
        typeof(double),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(2.5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty VerticalInsetProperty = DependencyProperty.Register(
        nameof(VerticalInset),
        typeof(double),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty StartRowProperty = DependencyProperty.RegisterAttached(
        "StartRow",
        typeof(int),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan",
        typeof(int),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty LaneIndexProperty = DependencyProperty.RegisterAttached(
        "LaneIndex",
        typeof(int),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty LaneSpanProperty = DependencyProperty.RegisterAttached(
        "LaneSpan",
        typeof(int),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty LaneCountProperty = DependencyProperty.RegisterAttached(
        "LaneCount",
        typeof(int),
        typeof(ScheduleTimelinePanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public int TotalRows
    {
        get => (int)GetValue(TotalRowsProperty);
        set => SetValue(TotalRowsProperty, value);
    }

    public double HorizontalInset
    {
        get => (double)GetValue(HorizontalInsetProperty);
        set => SetValue(HorizontalInsetProperty, value);
    }

    public double VerticalInset
    {
        get => (double)GetValue(VerticalInsetProperty);
        set => SetValue(VerticalInsetProperty, value);
    }

    public static int GetStartRow(DependencyObject element) => (int)element.GetValue(StartRowProperty);
    public static void SetStartRow(DependencyObject element, int value) => element.SetValue(StartRowProperty, value);

    public static int GetRowSpan(DependencyObject element) => (int)element.GetValue(RowSpanProperty);
    public static void SetRowSpan(DependencyObject element, int value) => element.SetValue(RowSpanProperty, value);

    public static int GetLaneIndex(DependencyObject element) => (int)element.GetValue(LaneIndexProperty);
    public static void SetLaneIndex(DependencyObject element, int value) => element.SetValue(LaneIndexProperty, value);

    public static int GetLaneSpan(DependencyObject element) => (int)element.GetValue(LaneSpanProperty);
    public static void SetLaneSpan(DependencyObject element, int value) => element.SetValue(LaneSpanProperty, value);

    public static int GetLaneCount(DependencyObject element) => (int)element.GetValue(LaneCountProperty);
    public static void SetLaneCount(DependencyObject element, int value) => element.SetValue(LaneCountProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 200.0 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) || availableSize.Height <= 0 ? 600.0 : availableSize.Height;

        int totalRows = Math.Max(1, TotalRows);
        double rowHeight = height / totalRows;
        double hInset = HorizontalInset;
        double vInset = VerticalInset;

        foreach (UIElement child in InternalChildren)
        {
            if (child == null)
            {
                continue;
            }

            int laneCount = Math.Max(1, GetLaneCount(child));
            int laneIndex = Math.Clamp(GetLaneIndex(child), 0, laneCount - 1);
            int laneSpan = Math.Clamp(GetLaneSpan(child), 1, laneCount - laneIndex);
            int rowSpan = Math.Max(1, GetRowSpan(child));

            double laneWidth = width / laneCount;
            double childWidth = Math.Max(18.0, laneSpan * laneWidth - hInset * 2.0);
            double childHeight = Math.Max(14.0, rowSpan * rowHeight - vInset * 2.0);
            child.Measure(new Size(childWidth, childHeight));
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int totalRows = Math.Max(1, TotalRows);
        double rowHeight = finalSize.Height / totalRows;
        double hInset = HorizontalInset;
        double vInset = VerticalInset;

        foreach (UIElement child in InternalChildren)
        {
            if (child == null)
            {
                continue;
            }

            int laneCount = Math.Max(1, GetLaneCount(child));
            int laneIndex = Math.Clamp(GetLaneIndex(child), 0, laneCount - 1);
            int laneSpan = Math.Clamp(GetLaneSpan(child), 1, laneCount - laneIndex);
            int startRow = Math.Clamp(GetStartRow(child), 0, totalRows - 1);
            int rowSpan = Math.Max(1, GetRowSpan(child));

            double laneWidth = finalSize.Width / laneCount;
            double x = laneIndex * laneWidth + hInset;
            double w = Math.Max(18.0, laneSpan * laneWidth - hInset * 2.0);
            double y = startRow * rowHeight + vInset;
            double maxAvailH = Math.Max(14.0, finalSize.Height - y);
            double h = Math.Max(14.0, Math.Min(maxAvailH, rowSpan * rowHeight - vInset * 2.0));

            child.Arrange(new Rect(x, y, w, h));
        }

        return finalSize;
    }
}

