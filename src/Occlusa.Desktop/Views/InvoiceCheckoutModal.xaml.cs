using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for draft invoice creation, smart suggestions, walk-in OTC sales,
/// shared patient credit handling, split-payment settlement, and audited soft-voiding.
/// </summary>
public partial class InvoiceCheckoutModal : Window
{
    public InvoiceCheckoutModal(InvoiceCheckoutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => DialogShell.ForceCloseWithoutPrompt();
    }
}
