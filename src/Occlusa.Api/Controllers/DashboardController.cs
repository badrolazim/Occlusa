using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IOcclusaRepository _repository;

    public DashboardController(IOcclusaRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("snapshot")]
    public async Task<ActionResult<ClinicDashboardSnapshotDto>> GetSnapshot(
        [FromQuery] string branchId = "HQ-KL",
        CancellationToken ct = default)
    {
        var snapshot = await _repository.GetDashboardSnapshotAsync(branchId, ct);
        return Ok(snapshot);
    }
}
