using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// Per-staff modal for configuring Simple vs Detailed Live Queue view and personal workspace options.
/// </summary>
public partial class UserPreferencesModal : Window
{
    public UserPreferencesModal(UserPreferencesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestCloseApproved += () => DialogShell.ForceCloseWithoutPrompt();
    }
}

