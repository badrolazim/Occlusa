using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Occlusa.Api.Models;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Api.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class WhatsAppController : ControllerBase
{
    private readonly IOcclusaRepository _repository;
    private readonly IWhatsAppAiReceptionistService _aiService;
    private readonly IRealtimeNotifier _notifier;
    private readonly WhatsAppOptions _whatsAppOptions;
    private readonly ClinicOptions _clinicOptions;

    public WhatsAppController(
        IOcclusaRepository repository,
        IWhatsAppAiReceptionistService aiService,
        IRealtimeNotifier notifier,
        IOptions<WhatsAppOptions> whatsAppOptions,
        IOptions<ClinicOptions> clinicOptions)
    {
        _repository = repository;
        _aiService = aiService;
        _notifier = notifier;
        _whatsAppOptions = whatsAppOptions.Value;
        _clinicOptions = clinicOptions.Value;
    }

    /// <summary>
    /// Meta WhatsApp Cloud API Webhook Challenge Verification endpoint.
    /// </summary>
    [HttpGet("webhooks/whatsapp")]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var configuredToken = _whatsAppOptions.VerifyToken;
        if (string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(configuredToken)
            && string.Equals(verifyToken, configuredToken, StringComparison.Ordinal))
        {
            return Content(challenge ?? string.Empty, "text/plain");
        }

        return Forbid();
    }

    /// <summary>
    /// Meta WhatsApp Cloud API Webhook inbound message receiver.
    /// </summary>
    [HttpPost("webhooks/whatsapp")]
    public async Task<IActionResult> ReceiveWebhook(
        [FromBody] MetaWebhookPayload payload,
        CancellationToken ct = default)
    {
        if (payload?.Entry is null || payload.Entry.Count == 0)
        {
            return Ok();
        }

        foreach (var entry in payload.Entry)
        {
            foreach (var change in entry.Changes)
            {
                var value = change.Value;
                if (value?.Messages is null || value.Messages.Count == 0)
                {
                    continue;
                }

                var contactName = value.Contacts.FirstOrDefault()?.Profile?.Name ?? string.Empty;
                foreach (var msg in value.Messages)
                {
                    if (!string.Equals(msg.Type, "text", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(msg.Text?.Body)
                        || string.IsNullOrWhiteSpace(msg.From))
                    {
                        continue;
                    }

                    await _aiService.ProcessInboundMessageAsync(
                        threadId: null,
                        patientPhone: msg.From,
                        patientName: string.IsNullOrWhiteSpace(contactName) ? msg.From : contactName,
                        messageText: msg.Text.Body,
                        branchId: _clinicOptions.DefaultBranchId,
                        sendViaMetaCloudApi: true,
                        ct: ct);
                }
            }
        }

        return Ok();
    }

    [HttpGet("whatsapp/threads")]
    public async Task<ActionResult<IReadOnlyList<WhatsAppThreadDto>>> GetThreads(
        [FromQuery] string? branchId = null,
        CancellationToken ct = default)
    {
        var threads = await _repository.GetWhatsAppThreadsAsync(branchId, ct);
        return Ok(threads);
    }

    [HttpPost("whatsapp/simulate")]
    public async Task<ActionResult<WhatsAppThreadDto>> SimulateInbound(
        [FromBody] WhatsAppSimulateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return BadRequest("MessageText is required.");
        }

        var thread = await _aiService.ProcessInboundMessageAsync(
            request.ThreadId,
            request.PatientPhone,
            request.PatientName,
            request.MessageText,
            request.BranchId,
            sendViaMetaCloudApi: false,
            ct: ct);

        return Ok(thread);
    }

    [HttpPost("whatsapp/threads/{threadId}/takeover-reply")]
    public async Task<ActionResult<WhatsAppThreadDto>> TakeoverReply(
        string threadId,
        [FromBody] WhatsAppTakeoverReplyRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return BadRequest("MessageText is required.");
        }

        var updated = await _aiService.SendStaffTakeoverReplyAsync(
            threadId,
            request.MessageText,
            request.StaffName,
            ct);

        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("whatsapp/bookings/{appointmentId:guid}/approve")]
    public async Task<ActionResult<AppointmentQueueItemDto>> ApproveAiBooking(
        Guid appointmentId,
        [FromQuery] string approvedBy = "Reception",
        CancellationToken ct = default)
    {
        var (appointment, thread, error) = await _repository.ApproveAiBookingAsync(appointmentId, approvedBy, ct);
        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error);
        }

        await _notifier.NotifyQueueUpdatedAsync(appointment!, ct);
        if (thread is not null)
        {
            await _notifier.NotifyWhatsAppUpdatedAsync(thread, ct);
        }

        return Ok(appointment);
    }
}
