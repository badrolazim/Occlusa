using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// ViewModel for UserPreferencesModal letting each staff member pick Simple vs Detailed Live Queue view
/// and optionally fine-tune individual workspace display preferences.
/// </summary>
public partial class UserPreferencesViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private readonly IReadOnlyList<DentistDto> _dentists;
    private readonly Action<UserWorkspacePreferencesDto>? _onSaved;
    private readonly bool _isDentist;
    private bool _suppressDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimplePreset))]
    [NotifyPropertyChangedFor(nameof(IsDetailedPreset))]
    [NotifyPropertyChangedFor(nameof(ActivePresetBadge))]
    private LiveQueueViewMode _queueViewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMyQueueScopeDefault))]
    [NotifyPropertyChangedFor(nameof(IsAllClinicScopeDefault))]
    private LiveQueueScopeMode _defaultQueueScope;

    [ObservableProperty]
    private bool _hideFinishedQueueRows;

    [ObservableProperty]
    private bool _showWaitTimers;

    [ObservableProperty]
    private bool _showInlineReceptionNotes;

    [ObservableProperty]
    private bool _showMyKadQuickBar;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "Changes apply only to your staff profile and sync across clinic workstations.";

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _isSaving;

    public UserPreferencesViewModel(
        string staffName,
        UserWorkspacePreferencesDto currentPreferences,
        IReadOnlyList<DentistDto> dentists,
        OcclusaApiClient apiClient,
        Action<UserWorkspacePreferencesDto>? onSaved = null)
    {
        StaffDisplayName = string.IsNullOrWhiteSpace(staffName) ? "Siti (Reception)" : staffName.Trim();
        StaffKey = AppointmentCalendarHelper.NormalizeStaffKey(StaffDisplayName);
        _dentists = dentists;
        _apiClient = apiClient;
        _onSaved = onSaved;
        _isDentist = AppointmentCalendarHelper.IsDentistStaff(StaffDisplayName, dentists);

        _suppressDirty = true;
        _queueViewMode = currentPreferences.QueueViewMode;
        _defaultQueueScope = currentPreferences.DefaultQueueScope;
        _hideFinishedQueueRows = currentPreferences.HideFinishedQueueRows;
        _showWaitTimers = currentPreferences.ShowWaitTimers;
        _showInlineReceptionNotes = currentPreferences.ShowInlineReceptionNotes;
        _showMyKadQuickBar = currentPreferences.ShowMyKadQuickBar;
        _suppressDirty = false;
        HasUnsavedChanges = false;
    }

    public event Action? RequestCloseApproved;

    public string StaffKey { get; }
    public string StaffDisplayName { get; }
    public bool IsDentist => _isDentist;
    public string ModalTitle => $"My View Settings — {StaffDisplayName}";
    public string ModalSubtitle => IsDentist
        ? "Dentist profile · Defaults to Simple View scoped to My Queue"
        : "Front-desk / assistant profile · Defaults to Simple View across All Clinic";
    public string ActivePresetBadge => IsSimplePreset ? "Simple View" : "Detailed View";
    public bool IsSimplePreset => QueueViewMode == LiveQueueViewMode.Simple;
    public bool IsDetailedPreset => QueueViewMode == LiveQueueViewMode.Detailed;
    public bool IsMyQueueScopeDefault => DefaultQueueScope == LiveQueueScopeMode.MyQueue;
    public bool IsAllClinicScopeDefault => DefaultQueueScope == LiveQueueScopeMode.AllClinic;
    public string ScopeHintText => IsDentist
        ? "My Queue automatically filters Live Queue to patients assigned to you."
        : "All Clinic is recommended for reception and dental assistants.";

    partial void OnQueueViewModeChanged(LiveQueueViewMode value) => MarkDirty();
    partial void OnDefaultQueueScopeChanged(LiveQueueScopeMode value) => MarkDirty();
    partial void OnHideFinishedQueueRowsChanged(bool value) => MarkDirty();
    partial void OnShowWaitTimersChanged(bool value) => MarkDirty();
    partial void OnShowInlineReceptionNotesChanged(bool value) => MarkDirty();
    partial void OnShowMyKadQuickBarChanged(bool value) => MarkDirty();

    [RelayCommand]
    private void SelectPreset(string modeName)
    {
        if (!Enum.TryParse<LiveQueueViewMode>(modeName, out var mode))
        {
            return;
        }

        var temp = new UserWorkspacePreferencesDto();
        AppointmentCalendarHelper.ApplyViewModePreset(temp, mode, _isDentist);

        _suppressDirty = true;
        QueueViewMode = temp.QueueViewMode;
        DefaultQueueScope = temp.DefaultQueueScope;
        HideFinishedQueueRows = temp.HideFinishedQueueRows;
        ShowWaitTimers = temp.ShowWaitTimers;
        ShowInlineReceptionNotes = temp.ShowInlineReceptionNotes;
        ShowMyKadQuickBar = temp.ShowMyKadQuickBar;
        _suppressDirty = false;

        HasUnsavedChanges = true;
        IsStatusError = false;
        StatusMessage = mode == LiveQueueViewMode.Simple
            ? "Applied Simple View preset (calm 3-Column Kanban Board with compact cards and collapsible Scheduled/Completed drawers)."
            : "Applied Detailed Operations preset (3-Column Kanban Board enriched with live wait timers, arrival metadata, and inline reception notes).";
    }

    [RelayCommand]
    private void SelectDefaultScope(string scopeName)
    {
        if (Enum.TryParse<LiveQueueScopeMode>(scopeName, out var scope))
        {
            DefaultQueueScope = scope;
        }
    }

    [RelayCommand]
    private void ResetToRoleDefault()
    {
        var defaults = AppointmentCalendarHelper.BuildDefaultUserPreferences(StaffDisplayName, _dentists);
        _suppressDirty = true;
        QueueViewMode = defaults.QueueViewMode;
        DefaultQueueScope = defaults.DefaultQueueScope;
        HideFinishedQueueRows = defaults.HideFinishedQueueRows;
        ShowWaitTimers = defaults.ShowWaitTimers;
        ShowInlineReceptionNotes = defaults.ShowInlineReceptionNotes;
        ShowMyKadQuickBar = defaults.ShowMyKadQuickBar;
        _suppressDirty = false;

        HasUnsavedChanges = true;
        IsStatusError = false;
        StatusMessage = "Reset to role default (Simple View). Click Save Preferences to apply.";
    }

    [RelayCommand]
    public async Task SavePreferencesAsync()
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        IsStatusError = false;
        StatusMessage = "Saving your workspace preferences...";

        var request = new SaveUserWorkspacePreferencesRequest
        {
            StaffKey = StaffKey,
            StaffDisplayName = StaffDisplayName,
            QueueViewMode = QueueViewMode,
            DefaultQueueScope = DefaultQueueScope,
            HideFinishedQueueRows = HideFinishedQueueRows,
            ShowWaitTimers = ShowWaitTimers,
            ShowInlineReceptionNotes = ShowInlineReceptionNotes,
            ShowMyKadQuickBar = ShowMyKadQuickBar
        };

        try
        {
            var saved = await _apiClient.SaveUserWorkspacePreferencesAsync(request);
            HasUnsavedChanges = false;
            StatusMessage = $"Saved {saved.QueueViewMode} view preferences for {StaffDisplayName}.";
            _onSaved?.Invoke(saved);
            RequestCloseApproved?.Invoke();
        }
        catch
        {
            // Offline-resilient fallback: apply and cache locally even if API is currently restarting.
            var localFallback = new UserWorkspacePreferencesDto
            {
                StaffKey = StaffKey,
                StaffDisplayName = StaffDisplayName,
                QueueViewMode = QueueViewMode,
                DefaultQueueScope = DefaultQueueScope,
                HideFinishedQueueRows = HideFinishedQueueRows,
                ShowWaitTimers = ShowWaitTimers,
                ShowInlineReceptionNotes = ShowInlineReceptionNotes,
                ShowMyKadQuickBar = ShowMyKadQuickBar,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            HasUnsavedChanges = false;
            StatusMessage = $"Saved locally for {StaffDisplayName} (API sync pending).";
            _onSaved?.Invoke(localFallback);
            RequestCloseApproved?.Invoke();
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            HasUnsavedChanges = true;
        }
    }
}

