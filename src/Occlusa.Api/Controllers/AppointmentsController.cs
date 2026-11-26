using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Api.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class AppointmentsController : ControllerBase
{
    private readonly IOcclusaRepository _repository;
    private readonly IRealtimeNotifier _notifier;
    private readonly IWhatsAppAiReceptionistService? _whatsAppService;

    public AppointmentsController(
        IOcclusaRepository repository,
        IRealtimeNotifier notifier,
        IWhatsAppAiReceptionistService? whatsAppService = null)
    {
        _repository = repository;
        _notifier = notifier;
        _whatsAppService = whatsAppService;
    }

    [HttpGet("appointments")]
    public async Task<ActionResult<IReadOnlyList<AppointmentQueueItemDto>>> GetAppointments(
        [FromQuery] string? branchId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? dentistId = null,
        [FromQuery] Guid? treatmentId = null,
        CancellationToken ct = default)
    {
        var items = await _repository.GetAppointmentsAsync(branchId, from, to, dentistId, treatmentId, ct);
        return Ok(items);
    }

    [HttpPost("appointments")]
    public async Task<ActionResult<AppointmentQueueItemDto>> CreateAppointment(
        [FromBody] CreateAppointmentRequest request,
        CancellationToken ct = default)
    {
        var created = await _repository.CreateAppointmentAsync(request, ct);
        await _notifier.NotifyQueueUpdatedAsync(created, ct);
        return Created($"/api/appointments/{created.Id}", created);
    }

    [HttpPost("appointments/register-and-book")]
    public async Task<ActionResult<RegisterPatientAndAppointmentResponse>> RegisterPatientAndBook(
        [FromBody] RegisterPatientAndAppointmentRequest request,
        CancellationToken ct = default)
    {
        if (request.Patient is null || string.IsNullOrWhiteSpace(request.Patient.FullName))
        {
            return BadRequest("Patient FullName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Patient.IcNumber))
        {
            return BadRequest("Patient IcNumber or Passport # is required.");
        }

        var result = await _repository.RegisterPatientAndCreateAppointmentAsync(request, ct);
        await _notifier.NotifyQueueUpdatedAsync(result.Appointment, ct);
        return Created($"/api/appointments/{result.Appointment.Id}", result);
    }

    [HttpPut("appointments/{id:guid}")]
    public async Task<ActionResult<AppointmentQueueItemDto>> UpdateAppointment(
        Guid id,
        [FromBody] UpdateAppointmentRequest request,
        CancellationToken ct = default)
    {
        var (item, error) = await _repository.UpdateAppointmentAsync(id, request, ct);
        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error);
        }

        await _notifier.NotifyQueueUpdatedAsync(item!, ct);
        return Ok(item);
    }

    [HttpGet("dentists")]
    public async Task<ActionResult<IReadOnlyList<DentistDto>>> GetDentists(
        [FromQuery] string? branchId = null,
        CancellationToken ct = default)
    {
        var dentists = await _repository.GetDentistsAsync(branchId, ct);
        return Ok(dentists);
    }

    [HttpGet("treatments")]
    public async Task<ActionResult<IReadOnlyList<TreatmentDto>>> GetTreatments(
        CancellationToken ct = default)
    {
        var treatments = await _repository.GetTreatmentsAsync(ct);
        return Ok(treatments);
    }

    [HttpPatch("queue/{id:guid}/status")]
    public async Task<ActionResult<AppointmentQueueItemDto>> UpdateQueueStatus(
        Guid id,
        [FromBody] UpdateQueueStatusRequest request,
        CancellationToken ct = default)
    {
        var (item, error) = await _repository.UpdateQueueStatusAsync(id, request, ct);
        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error);
        }

        await _notifier.NotifyQueueUpdatedAsync(item!, ct);
        return Ok(item);
    }

    [HttpPost("appointments/{id:guid}/reminder")]
    public async Task<ActionResult<UpdateAppointmentReminderResponse>> UpdateAppointmentReminder(
        Guid id,
        [FromBody] UpdateAppointmentReminderRequest request,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetAppointmentByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound("Appointment not found.");
        }

        if (AppointmentCalendarHelper.IsFinishedQueueStatus(existing.Status))
        {
            return BadRequest("Cannot send or update reminders for a completed or cancelled appointment.");
        }

        var actor = string.IsNullOrWhiteSpace(request.UpdatedBy) ? "Desktop Staff" : request.UpdatedBy.Trim();
        var effectiveStatus = request.SendWhatsAppMessage ? ReminderStatus.Reminded : request.Status;
        var effectiveChannel = request.SendWhatsAppMessage ? ReminderChannel.WhatsApp : request.Channel;

        WhatsAppThreadDto? thread = null;
        bool waSent = false;
        if (request.SendWhatsAppMessage)
        {
            if (_whatsAppService is null)
            {
                return BadRequest("WhatsApp reminder service is not configured.");
            }

            var patient = await _repository.GetPatientByIdAsync(existing.PatientId, ct);
            var phone = patient?.Phone?.Trim();
            if (string.IsNullOrWhiteSpace(phone))
            {
                return BadRequest("Patient phone number is required to send a WhatsApp reminder.");
            }

            var messageBody = string.IsNullOrWhiteSpace(request.CustomMessageText)
                ? AppointmentCalendarHelper.BuildBilingualReminderMessage(existing, existing.BranchId, TimeSpan.FromHours(8))
                : request.CustomMessageText.Trim();

            thread = await _whatsAppService.SendAppointmentReminderAsync(existing, phone, messageBody, actor, ct);
            waSent = true;
        }

        var (updated, error) = await _repository.UpdateAppointmentReminderAsync(id, effectiveStatus, effectiveChannel, actor, ct);
        if (error is not null || updated is null)
        {
            return BadRequest(error ?? "Failed to update appointment reminder.");
        }

        await _notifier.NotifyQueueUpdatedAsync(updated, ct);
        return Ok(new UpdateAppointmentReminderResponse
        {
            Appointment = updated,
            WhatsAppThread = thread,
            WhatsAppMessageSent = waSent,
            Message = waSent
                ? $"WhatsApp reminder sent to {updated.PatientName}."
                : $"Reminder updated to {updated.ReminderStatus} ({updated.ReminderChannel})."
        });
    }
}
