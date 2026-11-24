using System.Windows;
using System.Windows.Media;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Renders ultra-soft, inset hour and half-hour rhythm guides (without heavy spreadsheet gridlines)
/// plus an optional current-time indicator line inside a timeline column.
/// </summary>
public sealed class TimelineBackdropControl : FrameworkElement
{
    private static readonly Pen HourPen = CreateFrozenPen(Color.FromRgb(0xED, 0xF2, 0xF7), 0.9);
    private static readonly Pen HalfHourPen = CreateFrozenPen(Color.FromRgb(0xF5, 0xF7, 0xFA), 0.75, new DashStyle([3, 4], 0));
    private static readonly Brush NowDotBrush = CreateFrozenBrush(Color.FromRgb(0x0F, 0x76, 0x6E));
    private static readonly Pen NowPen = CreateFrozenPen(Color.FromRgb(0x0F, 0x76, 0x6E), 1.5);

    public static readonly DependencyProperty TotalRowsProperty = DependencyProperty.Register(
        nameof(TotalRows),
        typeof(int),
        typeof(TimelineBackdropControl),
        new FrameworkPropertyMetadata(36, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RowsPerHourProperty = DependencyProperty.Register(
        nameof(RowsPerHour),
        typeof(int),
        typeof(TimelineBackdropControl),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HourOffsetRowsProperty = DependencyProperty.Register(
        nameof(HourOffsetRows),
        typeof(int),
        typeof(TimelineBackdropControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowCurrentTimeLineProperty = DependencyProperty.Register(
        nameof(ShowCurrentTimeLine),
        typeof(bool),
        typeof(TimelineBackdropControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentTimeRowFractionProperty = DependencyProperty.Register(
        nameof(CurrentTimeRowFraction),
        typeof(double),
        typeof(TimelineBackdropControl),
        new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimelineBackdropControl()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public int TotalRows
    {
        get => (int)GetValue(TotalRowsProperty);
        set => SetValue(TotalRowsProperty, value);
    }

    public int RowsPerHour
    {
        get => (int)GetValue(RowsPerHourProperty);
        set => SetValue(RowsPerHourProperty, value);
    }

    public int HourOffsetRows
    {
        get => (int)GetValue(HourOffsetRowsProperty);
        set => SetValue(HourOffsetRowsProperty, value);
    }

    public bool ShowCurrentTimeLine
    {
        get => (bool)GetValue(ShowCurrentTimeLineProperty);
        set => SetValue(ShowCurrentTimeLineProperty, value);
    }

    public double CurrentTimeRowFraction
    {
        get => (double)GetValue(CurrentTimeRowFractionProperty);
        set => SetValue(CurrentTimeRowFractionProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ActualWidth <= 0 || ActualHeight <= 0 || TotalRows <= 0)
        {
            return;
        }

        double rowHeight = ActualHeight / TotalRows;
        int rowsPerHour = Math.Max(1, RowsPerHour);
        int halfStep = rowsPerHour / 2;

        for (int r = 1; r < TotalRows; r++)
        {
            double y = Math.Round(r * rowHeight) + 0.5;
            int rel = (r - HourOffsetRows + rowsPerHour * 10) % rowsPerHour;

            if (rel == 0)
            {
                dc.DrawLine(HourPen, new Point(6, y), new Point(Math.Max(6, ActualWidth - 6), y));
            }
            else if (halfStep > 0 && rel == halfStep)
            {
                dc.DrawLine(HalfHourPen, new Point(10, y), new Point(Math.Max(10, ActualWidth - 10), y));
            }
        }

        if (ShowCurrentTimeLine && CurrentTimeRowFraction >= 0 && CurrentTimeRowFraction <= TotalRows)
        {
            double nowY = CurrentTimeRowFraction * rowHeight;
            dc.DrawLine(NowPen, new Point(4, nowY), new Point(Math.Max(4, ActualWidth - 4), nowY));
            dc.DrawEllipse(NowDotBrush, null, new Point(6, nowY), 3.2, 3.2);
        }
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness, DashStyle? dashStyle = null)
    {
        var brush = CreateFrozenBrush(color);
        var pen = new Pen(brush, thickness);
        if (dashStyle != null)
        {
            pen.DashStyle = dashStyle;
        }
        pen.Freeze();
        return pen;
    }
}

