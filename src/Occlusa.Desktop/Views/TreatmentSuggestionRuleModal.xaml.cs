using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal dialog configuring treatment-to-invoice companion suggestions for Clinic Default or a specific Doctor.
/// </summary>
public partial class TreatmentSuggestionRuleModal : Window
{
    private readonly TreatmentSuggestionRuleViewModel _viewModel;

    public TreatmentSuggestionRuleModal(TreatmentSuggestionRuleViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.CloseRequested += (_, _) => DialogShell.ForceCloseWithoutPrompt();
    }

    private void CandidateGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is SelectableCompanionItemRowVm row)
        {
            _viewModel.ToggleRow(row);
        }
    }
}

