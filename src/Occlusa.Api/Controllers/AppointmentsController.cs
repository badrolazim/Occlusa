using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Api.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class AppointmentsController : ControllerBase
{
    private readonly IOcclusaRepository _repository;
    private readonly IRealtimeNotifier _notifier;

    public AppointmentsController(IOcclusaRepository repository, IRealtimeNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
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
}
