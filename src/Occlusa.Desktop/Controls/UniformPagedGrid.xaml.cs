using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Declarative column definition for <see cref="UniformPagedGrid"/>.
/// Designed to enforce 3-4 concise columns with zero text truncation.
/// </summary>
public sealed class UniformColumnDef
{
    public UniformColumnDef()
    {
    }

    public UniformColumnDef(string header, string bindingPath, double starWidth = 1.0, string? stringFormat = null)
    {
        Header = header;
        BindingPath = bindingPath;
        StarWidth = starWidth;
        StringFormat = stringFormat;
    }

    public string Header { get; set; } = string.Empty;
    public string BindingPath { get; set; } = string.Empty;
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
}

/// <summary>
/// Uniform 3-4 column paginated grid with zero scrollbars, zero text truncation,
/// responsive 1080p/1440p page sizing, and double-click row activation.
/// </summary>
[ContentProperty(nameof(Columns))]
public partial class UniformPagedGrid : UserControl
{
    private const int MaxUniformColumns = 4;
    private const double ResolutionHeightThreshold = 1120.0;
    private const int PageSize1080p = 11;
    private const int PageSize1440p = 17;

    private readonly List<object> _allItems = new();
    private INotifyCollectionChanged? _subscribedCollection;
    private Window? _hostWindow;
    private bool _hasExplicitPageSize;
    private bool _isApplyingAutoPageSize;

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

    public static readonly DependencyProperty EnforceColumnLimitProperty = DependencyProperty.Register(
        nameof(EnforceColumnLimit),
        typeof(bool),
        typeof(UniformPagedGrid),
        new PropertyMetadata(true, OnColumnsMetadataChanged));

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

    private void UniformPagedGrid_Loaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow();
        RecalculateAutoPageSize();
        RebuildColumns();
        RefreshPagination();
    }

    private void UniformPagedGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachHostWindow();
    }

    private void UniformPagedGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalculateAutoPageSize();
    }

    private void HostWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalculateAutoPageSize();
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
    }

    private void RebuildColumns()
    {
        if (InternalDataGrid == null)
        {
            return;
        }

        InternalDataGrid.Columns.Clear();
        if (Columns == null || Columns.Count == 0)
        {
            return;
        }

        IEnumerable<UniformColumnDef> activeColumns = EnforceColumnLimit
            ? Columns.Take(MaxUniformColumns)
            : Columns;

        foreach (var col in activeColumns)
        {
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
            elementStyle.Setters.Add(new Setter(MarginProperty, new Thickness(10, 0, 10, 0)));
            elementStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
            elementStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(15, 23, 42))));

            if (col.IsMonospace)
            {
                elementStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas")));
            }
            else
            {
                elementStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI")));
            }

            if (col.IsBold)
            {
                elementStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            }

            var dgCol = new DataGridTextColumn
            {
                Header = col.Header,
                Binding = binding,
                Width = new DataGridLength(Math.Max(0.2, col.StarWidth), DataGridLengthUnitType.Star),
                MinWidth = Math.Max(40, col.MinWidth),
                ElementStyle = elementStyle
            };

            InternalDataGrid.Columns.Add(dgCol);
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
}
