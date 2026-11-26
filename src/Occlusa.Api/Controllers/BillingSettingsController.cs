using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api/billing-settings")]
public sealed class BillingSettingsController : ControllerBase
{
    private readonly IOcclusaRepository _repository;

    public BillingSettingsController(IOcclusaRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<BillingSettingsSnapshotDto>> GetBillingSettings(CancellationToken ct = default)
    {
        var settings = await _repository.GetBillingSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPost("catalog")]
    public async Task<ActionResult<BillingCatalogItemDto>> UpsertCatalogItem(
        [FromBody] UpsertBillingCatalogItemRequest request,
        CancellationToken ct = default)
    {
        var (item, error) = await _repository.UpsertBillingCatalogItemAsync(request, ct);
        if (error is not null || item is null)
        {
            return BadRequest(error ?? "Could not save billing catalog item.");
        }

        return Ok(item);
    }

    [HttpPut("doctor-preferences")]
    public async Task<ActionResult<DoctorInvoicePreferenceDto>> SaveDoctorPreference(
        [FromBody] SaveDoctorInvoicePreferenceRequest request,
        CancellationToken ct = default)
    {
        var (pref, error) = await _repository.SaveDoctorInvoicePreferenceAsync(request, ct);
        if (error is not null || pref is null)
        {
            return BadRequest(error ?? "Could not save doctor invoice preference.");
        }

        return Ok(pref);
    }
}

