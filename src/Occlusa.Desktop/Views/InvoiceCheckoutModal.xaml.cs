using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for itemized invoice settlement, split payments, and audited soft-voiding.
/// </summary>
public partial class InvoiceCheckoutModal : Window
{
    public InvoiceCheckoutModal(InvoiceCheckoutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
