using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Api.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class PatientsController : ControllerBase
{
    private readonly IOcclusaRepository _repository;
    private readonly IRealtimeNotifier _notifier;

    public PatientsController(IOcclusaRepository repository, IRealtimeNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    [HttpGet("patients")]
    public async Task<ActionResult<IReadOnlyList<PatientDto>>> GetPatients(
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var patients = await _repository.GetPatientsAsync(search, ct);
        return Ok(patients);
    }

    [HttpGet("patients/{id:guid}")]
    public async Task<ActionResult<PatientDto>> GetPatient(Guid id, CancellationToken ct = default)
    {
        var patient = await _repository.GetPatientByIdAsync(id, ct);
        return patient is null ? NotFound() : Ok(patient);
    }

    [HttpPost("patients")]
    public async Task<ActionResult<PatientDto>> UpsertPatient(
        [FromBody] UpsertPatientRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) && string.IsNullOrWhiteSpace(request.IcNumber))
        {
            return BadRequest("FullName or IcNumber is required.");
        }

        var saved = await _repository.UpsertPatientAsync(request, forceMyKadVerified: false, ct);
        return Ok(saved);
    }

    [HttpPost("patients/mykad-upsert")]
    public async Task<ActionResult<PatientDto>> MyKadUpsert(
        [FromBody] UpsertPatientRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IcNumber))
        {
            return BadRequest("IcNumber is required for MyKad upsert.");
        }

        var saved = await _repository.UpsertPatientAsync(request, forceMyKadVerified: true, ct);
        return Ok(saved);
    }

    [HttpPut("patients/{id:guid}/medical-alerts")]
    public async Task<ActionResult<PatientDto>> UpdateMedicalAlerts(
        Guid id,
        [FromBody] MedicalAlertDto alerts,
        [FromQuery] string branchId = "HQ-KL",
        CancellationToken ct = default)
    {
        var updated = await _repository.UpdateMedicalAlertsAsync(id, alerts, branchId, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("patients/{id:guid}/consent")]
    public async Task<ActionResult<PatientDto>> SignConsent(
        Guid id,
        [FromBody] SignConsentRequest? request = null,
        [FromQuery] string branchId = "HQ-KL",
        CancellationToken ct = default)
    {
        var updated = await _repository.SignConsentAsync(id, request ?? new SignConsentRequest(), branchId, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("patients/{id:guid}/odontogram")]
    public async Task<ActionResult<IReadOnlyList<OdontogramToothDto>>> GetOdontogram(
        Guid id,
        CancellationToken ct = default)
    {
        var patient = await _repository.GetPatientByIdAsync(id, ct);
        if (patient is null)
        {
            return NotFound();
        }

        var chart = await _repository.GetOdontogramAsync(id, ct);
        return Ok(chart);
    }

    [HttpPut("patients/{id:guid}/odontogram")]
    public async Task<ActionResult<OdontogramToothDto>> UpdateOdontogram(
        Guid id,
        [FromBody] UpdateOdontogramRequest request,
        CancellationToken ct = default)
    {
        var (tooth, updatedInvoice, error) = await _repository.UpdateOdontogramAsync(id, request, ct);
        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(error)
                : BadRequest(error);
        }

        if (updatedInvoice is not null)
        {
            await _notifier.NotifyInvoiceUpdatedAsync(updatedInvoice, ct);
        }

        return Ok(tooth);
    }

    [HttpGet("patients/{id:guid}/soap-notes")]
    public async Task<ActionResult<IReadOnlyList<SoapNoteDto>>> GetSoapNotes(
        Guid id,
        CancellationToken ct = default)
    {
        var patient = await _repository.GetPatientByIdAsync(id, ct);
        if (patient is null)
        {
            return NotFound();
        }

        var notes = await _repository.GetSoapNotesAsync(id, ct);
        return Ok(notes);
    }

    [HttpPost("patients/{id:guid}/soap-notes")]
    public async Task<ActionResult<SoapNoteDto>> CreateSoapNote(
        Guid id,
        [FromBody] CreateSoapNoteRequest request,
        CancellationToken ct = default)
    {
        var note = await _repository.CreateSoapNoteAsync(id, request, ct);
        return note is null
            ? NotFound()
            : CreatedAtAction(nameof(GetSoapNotes), new { id }, note);
    }

    [HttpGet("soap-macros")]
    public async Task<ActionResult<IReadOnlyList<SoapMacroDto>>> GetSoapMacros(CancellationToken ct = default)
    {
        var macros = await _repository.GetSoapMacrosAsync(ct);
        return Ok(macros);
    }
}
