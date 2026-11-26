using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// ViewModel for AppointmentReminderModal supporting 1-click bilingual WhatsApp reminder dispatch
/// and manual outreach tracking (Phone Call, SMS, Confirmed, No Reply).
/// </summary>
public partial class AppointmentReminderViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private readonly string _activeStaffName;
    private readonly Action<UpdateAppointmentReminderResponse>? _onUpdated;
    private AppointmentQueueItemDto _appointment;

    [ObservableProperty]
    private string _whatsAppMessageText = string.Empty;

    [ObservableProperty]
    private string _currentReminderSummary = string.Empty;

    [ObservableProperty]
    private string _currentReminderBadge = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Send a WhatsApp reminder or record a phone/SMS confirmation.";

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _isBusy;

    public AppointmentReminderViewModel(
        AppointmentQueueItemDto appointment,
        string? patientPhone,
        OcclusaApiClient apiClient,
        string activeStaffName,
        Action<UpdateAppointmentReminderResponse>? onUpdated = null)
    {
        _appointment = appointment;
        _apiClient = apiClient;
        _activeStaffName = string.IsNullOrWhiteSpace(activeStaffName) ? "Desktop Staff" : activeStaffName.Trim();
        _onUpdated = onUpdated;

        PatientName = appointment.PatientName;
        PatientPhone = string.IsNullOrWhiteSpace(patientPhone) ? string.Empty : patientPhone.Trim();
        HasPatientPhone = !string.IsNullOrWhiteSpace(PatientPhone);
        PhoneDisplay = HasPatientPhone ? PatientPhone : "No phone number on patient profile";
        SlotDisplay = $"{appointment.ScheduledAt.ToLocalTime():ddd dd MMM yyyy, hh:mm tt} · {appointment.DoctorName}";
        ProcedureDisplay = appointment.ProcedureSummary;

        _whatsAppMessageText = AppointmentCalendarHelper.BuildBilingualReminderMessage(
            appointment,
            appointment.BranchId,
            DateTimeOffset.Now.Offset);

        RefreshCurrentStatusDisplay();
    }

    public event Action? RequestClose;

    public string PatientName { get; }
    public string PatientPhone { get; }
    public bool HasPatientPhone { get; }
    public string PhoneDisplay { get; }
    public string SlotDisplay { get; }
    public string ProcedureDisplay { get; }
    public string ModalTitle => $"Appointment Reminder — {PatientName}";
    public string ModalSubtitle => $"{SlotDisplay} ({ProcedureDisplay})";
    public bool CanAct => !IsBusy;
    public bool CanSendWhatsApp => HasPatientPhone && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(CanSendWhatsApp));
    }

    [RelayCommand]
    private async Task SendWhatsAppReminderAsync()
    {
        if (!HasPatientPhone)
        {
            IsStatusError = true;
            StatusMessage = "Add a patient phone number in Patient Profile before sending WhatsApp reminders.";
            return;
        }

        await ExecuteReminderRequestAsync(new UpdateAppointmentReminderRequest
        {
            Status = ReminderStatus.Reminded,
            Channel = ReminderChannel.WhatsApp,
            SendWhatsAppMessage = true,
            CustomMessageText = WhatsAppMessageText?.Trim(),
            UpdatedBy = _activeStaffName
        }, closeOnSuccess: true);
    }

    [RelayCommand]
    private Task MarkRemindedCallAsync() => ExecuteReminderRequestAsync(new UpdateAppointmentReminderRequest
    {
        Status = ReminderStatus.Reminded,
        Channel = ReminderChannel.PhoneCall,
        SendWhatsAppMessage = false,
        UpdatedBy = _activeStaffName
    }, closeOnSuccess: true);

    [RelayCommand]
    private Task MarkRemindedSmsAsync() => ExecuteReminderRequestAsync(new UpdateAppointmentReminderRequest
    {
        Status = ReminderStatus.Reminded,
        Channel = ReminderChannel.Sms,
        SendWhatsAppMessage = false,
        UpdatedBy = _activeStaffName
    }, closeOnSuccess: true);

    [RelayCommand]
    private Task MarkConfirmedAsync() => ExecuteReminderRequestAsync(new UpdateAppointmentReminderRequest
    {
        Status = ReminderStatus.Confirmed,
        Channel = _appointment.ReminderChannel == ReminderChannel.None ? ReminderChannel.PhoneCall : _appointment.ReminderChannel,
        SendWhatsAppMessage = false,
        UpdatedBy = _activeStaffName
    }, closeOnSuccess: true);

    [RelayCommand]
    private Task MarkNoReplyAsync() => ExecuteReminderRequestAsync(new UpdateAppointmentReminderRequest
    {
        Status = ReminderStatus.NoReply,
        Channel = _appointment.ReminderChannel == ReminderChannel.None ? ReminderChannel.PhoneCall : _appointment.ReminderChannel,
        SendWhatsAppMessage = false,
        UpdatedBy = _activeStaffName
    }, closeOnSuccess: true);

    [RelayCommand]
    private void ResetDefaultMessage()
    {
        WhatsAppMessageText = AppointmentCalendarHelper.BuildBilingualReminderMessage(
            _appointment,
            _appointment.BranchId,
            DateTimeOffset.Now.Offset);
        IsStatusError = false;
        StatusMessage = "Reset message to default bilingual EN/BM reminder template.";
    }

    private async Task ExecuteReminderRequestAsync(UpdateAppointmentReminderRequest request, bool closeOnSuccess)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsStatusError = false;
        StatusMessage = request.SendWhatsAppMessage ? "Sending WhatsApp reminder..." : "Updating reminder status...";

        try
        {
            var response = await _apiClient.UpdateAppointmentReminderAsync(_appointment.Id, request);
            _appointment = response.Appointment;
            RefreshCurrentStatusDisplay();
            StatusMessage = response.Message;
            _onUpdated?.Invoke(response);
            if (closeOnSuccess)
            {
                RequestClose?.Invoke();
            }
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Reminder update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshCurrentStatusDisplay()
    {
        CurrentReminderBadge = AppointmentCalendarHelper.FormatReminderBadgeLabel(_appointment);
        CurrentReminderSummary = AppointmentCalendarHelper.FormatReminderDetailSummary(_appointment);
    }
}
