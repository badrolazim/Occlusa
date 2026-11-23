using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for end-of-day sales reconciliation, audit trail paging, and backup verification.
/// </summary>
public partial class DailyReconciliationModal : Window
{
    private readonly DailyReconciliationViewModel _viewModel;

    public DailyReconciliationModal(DailyReconciliationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }
}
