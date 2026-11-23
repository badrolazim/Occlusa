using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// ViewModel for DailyReconciliationModal delivering 1-click End-of-Day sales closing,
/// paged immutable Audit Trail inspection, and database backup verification.
/// </summary>
public partial class DailyReconciliationViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private readonly string _branchId;

    [ObservableProperty]
    private DailySalesReconciliationDto _summary;

    [ObservableProperty]
    private string _balancedBadgeText = "Pending EOD Check";

    [ObservableProperty]
    private string _backupStatusText = "Backup verification not run yet in this session.";

    [ObservableProperty]
    private string _backupChecksumText = "-";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public DailyReconciliationViewModel(
        string branchId,
        DailySalesReconciliationDto? initialSummary,
        OcclusaApiClient apiClient)
    {
        _branchId = branchId;
        _apiClient = apiClient;
        _summary = initialSummary ?? new DailySalesReconciliationDto { BranchId = branchId };
        AuditRows = new ObservableCollection<AuditLogRowItem>();
        UpdateBalancedBadge();
    }

    public ObservableCollection<AuditLogRowItem> AuditRows { get; }

    public string ModalTitle => $"End-of-Day Reconciliation — {_branchId}";
    public string ModalSubtitle => $"Closing Date: {Summary.Date:dd MMM yyyy} · Zero Hard Deletes Enforced";

    public async Task LoadAsync()
    {
        await RunReconciliationAsync();
        await LoadAuditLogsAsync();
    }

    [RelayCommand]
    private async Task RunReconciliationAsync()
    {
        try
        {
            var result = await _apiClient.ReconcileDailySalesAsync(new ReconcileDailySalesRequest
            {
                BranchId = _branchId,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                ReconciledBy = "Desktop Branch Manager"
            });

            Summary = result;
            UpdateBalancedBadge();
            StatusMessage = $"Reconciled {result.TotalInvoicesCount} active invoices at {DateTime.Now:HH:mm:ss}.";
            await LoadAuditLogsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reconciliation failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task VerifyBackupAsync()
    {
        try
        {
            var verification = await _apiClient.VerifyBackupAsync(_branchId, "Desktop Branch Manager");
            BackupStatusText = verification.RestoreTestPassed
                ? $"PASS · {verification.FileName} ({verification.SizeBytes:N0} bytes, Encrypted: {verification.IsEncrypted})"
                : $"FAILED · {verification.Notes}";
            BackupChecksumText = verification.Sha256Checksum;
            StatusMessage = $"Daily backup verification completed ({verification.VerifiedAt.ToLocalTime():HH:mm:ss}).";
            await LoadAuditLogsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup verification failed: {ex.Message}";
        }
    }

    private async Task LoadAuditLogsAsync()
    {
        try
        {
            var logs = await _apiClient.GetAuditLogsAsync(_branchId);
            AuditRows.Clear();
            foreach (var log in logs.OrderByDescending(l => l.Timestamp))
            {
                AuditRows.Add(new AuditLogRowItem(log));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load audit logs: {ex.Message}";
        }
    }

    private void UpdateBalancedBadge()
    {
        BalancedBadgeText = Summary.IsBalanced ? "BALANCED ✓" : "CHECK DISCREPANCY";
    }
}
