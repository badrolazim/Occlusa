using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly IOcclusaRepository _repository;
    private readonly ClinicOptions _clinicOptions;

    public HealthController(IOcclusaRepository repository, IOptions<ClinicOptions> clinicOptions)
    {
        _repository = repository;
        _clinicOptions = clinicOptions.Value;
    }

    [HttpGet]
    public ActionResult<HealthStatusDto> Get()
    {
        return Ok(new HealthStatusDto
        {
            Status = "Healthy",
            DatabaseMode = _repository.DatabaseMode,
            DefaultBranchId = string.IsNullOrWhiteSpace(_clinicOptions.DefaultBranchId) ? "HQ-KL" : _clinicOptions.DefaultBranchId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
