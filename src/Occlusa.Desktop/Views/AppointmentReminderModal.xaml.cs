using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// Compact modal for sending bilingual WhatsApp reminders or recording manual Call/SMS confirmation.
/// </summary>
public partial class AppointmentReminderModal : Window
{
    public AppointmentReminderModal(AppointmentReminderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogShell.ForceCloseWithoutPrompt();
    }
}

