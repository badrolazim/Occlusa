using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal dialog for creating or editing a Master Billing Catalog item and its SOAP note keywords.
/// </summary>
public partial class BillingCatalogItemModal : Window
{
    public BillingCatalogItemModal(BillingCatalogItemViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => DialogShell.ForceCloseWithoutPrompt();
    }
}

