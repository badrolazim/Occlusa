using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for WhatsApp AI Receptionist paging, simulation presets, and emergency takeover.
/// </summary>
public partial class WhatsAppConsoleModal : Window
{
    public WhatsAppConsoleModal(WhatsAppConsoleViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
