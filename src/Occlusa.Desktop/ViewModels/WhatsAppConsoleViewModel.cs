using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// ViewModel for WhatsAppConsoleModal featuring scrollbar-free paged chat transcripts,
/// preset patient message simulation (Routine / Complex Implant / Emergency), AI booking
/// approval, and Staff Emergency Takeover reply.
/// </summary>
public partial class WhatsAppConsoleViewModel : ObservableObject
{
    private const int MessagesPerPage = 3;

    private readonly OcclusaApiClient _apiClient;
    private readonly string _branchId;
    private readonly string _activeStaffName;
    private readonly Action _onUpdated;
    private readonly List<WhatsAppMessageDto> _allMessages = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModalTitle))]
    [NotifyPropertyChangedFor(nameof(EmergencyBadgeText))]
    [NotifyPropertyChangedFor(nameof(EmergencyReasonDisplay))]
    private WhatsAppThreadDto _thread;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private string _paginationSummary = "Page 1 of 1";

    [ObservableProperty]
    private bool _canGoOlder;

    [ObservableProperty]
    private bool _canGoNewer;

    [ObservableProperty]
    private string _simPatientPhone = "+6012-8899112";

    [ObservableProperty]
    private string _simPatientName = "Farah Nadia";

    [ObservableProperty]
    private string _simMessageText = string.Empty;

    [ObservableProperty]
    private string _takeoverReplyText = string.Empty;

    [ObservableProperty]
    private bool _hasPendingAiBooking;

    [ObservableProperty]
    private Guid? _pendingBookingId;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public WhatsAppConsoleViewModel(
        WhatsAppThreadDto? thread,
        string branchId,
        IReadOnlyList<AppointmentQueueItemDto> queueItems,
        OcclusaApiClient apiClient,
        Action onUpdated,
        string activeStaffName = "Desktop Reception")
    {
        _branchId = branchId;
        _apiClient = apiClient;
        _onUpdated = onUpdated;
        _activeStaffName = string.IsNullOrWhiteSpace(activeStaffName) ? "Desktop Reception" : activeStaffName.Trim();

        _thread = thread ?? new WhatsAppThreadDto
        {
            ThreadId = string.Empty,
            PatientName = "Farah Nadia",
            PatientPhone = "+6012-8899112"
        };

        PagedMessages = new ObservableCollection<WhatsAppMessageDto>();
        SimPatientName = string.IsNullOrWhiteSpace(_thread.PatientName) ? "Farah Nadia" : _thread.PatientName;
        SimPatientPhone = string.IsNullOrWhiteSpace(_thread.PatientPhone) ? "+6012-8899112" : _thread.PatientPhone;

        ReloadMessagesFromThread(queueItems);
    }

    public ObservableCollection<WhatsAppMessageDto> PagedMessages { get; }

    public string ModalTitle => string.IsNullOrWhiteSpace(Thread.PatientName)
        ? "WhatsApp AI Receptionist Console"
        : $"WhatsApp AI Console — {Thread.PatientName}";

    public string ModalSubtitle => $"Phone: {SimPatientPhone} · Guardrails: AiBookedPending + Emergency Halts Booking";

    public string EmergencyBadgeText => Thread.IsEmergencyEscalated
        ? "EMERGENCY ESCALATED"
        : "AI Receptionist Active";

    public string EmergencyReasonDisplay => Thread.IsEmergencyEscalated
        ? $"EMERGENCY TAKEOVER REQUIRED: {Thread.EmergencyReason}"
        : string.Empty;

    partial void OnTakeoverReplyTextChanged(string value)
    {
        HasUnsavedChanges = !string.IsNullOrWhiteSpace(value);
    }

    [RelayCommand]
    private void GoOlderPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            RefreshPagedMessages();
        }
    }

    [RelayCommand]
    private void GoNewerPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            RefreshPagedMessages();
        }
    }

    [RelayCommand]
    private void ApplyRoutinePreset()
    {
        SimMessageText = "Hi, I want to book a routine scaling and polishing appointment tomorrow morning at KL Sentral.";
    }

    [RelayCommand]
    private void ApplyComplexImplantPreset()
    {
        SimMessageText = "Hi, can I book a dental implant surgery and wisdom tooth surgery next Friday?";
    }

    [RelayCommand]
    private void ApplyEmergencyPreset()
    {
        SimMessageText = "Help! I have severe pain, facial swelling, and uncontrolled bleeding after my tooth extraction!";
    }

    [RelayCommand]
    private async Task SimulatePatientMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(SimMessageText))
        {
            StatusMessage = "Enter or select a patient message to simulate.";
            return;
        }

        try
        {
            var updated = await _apiClient.SimulateWhatsAppAsync(new WhatsAppSimulateRequest
            {
                ThreadId = string.IsNullOrWhiteSpace(Thread.ThreadId) ? null : Thread.ThreadId,
                PatientPhone = SimPatientPhone.Trim(),
                PatientName = SimPatientName.Trim(),
                MessageText = SimMessageText.Trim(),
                BranchId = _branchId
            });

            Thread = updated;
            SimMessageText = string.Empty;
            var latestQueue = await _apiClient.GetAppointmentsAsync(_branchId);
            ReloadMessagesFromThread(latestQueue);
            OnPropertyChanged(nameof(ModalTitle));
            OnPropertyChanged(nameof(EmergencyBadgeText));
            _onUpdated();

            StatusMessage = updated.IsEmergencyEscalated
                ? "EMERGENCY DETECTED: AI booking halted and staff takeover alerted!"
                : "Simulated WhatsApp turn processed (single concise AI bubble).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Simulation failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApproveAiBookingAsync()
    {
        if (!PendingBookingId.HasValue)
        {
            return;
        }

        try
        {
            var approved = await _apiClient.ApproveAiBookingAsync(PendingBookingId.Value, _activeStaffName);
            HasPendingAiBooking = false;
            PendingBookingId = null;
            _onUpdated();
            StatusMessage = $"Approved AI booking for {approved.PatientName} -> Status: {approved.Status}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Approval failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SendTakeoverReplyAsync()
    {
        if (string.IsNullOrWhiteSpace(Thread.ThreadId))
        {
            StatusMessage = "Simulate or open an active WhatsApp thread first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TakeoverReplyText))
        {
            StatusMessage = "Enter a staff takeover reply message.";
            return;
        }

        try
        {
            var updated = await _apiClient.SendWhatsAppTakeoverReplyAsync(
                Thread.ThreadId,
                new WhatsAppTakeoverReplyRequest
                {
                    MessageText = TakeoverReplyText.Trim(),
                    StaffName = _activeStaffName
                });

            Thread = updated;
            TakeoverReplyText = string.Empty;
            HasUnsavedChanges = false;
            var latestQueue = await _apiClient.GetAppointmentsAsync(_branchId);
            ReloadMessagesFromThread(latestQueue);
            OnPropertyChanged(nameof(EmergencyBadgeText));
            _onUpdated();
            StatusMessage = "Staff takeover reply posted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Takeover reply failed: {ex.Message}";
        }
    }

    private void ReloadMessagesFromThread(IReadOnlyList<AppointmentQueueItemDto> queueItems)
    {
        _allMessages.Clear();
        _allMessages.AddRange(Thread.Messages.OrderBy(m => m.Timestamp));

        var bookingIds = Thread.Messages
            .Where(m => m.CreatedBookingId.HasValue)
            .Select(m => m.CreatedBookingId!.Value)
            .ToList();

        var pending = queueItems.FirstOrDefault(q =>
            q.Status == QueueStatus.AiBookedPending
            && (bookingIds.Contains(q.Id)
                || string.Equals(q.PatientName, Thread.PatientName, StringComparison.OrdinalIgnoreCase)));

        PendingBookingId = pending?.Id;
        HasPendingAiBooking = pending != null;

        TotalPages = Math.Max(1, (int)Math.Ceiling(_allMessages.Count / (double)MessagesPerPage));
        CurrentPage = TotalPages; // Jump to newest page on load/message arrival
        RefreshPagedMessages();
    }

    private void RefreshPagedMessages()
    {
        TotalPages = Math.Max(1, (int)Math.Ceiling(_allMessages.Count / (double)MessagesPerPage));
        CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages);

        PagedMessages.Clear();
        foreach (var msg in _allMessages.Skip((CurrentPage - 1) * MessagesPerPage).Take(MessagesPerPage))
        {
            PagedMessages.Add(msg);
        }

        CanGoOlder = CurrentPage > 1;
        CanGoNewer = CurrentPage < TotalPages;
        PaginationSummary = $"Page {CurrentPage} of {TotalPages} ({_allMessages.Count} msgs)";
    }
}
