using Microsoft.AspNetCore.SignalR;
using Occlusa.Api.Hubs;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Services;

public interface IRealtimeNotifier
{
    Task NotifyQueueUpdatedAsync(AppointmentQueueItemDto item, CancellationToken ct = default);
    Task NotifyInvoiceUpdatedAsync(InvoiceDto invoice, CancellationToken ct = default);
    Task NotifyWhatsAppUpdatedAsync(WhatsAppThreadDto thread, CancellationToken ct = default);
    Task NotifyEmergencyEscalatedAsync(WhatsAppThreadDto thread, CancellationToken ct = default);
}

public sealed class QueueRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<QueueHub> _hubContext;

    public QueueRealtimeNotifier(IHubContext<QueueHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyQueueUpdatedAsync(AppointmentQueueItemDto item, CancellationToken ct = default) =>
        _hubContext.Clients.All.SendAsync(QueueHub.QueueUpdatedEvent, item, ct);

    public Task NotifyInvoiceUpdatedAsync(InvoiceDto invoice, CancellationToken ct = default) =>
        _hubContext.Clients.All.SendAsync(QueueHub.InvoiceUpdatedEvent, invoice, ct);

    public Task NotifyWhatsAppUpdatedAsync(WhatsAppThreadDto thread, CancellationToken ct = default) =>
        _hubContext.Clients.All.SendAsync(QueueHub.WhatsAppUpdatedEvent, thread, ct);

    public Task NotifyEmergencyEscalatedAsync(WhatsAppThreadDto thread, CancellationToken ct = default) =>
        _hubContext.Clients.All.SendAsync(QueueHub.EmergencyEscalatedEvent, thread, ct);
}
