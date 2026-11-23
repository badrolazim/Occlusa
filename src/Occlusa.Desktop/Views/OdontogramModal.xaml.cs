using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for interactive 32-tooth FDI odontogram charting and billing push.
/// </summary>
public partial class OdontogramModal : Window
{
    private readonly OdontogramViewModel _viewModel;

    public OdontogramModal(OdontogramViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.LoadOdontogramAsync();
    }
}
