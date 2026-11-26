using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Declarative column definition for <see cref="UniformPagedGrid"/>.
/// Supports unlimited priority-ordered atomic columns that automatically fit the host panel width
/// on strict single-line 36px rows with zero ellipsis truncation and zero right-edge clipping.
/// </summary>
public sealed class UniformColumnDef
{
    public UniformColumnDef()
    {
    }

    public UniformColumnDef(
        string header,
        string bindingPath,
        double starWidth = 1.0,
        string? stringFormat = null,
        int priority = 1)
    {
        Header = header;
        BindingPath = bindingPath;
        StarWidth = starWidth;
        StringFormat = stringFormat;
        Priority = Math.Max(1, priority);
    }

    public string Header { get; set; } = string.Empty;
    public string BindingPath { get; set; } = string.Empty;

    /// <summary>
    /// Display priority when panel width is constrained:
    /// 1 = Essential (always shown), 2 = Important, 3 = Secondary, 4 = Wide-monitor extended.
    /// Lower-priority columns auto-hide earlier so visible cells never clip or truncate to '...'.
    /// </summary>
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Relative share of surplus panel width after each visible column's measured single-line width is satisfied.
    /// </summary>
    public double StarWidth { get; set; } = 1.0;

    /// <summary>
    /// Alias for <see cref="StarWidth"/> to support either naming convention in XAML or code.
    /// </summary>
    public double WidthRatio
    {
        get => StarWidth;
        set => StarWidth = value;
    }

    public string? StringFormat { get; set; }
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;
    public bool IsMonospace { get; set; }
    public bool IsBold { get; set; }
    public double MinWidth { get; set; } = 70;
    public DataTemplate? CellTemplate { get; set; }

    /// <summary>
    /// Optional property path measured for custom <see cref="CellTemplate"/> columns so single-line
    /// width budgeting accounts for actual data length. Falls back to <see cref="BindingPath"/> when null.
    /// </summary>
    public string? MeasureBindingPath { get; set; }

    /// <summary>
    /// Extra DIP width reserved for badges, status dots, pill padding, and horizontal cell margins
    /// inside custom <see cref="CellTemplate"/> columns.
    /// </summary>
    public double MeasureExtraWidth { get; set; }
}

/// <summary>
/// Panel-fitted, priority-adaptive paginated grid with strict single-line 36px rows,
/// zero scrollbars, zero text truncation ('...'), zero right-edge clipping,
/// responsive 1280p/1080p/1440p layout, row hover tooltips, and double-click row activation.
/// </summary>
[ContentProperty(nameof(Columns))]
public partial class UniformPagedGrid : UserControl
{
    private const int LegacyMaxUniformColumns = 4;
    private const double ResolutionHeightThreshold = 1120.0;
    private const int PageSize1080p = 11;
    private const int PageSize1440p = 17;
    private const double AutoRowEstimatedHeight = 42.0;
    private const int FullDatasetMeasureLimit = 250;
    private const double StandardCellHorizontalChrome = 26.0;
    private const double HeaderHorizontalChrome = 24.0;
    private const double ViewportSafetyGutter = 6.0;

    private static readonly FontFamily PrimaryFontFamily = new("Segoe UI");
    private static readonly FontFamily MonoFontFamily = new("Consolas");
    private static readonly Typeface HeaderTypeface = new(PrimaryFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface PrimaryRegularTypeface = new(PrimaryFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface PrimaryBoldTypeface = new(PrimaryFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface MonoRegularTypeface = new(MonoFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface MonoBoldTypeface = new(MonoFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly SolidColorBrush PrimaryTextBrush = CreateFrozenBrush(15, 23, 42);
    private static readonly ConcurrentDictionary<(Type Type, string Path), PropertyInfo?[]> PropertyPathCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> ToolTipPropertyCache = new();

    private readonly List<object> _allItems = new();
    private readonly List<(UniformColumnDef Def, DataGridColumn Column)> _builtColumns = new();
    private INotifyCollectionChanged? _subscribedCollection;
    private Window? _hostWindow;
    private bool _hasExplicitPageSize;
    private bool _isApplyingAutoPageSize;
    private bool _responsiveLayoutQueued;
    private string _lastResponsiveSignature = string.Empty;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(UniformPagedGrid),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(ObservableCollection<UniformColumnDef>),
        typeof(UniformPagedGrid),
        new PropertyMetadata(null, OnColumnsCollectionPropertyChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(UniformPagedGrid),
        new PropertyMetadata(string.Empty, OnHeaderTextChanged));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(UniformPagedGrid),
        new PropertyMetadata(string.Empty, OnHeaderTextChanged));

    public static readonly DependencyProperty EmptyMessageProperty = DependencyProperty.Register(
        nameof(EmptyMessage),
        typeof(string),
        typeof(UniformPagedGrid),
        new PropertyMetadata("No records available."));

    public static readonly DependencyProperty PageSizeProperty = DependencyProperty.Register(
        nameof(PageSize),
        typeof(int),
        typeof(UniformPagedGrid),
        new PropertyMetadata(PageSize1080p, OnPageSizeChanged, CoercePageSize));

    public static readonly DependencyProperty AutoPageSizeProperty = DependencyProperty.Register(
        nameof(AutoPageSize),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(true, OnAutoPageSizeChanged));

    public static readonly DependencyProperty AllowAutoRowHeightProperty = DependencyProperty.Register(
        nameof(AllowAutoRowHeight),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(false, OnAllowAutoRowHeightChanged));

    public static readonly DependencyProperty FitColumnsToContentProperty = DependencyProperty.Register(
        nameof(FitColumnsToContent),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(false, OnFitColumnsToContentChanged));

    public static readonly DependencyProperty ResponsiveColumnsProperty = DependencyProperty.Register(
        nameof(ResponsiveColumns),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(true, OnColumnsMetadataChanged));

    public static readonly DependencyProperty IsHeaderHorizontalProperty = DependencyProperty.Register(
        nameof(IsHeaderHorizontal),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(true));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing),
        typeof(double),
        typeof(UniformPagedGrid),
        new PropertyMetadata(0.0, OnRowSpacingChanged));

    public static readonly DependencyProperty EnforceColumnLimitProperty = DependencyProperty.Register(
        nameof(EnforceColumnLimit),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(false, OnColumnsMetadataChanged));

    public static readonly DependencyProperty CurrentPageProperty = DependencyProperty.Register(
        nameof(CurrentPage),
        typeof(int),
        typeof(UniformPagedGrid),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCurrentPageChanged, CoerceCurrentPage));

    public static readonly DependencyProperty TotalPagesProperty = DependencyProperty.Register(
        nameof(TotalPages),
        typeof(int),
        typeof(UniformPagedGrid),
        new PropertyMetadata(1));

    public static readonly DependencyProperty TotalItemsProperty = DependencyProperty.Register(
        nameof(TotalItems),
        typeof(int),
        typeof(UniformPagedGrid),
        new PropertyMetadata(0));

    public static readonly DependencyProperty VisibleColumnCountProperty = DependencyProperty.Register(
        nameof(VisibleColumnCount),
        typeof(int),
        typeof(UniformPagedGrid),
        new PropertyMetadata(0));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(UniformPagedGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty RowDoubleClickCommandProperty = DependencyProperty.Register(
        nameof(RowDoubleClickCommand),
        typeof(ICommand),
        typeof(UniformPagedGrid),
        new PropertyMetadata(null));

    public static readonly DependencyProperty RowDoubleClickCommandParameterProperty = DependencyProperty.Register(
        nameof(RowDoubleClickCommandParameter),
        typeof(object),
        typeof(UniformPagedGrid),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PagedItemsProperty = DependencyProperty.Register(
        nameof(PagedItems),
        typeof(ObservableCollection<object>),
        typeof(UniformPagedGrid),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PaginationSummaryProperty = DependencyProperty.Register(
        nameof(PaginationSummary),
        typeof(string),
        typeof(UniformPagedGrid),
        new PropertyMetadata("Page 1 of 1 (0 total)"));

    public static readonly DependencyProperty CanGoPrevProperty = DependencyProperty.Register(
        nameof(CanGoPrev),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(false));

    public static readonly DependencyProperty CanGoNextProperty = DependencyProperty.Register(
        nameof(CanGoNext),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
        nameof(IsEmpty),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(true));

    public static readonly DependencyProperty HasHeaderTextProperty = DependencyProperty.Register(
        nameof(HasHeaderText),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(false));

    public event EventHandler<object?>? RowDoubleClicked;

    public UniformPagedGrid()
    {
        InitializeComponent();

        PagedItems = new ObservableCollection<object>();
        var columns = new ObservableCollection<UniformColumnDef>();
        columns.CollectionChanged += Columns_CollectionChanged;
        SetCurrentValue(ColumnsProperty, columns);

        Loaded += UniformPagedGrid_Loaded;
        Unloaded += UniformPagedGrid_Unloaded;
        SizeChanged += UniformPagedGrid_SizeChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RecalculateAutoPageSize();
                ScheduleResponsiveColumnLayout(force: true);
                RefreshAutoColumnWidths();
            }
        };
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ObservableCollection<UniformColumnDef> Columns
    {
        get => (ObservableCollection<UniformColumnDef>)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string EmptyMessage
    {
        get => (string)GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    public int PageSize
    {
        get => (int)GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public bool AutoPageSize
    {
        get => (bool)GetValue(AutoPageSizeProperty);
        set => SetValue(AutoPageSizeProperty, value);
    }

    public bool AllowAutoRowHeight
    {
        get => (bool)GetValue(AllowAutoRowHeightProperty);
        set => SetValue(AllowAutoRowHeightProperty, value);
    }

    public bool FitColumnsToContent
    {
        get => (bool)GetValue(FitColumnsToContentProperty);
        set => SetValue(FitColumnsToContentProperty, value);
    }

    public bool ResponsiveColumns
    {
        get => (bool)GetValue(ResponsiveColumnsProperty);
        set => SetValue(ResponsiveColumnsProperty, value);
    }

    public bool IsHeaderHorizontal
    {
        get => (bool)GetValue(IsHeaderHorizontalProperty);
        private set => SetValue(IsHeaderHorizontalProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public bool EnforceColumnLimit
    {
        get => (bool)GetValue(EnforceColumnLimitProperty);
        set => SetValue(EnforceColumnLimitProperty, value);
    }

    public int CurrentPage
    {
        get => (int)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public int TotalPages
    {
        get => (int)GetValue(TotalPagesProperty);
        private set => SetValue(TotalPagesProperty, value);
    }

    public int TotalItems
    {
        get => (int)GetValue(TotalItemsProperty);
        private set => SetValue(TotalItemsProperty, value);
    }

    public int VisibleColumnCount
    {
        get => (int)GetValue(VisibleColumnCountProperty);
        private set => SetValue(VisibleColumnCountProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ICommand? RowDoubleClickCommand
    {
        get => (ICommand?)GetValue(RowDoubleClickCommandProperty);
        set => SetValue(RowDoubleClickCommandProperty, value);
    }

    public object? RowDoubleClickCommandParameter
    {
        get => GetValue(RowDoubleClickCommandParameterProperty);
        set => SetValue(RowDoubleClickCommandParameterProperty, value);
    }

    public ObservableCollection<object> PagedItems
    {
        get => (ObservableCollection<object>)GetValue(PagedItemsProperty);
        private set => SetValue(PagedItemsProperty, value);
    }

    public string PaginationSummary
    {
        get => (string)GetValue(PaginationSummaryProperty);
        private set => SetValue(PaginationSummaryProperty, value);
    }

    public bool CanGoPrev
    {
        get => (bool)GetValue(CanGoPrevProperty);
        private set => SetValue(CanGoPrevProperty, value);
    }

    public bool CanGoNext
    {
        get => (bool)GetValue(CanGoNextProperty);
        private set => SetValue(CanGoNextProperty, value);
    }

    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        private set => SetValue(IsEmptyProperty, value);
    }

    public bool HasHeaderText
    {
        get => (bool)GetValue(HasHeaderTextProperty);
        private set => SetValue(HasHeaderTextProperty, value);
    }

    public void RecalculateAutoPageSize()
    {
        if (!AutoPageSize || _hasExplicitPageSize)
        {
            return;
        }

        double referenceHeight = 0;
        if (_hostWindow is { ActualHeight: > 0 })
        {
            referenceHeight = _hostWindow.ActualHeight;
        }
        else if (_hostWindow is { Height: > 0 } && !double.IsNaN(_hostWindow.Height))
        {
            referenceHeight = _hostWindow.Height;
        }
        else if (ActualHeight > 0)
        {
            referenceHeight = ActualHeight;
        }
        else
        {
            referenceHeight = SystemParameters.PrimaryScreenHeight;
        }

        int targetSize = referenceHeight > ResolutionHeightThreshold ? PageSize1440p : PageSize1080p;
        if ((AllowAutoRowHeight || RowSpacing > 0) && ActualHeight > 0)
        {
            double headerOverhead = HasHeaderText ? (FitColumnsToContent ? 56.0 : 37.0) : 0.0;
            double overhead = headerOverhead + 36.0 + 44.0 + 6.0;
            double available = ActualHeight - overhead;
            double estimatedRowHeight = (AllowAutoRowHeight ? AutoRowEstimatedHeight : 36.0) + Math.Max(0.0, RowSpacing) + (RowSpacing > 0 ? 4.0 : 0.0);
            if (available > estimatedRowHeight)
            {
                int maxFit = Math.Max(4, (int)Math.Floor(available / estimatedRowHeight));
                targetSize = Math.Min(targetSize, maxFit);
            }
        }

        if (PageSize != targetSize)
        {
            _isApplyingAutoPageSize = true;
            try
            {
                SetCurrentValue(PageSizeProperty, targetSize);
            }
            finally
            {
                _isApplyingAutoPageSize = false;
            }
        }
    }

    /// <summary>
    /// Recomputes priority-based column visibility and non-clipping single-line widths for the current panel width.
    /// </summary>
    public void RefreshResponsiveColumnLayout()
    {
        ApplyResponsiveColumnLayout(force: true);
    }

    private void UniformPagedGrid_Loaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow();
        ApplyRowHeightMode();
        RecalculateAutoPageSize();
        RebuildColumns();
        RefreshPagination();
        ScheduleResponsiveColumnLayout(force: true);
    }

    private void UniformPagedGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachHostWindow();
    }

    private void UniformPagedGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalculateAutoPageSize();
        if (e.WidthChanged)
        {
            ApplyResponsiveColumnLayout(force: false);
        }
    }

    private void InternalDataGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            ApplyResponsiveColumnLayout(force: false);
        }
    }

    private void HostWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalculateAutoPageSize();
        if (e.WidthChanged)
        {
            ScheduleResponsiveColumnLayout(force: false);
        }
    }

    private void AttachHostWindow()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _hostWindow))
        {
            return;
        }

        DetachHostWindow();
        _hostWindow = window;
        if (_hostWindow != null)
        {
            _hostWindow.SizeChanged += HostWindow_SizeChanged;
        }
    }

    private void DetachHostWindow()
    {
        if (_hostWindow != null)
        {
            _hostWindow.SizeChanged -= HostWindow_SizeChanged;
            _hostWindow = null;
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        grid.HookCollectionChanged(e.NewValue as INotifyCollectionChanged);
        grid.ReloadSourceItems();
    }

    private static void OnColumnsCollectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        if (e.OldValue is ObservableCollection<UniformColumnDef> oldCols)
        {
            oldCols.CollectionChanged -= grid.Columns_CollectionChanged;
        }
        if (e.NewValue is ObservableCollection<UniformColumnDef> newCols)
        {
            newCols.CollectionChanged += grid.Columns_CollectionChanged;
        }

        grid.RebuildColumns();
    }

    private static void OnColumnsMetadataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UniformPagedGrid)d).RebuildColumns();
    }

    private static void OnHeaderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        grid.HasHeaderText = !string.IsNullOrWhiteSpace(grid.Title) || !string.IsNullOrWhiteSpace(grid.Subtitle);
    }

    private static object CoercePageSize(DependencyObject d, object baseValue)
    {
        return baseValue is int size ? Math.Max(1, size) : PageSize1080p;
    }

    private static void OnPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        if (!grid._isApplyingAutoPageSize)
        {
            grid._hasExplicitPageSize = true;
        }

        grid.RefreshPagination();
    }

    private static void OnAutoPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        if (grid.AutoPageSize && !grid._hasExplicitPageSize)
        {
            grid.RecalculateAutoPageSize();
        }
    }

    private static void OnAllowAutoRowHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        grid.ApplyRowHeightMode();
        grid.RecalculateAutoPageSize();
    }

    private static void OnFitColumnsToContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        grid.IsHeaderHorizontal = !grid.FitColumnsToContent;
        grid.RebuildColumns();
        grid.RecalculateAutoPageSize();
        grid.RefreshAutoColumnWidths();
    }

    private static void OnRowSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (UniformPagedGrid)d;
        grid.ApplyRowHeightMode();
        grid.RecalculateAutoPageSize();
    }

    private void ApplyRowHeightMode()
    {
        if (InternalDataGrid == null)
        {
            return;
        }

        if (RowSpacing > 0)
        {
            InternalDataGrid.GridLinesVisibility = DataGridGridLinesVisibility.None;
            InternalDataGrid.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            InternalDataGrid.RowHeight = AllowAutoRowHeight ? double.NaN : 36;
            InternalDataGrid.MinRowHeight = AllowAutoRowHeight ? 38 : 0;

            var baseKey = AllowAutoRowHeight ? "UniformSpacedAutoHeightDataGridRowStyle" : "UniformDataGridRowStyle";
            if (Resources[baseKey] is Style baseStyle)
            {
                var spaced = new Style(typeof(DataGridRow), baseStyle);
                double halfGap = Math.Max(1.0, RowSpacing / 2.0);
                spaced.Setters.Add(new Setter(MarginProperty, new Thickness(0, halfGap, 0, halfGap)));
                InternalDataGrid.RowStyle = spaced;
            }
            return;
        }

        InternalDataGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        InternalDataGrid.Background = Brushes.White;

        if (AllowAutoRowHeight)
        {
            InternalDataGrid.RowHeight = double.NaN;
            InternalDataGrid.MinRowHeight = 36;
            if (Resources["UniformAutoHeightDataGridRowStyle"] is Style autoStyle)
            {
                InternalDataGrid.RowStyle = autoStyle;
            }
        }
        else
        {
            InternalDataGrid.RowHeight = 36;
            InternalDataGrid.MinRowHeight = 0;
            if (Resources["UniformDataGridRowStyle"] is Style fixedStyle)
            {
                InternalDataGrid.RowStyle = fixedStyle;
            }
        }
    }

    private static object CoerceCurrentPage(DependencyObject d, object baseValue)
    {
        var grid = (UniformPagedGrid)d;
        int requested = baseValue is int p ? p : 1;
        int maxPage = Math.Max(1, grid.TotalPages);
        return Math.Clamp(requested, 1, maxPage);
    }

    private static void OnCurrentPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UniformPagedGrid)d).UpdatePagedSlice();
    }

    private void Columns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildColumns();
    }

    private void HookCollectionChanged(INotifyCollectionChanged? newCollection)
    {
        if (_subscribedCollection != null)
        {
            _subscribedCollection.CollectionChanged -= SourceCollection_CollectionChanged;
        }

        _subscribedCollection = newCollection;
        if (_subscribedCollection != null)
        {
            _subscribedCollection.CollectionChanged += SourceCollection_CollectionChanged;
        }
    }

    private void SourceCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ReloadSourceItems();
    }

    private void ReloadSourceItems()
    {
        _allItems.Clear();
        if (ItemsSource != null)
        {
            foreach (var item in ItemsSource)
            {
                if (item != null)
                {
                    _allItems.Add(item);
                }
            }
        }

        RefreshPagination();
        ScheduleResponsiveColumnLayout(force: false);
    }

    private void RefreshPagination()
    {
        int total = _allItems.Count;
        int pageSize = Math.Max(1, PageSize);
        int totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);

        TotalItems = total;
        TotalPages = totalPages;
        IsEmpty = total == 0;

        int clampedPage = Math.Clamp(CurrentPage, 1, totalPages);
        if (clampedPage != CurrentPage)
        {
            SetCurrentValue(CurrentPageProperty, clampedPage);
        }

        UpdatePagedSlice();
    }

    private void UpdatePagedSlice()
    {
        int pageSize = Math.Max(1, PageSize);
        int currentPage = Math.Clamp(CurrentPage, 1, Math.Max(1, TotalPages));

        PagedItems.Clear();
        foreach (var item in _allItems.Skip((currentPage - 1) * pageSize).Take(pageSize))
        {
            PagedItems.Add(item);
        }

        CanGoPrev = currentPage > 1;
        CanGoNext = currentPage < TotalPages;
        PaginationSummary = $"Page {currentPage} of {TotalPages} ({TotalItems} total)";
        RefreshAutoColumnWidths();

        if (_allItems.Count > FullDatasetMeasureLimit)
        {
            ScheduleResponsiveColumnLayout(force: false);
        }
    }

    private void RefreshAutoColumnWidths()
    {
        if (!FitColumnsToContent || InternalDataGrid == null || InternalDataGrid.Columns.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!FitColumnsToContent || InternalDataGrid == null)
            {
                return;
            }

            foreach (var col in InternalDataGrid.Columns)
            {
                col.Width = 0;
                col.Width = DataGridLength.Auto;
            }

            InternalDataGrid.UpdateLayout();
        }));
    }

    private void RebuildColumns()
    {
        if (InternalDataGrid == null)
        {
            return;
        }

        InternalDataGrid.HorizontalAlignment = FitColumnsToContent
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Stretch;
        InternalDataGrid.Columns.Clear();
        _builtColumns.Clear();
        _lastResponsiveSignature = string.Empty;

        if (Columns == null || Columns.Count == 0)
        {
            VisibleColumnCount = 0;
            return;
        }

        IEnumerable<UniformColumnDef> activeColumns = EnforceColumnLimit
            ? Columns.Take(LegacyMaxUniformColumns)
            : Columns;

        foreach (var col in activeColumns)
        {
            Style? headerStyle = null;
            if (col.HorizontalAlignment != HorizontalAlignment.Left || FitColumnsToContent)
            {
                var baseHeaderStyle = Resources["UniformColumnHeaderStyle"] as Style;
                headerStyle = baseHeaderStyle != null
                    ? new Style(typeof(DataGridColumnHeader), baseHeaderStyle)
                    : new Style(typeof(DataGridColumnHeader));
                if (col.HorizontalAlignment != HorizontalAlignment.Left)
                {
                    headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, col.HorizontalAlignment));
                }
                if (FitColumnsToContent)
                {
                    headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 0, 22, 0)));
                }
            }

            var columnWidth = FitColumnsToContent
                ? DataGridLength.Auto
                : new DataGridLength(Math.Max(0.2, col.StarWidth), DataGridLengthUnitType.Star);

            if (col.CellTemplate != null)
            {
                var tplCol = new DataGridTemplateColumn
                {
                    Header = col.Header,
                    CellTemplate = col.CellTemplate,
                    Width = columnWidth,
                    MinWidth = Math.Max(40, col.MinWidth)
                };
                if (headerStyle != null)
                {
                    tplCol.HeaderStyle = headerStyle;
                }
                InternalDataGrid.Columns.Add(tplCol);
                _builtColumns.Add((col, tplCol));
                continue;
            }

            var binding = new Binding(col.BindingPath);
            if (!string.IsNullOrWhiteSpace(col.StringFormat))
            {
                binding.StringFormat = col.StringFormat;
            }

            var elementStyle = new Style(typeof(TextBlock));
            elementStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.None));
            elementStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
            elementStyle.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
            elementStyle.Setters.Add(new Setter(HorizontalAlignmentProperty, col.HorizontalAlignment));
            elementStyle.Setters.Add(new Setter(MarginProperty, FitColumnsToContent ? new Thickness(14, 0, 22, 0) : new Thickness(10, 0, 10, 0)));
            elementStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
            elementStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, PrimaryTextBrush));
            elementStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, col.IsMonospace ? MonoFontFamily : PrimaryFontFamily));

            if (col.IsBold)
            {
                elementStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            }

            var dgCol = new DataGridTextColumn
            {
                Header = col.Header,
                Binding = binding,
                Width = columnWidth,
                MinWidth = Math.Max(40, col.MinWidth),
                ElementStyle = elementStyle
            };
            if (headerStyle != null)
            {
                dgCol.HeaderStyle = headerStyle;
            }

            InternalDataGrid.Columns.Add(dgCol);
            _builtColumns.Add((col, dgCol));
        }

        ApplyResponsiveColumnLayout(force: true);
        RefreshAutoColumnWidths();
    }

    private void ScheduleResponsiveColumnLayout(bool force)
    {
        if (!ResponsiveColumns || FitColumnsToContent || _builtColumns.Count == 0)
        {
            return;
        }

        if (_responsiveLayoutQueued && !force)
        {
            return;
        }

        _responsiveLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _responsiveLayoutQueued = false;
            ApplyResponsiveColumnLayout(force);
        }));
    }

    private void ApplyResponsiveColumnLayout(bool force)
    {
        if (InternalDataGrid == null || _builtColumns.Count == 0)
        {
            VisibleColumnCount = 0;
            return;
        }

        if (!ResponsiveColumns || FitColumnsToContent)
        {
            foreach (var (def, col) in _builtColumns)
            {
                col.Visibility = Visibility.Visible;
                col.MinWidth = Math.Max(40, def.MinWidth);
                col.Width = FitColumnsToContent
                    ? DataGridLength.Auto
                    : new DataGridLength(Math.Max(0.2, def.StarWidth), DataGridLengthUnitType.Star);
            }

            VisibleColumnCount = _builtColumns.Count;
            return;
        }

        double rawWidth = InternalDataGrid.ActualWidth > 0
            ? InternalDataGrid.ActualWidth
            : (ActualWidth > 0 ? ActualWidth - 2.0 : 0.0);
        double availableWidth = rawWidth > 20.0
            ? Math.Max(100.0, rawWidth - ViewportSafetyGutter)
            : 0.0;

        double pixelsPerDip = ResolvePixelsPerDip();
        var sampleItems = _allItems.Count <= FullDatasetMeasureLimit
            ? (IReadOnlyList<object>)_allItems
            : PagedItems;

        var specs = new ResponsiveColumnSpec[_builtColumns.Count];
        for (int i = 0; i < _builtColumns.Count; i++)
        {
            var def = _builtColumns[i].Def;
            double requiredWidth = MeasureColumnRequiredWidth(def, sampleItems, pixelsPerDip);
            double floorWidth = Math.Max(40.0, def.MinWidth);
            specs[i] = new ResponsiveColumnSpec(
                Index: i,
                Priority: Math.Max(1, def.Priority),
                RequiredWidth: requiredWidth,
                FloorWidth: floorWidth,
                Weight: Math.Max(0.15, def.StarWidth),
                IsOptional: def.Priority > 1);
        }

        var plan = ResponsiveTableColumnPlanner.ComputeLayout(specs, availableWidth);
        string signature = BuildLayoutSignature(availableWidth, plan);
        if (!force && string.Equals(signature, _lastResponsiveSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastResponsiveSignature = signature;

        int visibleCount = 0;
        for (int i = 0; i < plan.Count; i++)
        {
            var alloc = plan[i];
            var dgCol = _builtColumns[i].Column;

            if (!alloc.IsVisible)
            {
                dgCol.MinWidth = 0;
                dgCol.Visibility = Visibility.Collapsed;
                continue;
            }

            visibleCount++;
            double safeMin = Math.Floor(Math.Max(24.0, Math.Min(alloc.EffectiveMinWidth, alloc.AllocatedWidth)));
            dgCol.Visibility = Visibility.Visible;
            dgCol.MinWidth = safeMin;
            dgCol.Width = new DataGridLength(Math.Max(1.0, alloc.AllocatedWidth), DataGridLengthUnitType.Star);
        }

        VisibleColumnCount = visibleCount;
    }

    private double MeasureColumnRequiredWidth(
        UniformColumnDef def,
        IReadOnlyList<object> sampleItems,
        double pixelsPerDip)
    {
        double required = Math.Max(40.0, def.MinWidth);

        if (!string.IsNullOrWhiteSpace(def.Header))
        {
            double headerWidth = MeasureTextWidth(def.Header, HeaderTypeface, 11.5, pixelsPerDip) + HeaderHorizontalChrome;
            if (headerWidth > required)
            {
                required = headerWidth;
            }
        }

        string measurePath = !string.IsNullOrWhiteSpace(def.MeasureBindingPath)
            ? def.MeasureBindingPath!
            : def.BindingPath;

        if (string.IsNullOrWhiteSpace(measurePath) || sampleItems.Count == 0)
        {
            return Math.Ceiling(required);
        }

        Typeface cellTypeface = ResolveCellTypeface(def);
        double chrome = def.CellTemplate != null
            ? Math.Max(StandardCellHorizontalChrome, def.MeasureExtraWidth)
            : StandardCellHorizontalChrome;

        double maxTextWidth = 0.0;
        for (int i = 0; i < sampleItems.Count; i++)
        {
            string? text = ExtractFormattedValue(sampleItems[i], measurePath, def.StringFormat);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            double w = MeasureTextWidth(text, cellTypeface, 12.0, pixelsPerDip);
            if (w > maxTextWidth)
            {
                maxTextWidth = w;
            }
        }

        if (maxTextWidth > 0.0)
        {
            double cellRequired = maxTextWidth + chrome;
            if (cellRequired > required)
            {
                required = cellRequired;
            }
        }

        return Math.Ceiling(required);
    }

    private double ResolvePixelsPerDip()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return dpi.PixelsPerDip > 0 ? dpi.PixelsPerDip : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static Typeface ResolveCellTypeface(UniformColumnDef def) => (def.IsMonospace, def.IsBold) switch
    {
        (true, true) => MonoBoldTypeface,
        (true, false) => MonoRegularTypeface,
        (false, true) => PrimaryBoldTypeface,
        (false, false) => PrimaryRegularTypeface
    };

    private static double MeasureTextWidth(string text, Typeface typeface, double fontSize, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static string? ExtractFormattedValue(object item, string propertyPath, string? stringFormat)
    {
        var props = PropertyPathCache.GetOrAdd((item.GetType(), propertyPath), static key =>
        {
            var segments = key.Path.Split('.');
            var chain = new PropertyInfo?[segments.Length];
            var currentType = key.Type;
            for (int i = 0; i < segments.Length; i++)
            {
                var prop = currentType.GetProperty(segments[i], BindingFlags.Public | BindingFlags.Instance);
                chain[i] = prop;
                if (prop == null)
                {
                    break;
                }

                currentType = prop.PropertyType;
            }

            return chain;
        });

        object? current = item;
        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            if (prop == null || current == null)
            {
                return null;
            }

            current = prop.GetValue(current);
        }

        if (current == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(stringFormat))
        {
            try
            {
                string fmt = stringFormat.Contains("{0", StringComparison.Ordinal)
                    ? stringFormat
                    : $"{{0:{stringFormat}}}";
                return string.Format(CultureInfo.CurrentCulture, fmt, current);
            }
            catch
            {
                return current.ToString();
            }
        }

        return current.ToString();
    }

    private static string BuildLayoutSignature(double availableWidth, IReadOnlyList<ResponsiveColumnAllocation> plan)
    {
        var parts = new string[plan.Count + 1];
        parts[0] = ((int)Math.Round(availableWidth)).ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < plan.Count; i++)
        {
            var p = plan[i];
            parts[i + 1] = p.IsVisible
                ? $"{i}:{(int)Math.Round(p.AllocatedWidth)}"
                : $"{i}:H";
        }

        return string.Join('|', parts);
    }

    private void InternalDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var item = e.Row.Item;
        if (item == null)
        {
            e.Row.ToolTip = null;
            return;
        }

        var tipProp = ToolTipPropertyCache.GetOrAdd(
            item.GetType(),
            static t => t.GetProperty("ToolTipText", BindingFlags.Public | BindingFlags.Instance));

        if (tipProp?.GetValue(item) is string explicitTip && !string.IsNullOrWhiteSpace(explicitTip))
        {
            e.Row.ToolTip = explicitTip;
            return;
        }

        // Synthesize a clean multi-line hover tooltip from all configured columns so hidden or long details stay accessible.
        if (_builtColumns.Count > 0)
        {
            var lines = new List<string>(_builtColumns.Count);
            foreach (var (def, _) in _builtColumns)
            {
                string path = !string.IsNullOrWhiteSpace(def.MeasureBindingPath)
                    ? def.MeasureBindingPath!
                    : def.BindingPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var val = ExtractFormattedValue(item, path, def.StringFormat);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    lines.Add($"{def.Header}: {val}");
                }
            }

            e.Row.ToolTip = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : null;
        }
    }

    private void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPage > 1)
        {
            SetCurrentValue(CurrentPageProperty, CurrentPage - 1);
        }
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPage < TotalPages)
        {
            SetCurrentValue(CurrentPageProperty, CurrentPage + 1);
        }
    }

    private void InternalDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item == null)
        {
            return;
        }

        SetCurrentValue(SelectedItemProperty, row.Item);
        RowDoubleClicked?.Invoke(this, row.Item);

        var parameter = RowDoubleClickCommandParameter ?? row.Item;
        if (RowDoubleClickCommand != null && RowDoubleClickCommand.CanExecute(parameter))
        {
            RowDoubleClickCommand.Execute(parameter);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
