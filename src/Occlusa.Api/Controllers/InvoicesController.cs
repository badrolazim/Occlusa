using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Api.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IOcclusaRepository _repository;
    private readonly IRealtimeNotifier _notifier;

    public InvoicesController(IOcclusaRepository repository, IRealtimeNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GetInvoices(
        [FromQuery] string? branchId = null,
        CancellationToken ct = default)
    {
        var invoices = await _repository.GetInvoicesAsync(branchId, ct);
        return Ok(invoices);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> GetInvoice(Guid id, CancellationToken ct = default)
    {
        var invoice = await _repository.GetInvoiceByIdAsync(id, ct);
        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpPost("{id:guid}/pay")]
    public async Task<ActionResult<InvoiceDto>> ApplyPayment(
        Guid id,
        [FromBody] ApplyPaymentRequest request,
        CancellationToken ct = default)
    {
        var (invoice, updatedQueueItem, error) = await _repository.ApplyInvoicePaymentAsync(id, request, ct);
        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error);
        }

        await _notifier.NotifyInvoiceUpdatedAsync(invoice!, ct);
        if (updatedQueueItem is not null)
        {
            await _notifier.NotifyQueueUpdatedAsync(updatedQueueItem, ct);
        }

        return Ok(invoice);
    }

    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<InvoiceDto>> VoidInvoice(
        Guid id,
        [FromBody] VoidInvoiceRequest request,
        CancellationToken ct = default)
    {
        var (invoice, error) = await _repository.VoidInvoiceAsync(id, request, ct);
        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error);
        }

        await _notifier.NotifyInvoiceUpdatedAsync(invoice!, ct);
        return Ok(invoice);
    }
}
