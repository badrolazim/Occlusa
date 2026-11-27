using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Presentation ViewModel for one zero-scrollbar column on the Live Queue 3-Column Kanban Board
/// (Waiting, In Chair, or Ready for Payment) with dynamic per-column pagination.
/// </summary>
public sealed partial class LiveQueueKanbanColumnVm : ObservableObject
{
    private List<QueueRowItem> _allCards = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderWithCount))]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(PaginationSummary))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaginationSummary))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaginationSummary))]
    [NotifyPropertyChangedFor(nameof(HasMultiplePages))]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _cardsPerPage = 4;

    [ObservableProperty]
    private bool _canGoPrev;

    [ObservableProperty]
    private bool _canGoNext;

    public LiveQueueKanbanColumnVm(
        QueueStatus stage,
        string title,
        string emptyMessage,
        Brush accentBrush,
        Brush headerTintBrush,
        Brush badgeForegroundBrush)
    {
        Stage = stage;
        Title = title;
        EmptyMessage = emptyMessage;
        AccentBrush = accentBrush;
        HeaderTintBrush = headerTintBrush;
        BadgeForegroundBrush = badgeForegroundBrush;
        PagedCards = new ObservableCollection<QueueRowItem>();
    }

    public QueueStatus Stage { get; }
    public string Title { get; }
    public string EmptyMessage { get; }
    public Brush AccentBrush { get; }
    public Brush HeaderTintBrush { get; }
    public Brush BadgeForegroundBrush { get; }
    public ObservableCollection<QueueRowItem> PagedCards { get; }
    public IReadOnlyList<QueueRowItem> AllCards => _allCards;

    public string HeaderWithCount => $"{Title} ({TotalCount})";
    public bool HasItems => TotalCount > 0;
    public bool IsEmpty => TotalCount == 0;
    public bool HasMultiplePages => TotalPages > 1;
    public string PaginationSummary => TotalPages > 1
        ? $"Page {CurrentPage} / {TotalPages}"
        : (TotalCount == 1 ? "1 patient" : $"{TotalCount} patients");

    public void SetItems(IReadOnlyList<QueueRowItem> items, Guid? selectedId)
    {
        _allCards = items.ToList();
        TotalCount = _allCards.Count;
        SyncSelection(selectedId);
        RebuildPage();
    }

    public void SetCardsPerPage(int cardsPerPage)
    {
        int clamped = Math.Max(1, cardsPerPage);
        if (clamped == CardsPerPage)
        {
            return;
        }

        CardsPerPage = clamped;
        RebuildPage();
    }

    public void RefreshElapsed(DateTimeOffset now)
    {
        foreach (var card in _allCards)
        {
            if (!card.IsFinished)
            {
                card.RefreshElapsed(now);
            }
        }
    }

    public void SyncSelection(Guid? selectedId)
    {
        foreach (var card in _allCards)
        {
            card.IsSelected = selectedId.HasValue && card.Source.Id == selectedId.Value;
        }
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            RebuildPage();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            RebuildPage();
        }
    }

    private void RebuildPage()
    {
        var slice = AppointmentCalendarHelper.ComputeKanbanPage(_allCards, CurrentPage, CardsPerPage);
        CurrentPage = slice.CurrentPage;
        TotalPages = slice.TotalPages;
        CanGoPrev = CurrentPage > 1;
        CanGoNext = CurrentPage < TotalPages;

        PagedCards.Clear();
        foreach (var item in slice.Items)
        {
            PagedCards.Add(item);
        }
    }
}

