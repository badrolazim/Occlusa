using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Api.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class VisitsController : ControllerBase
{
    private readonly IOcclusaRepository _repository;
    private readonly IRealtimeNotifier _notifier;

    public VisitsController(IOcclusaRepository repository, IRealtimeNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    [HttpGet("patients/{patientId:guid}/visits")]
    public async Task<ActionResult<IReadOnlyList<PatientVisitDto>>> GetPatientVisits(
        Guid patientId,
        CancellationToken ct = default)
    {
        var patient = await _repository.GetPatientByIdAsync(patientId, ct);
        if (patient is null)
        {
            return NotFound("Patient not found.");
        }

        var visits = await _repository.GetPatientVisitsAsync(patientId, ct);
        return Ok(visits);
    }

    [HttpPost("patients/{patientId:guid}/visits")]
    public async Task<ActionResult<PatientVisitDto>> CreatePatientVisit(
        Guid patientId,
        [FromBody] CreatePatientVisitRequest request,
        CancellationToken ct = default)
    {
        var (visit, updatedAppointment, error) = await _repository.CreatePatientVisitAsync(patientId, request, ct);
        if (error is not null || visit is null)
        {
            return (error ?? string.Empty).Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error ?? "Could not create visit.");
        }

        if (updatedAppointment is not null)
        {
            await _notifier.NotifyQueueUpdatedAsync(updatedAppointment, ct);
        }

        return Created($"/api/visits/{visit.Id}", visit);
    }

    [HttpPost("appointments/{appointmentId:guid}/ensure-visit")]
    public async Task<ActionResult<PatientVisitDto>> EnsureVisitForAppointment(
        Guid appointmentId,
        [FromQuery] string? actor = null,
        CancellationToken ct = default)
    {
        var (visit, updatedAppointment, error) = await _repository.EnsureVisitForAppointmentAsync(appointmentId, actor, ct);
        if (error is not null || visit is null)
        {
            return (error ?? string.Empty).Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error ?? "Could not ensure visit for appointment.");
        }

        if (updatedAppointment is not null)
        {
            await _notifier.NotifyQueueUpdatedAsync(updatedAppointment, ct);
        }

        return Ok(visit);
    }

    [HttpGet("visits/{visitId:guid}")]
    public async Task<ActionResult<PatientVisitDto>> GetVisit(Guid visitId, CancellationToken ct = default)
    {
        var visit = await _repository.GetVisitByIdAsync(visitId, ct);
        return visit is null ? NotFound("Visit record not found.") : Ok(visit);
    }

    [HttpPut("visits/{visitId:guid}")]
    public async Task<ActionResult<PatientVisitDto>> UpdateVisit(
        Guid visitId,
        [FromBody] UpdatePatientVisitRequest request,
        CancellationToken ct = default)
    {
        var (visit, error) = await _repository.UpdatePatientVisitAsync(visitId, request, ct);
        if (error is not null || visit is null)
        {
            return (error ?? string.Empty).Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error ?? "Could not update visit.");
        }

        return Ok(visit);
    }

    [HttpGet("visits/{visitId:guid}/odontogram")]
    public async Task<ActionResult<IReadOnlyList<OdontogramToothDto>>> GetVisitOdontogram(
        Guid visitId,
        CancellationToken ct = default)
    {
        var visit = await _repository.GetVisitByIdAsync(visitId, ct);
        if (visit is null)
        {
            return NotFound("Visit record not found.");
        }

        var chart = await _repository.GetVisitOdontogramAsync(visitId, ct);
        return Ok(chart);
    }

    [HttpPut("visits/{visitId:guid}/odontogram")]
    public async Task<ActionResult<OdontogramToothDto>> UpdateVisitOdontogram(
        Guid visitId,
        [FromBody] UpdateOdontogramRequest request,
        CancellationToken ct = default)
    {
        var (tooth, updatedInvoice, error) = await _repository.UpdateVisitOdontogramAsync(visitId, request, ct);
        if (error is not null || tooth is null)
        {
            return (error ?? string.Empty).Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error ?? "Could not update visit odontogram.");
        }

        if (updatedInvoice is not null)
        {
            await _notifier.NotifyInvoiceUpdatedAsync(updatedInvoice, ct);
        }

        return Ok(tooth);
    }

    [HttpPost("visits/{visitId:guid}/odontogram/reset")]
    public async Task<ActionResult<PatientVisitDto>> ResetVisitOdontogram(
        Guid visitId,
        [FromBody] ResetVisitOdontogramRequest? request = null,
        CancellationToken ct = default)
    {
        var (visit, error) = await _repository.ResetVisitOdontogramAsync(
            visitId,
            request ?? new ResetVisitOdontogramRequest(),
            ct);
        if (error is not null || visit is null)
        {
            return (error ?? string.Empty).Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error ?? "Could not reset visit odontogram.");
        }

        return Ok(visit);
    }
}

