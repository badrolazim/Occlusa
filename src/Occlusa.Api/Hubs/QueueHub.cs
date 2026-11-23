using Microsoft.AspNetCore.SignalR;

namespace Occlusa.Api.Hubs;

/// <summary>
/// SignalR hub broadcasting real-time queue, billing, and WhatsApp AI receptionist updates
/// to connected Occlusa Desktop clients.
/// </summary>
public sealed class QueueHub : Hub
{
    public const string QueueUpdatedEvent = "QueueUpdated";
    public const string InvoiceUpdatedEvent = "InvoiceUpdated";
    public const string WhatsAppUpdatedEvent = "WhatsAppUpdated";
    public const string EmergencyEscalatedEvent = "EmergencyEscalated";

    /// <summary>
    /// Allows clients to optionally subscribe to a branch group in addition to receiving global updates.
    /// </summary>
    public async Task JoinBranch(string branchId)
    {
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, branchId.Trim().ToUpperInvariant());
        }
    }

    public async Task LeaveBranch(string branchId)
    {
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, branchId.Trim().ToUpperInvariant());
        }
    }
}
