using Microsoft.AspNetCore.Mvc;
using Occlusa.Api.Repositories;
using Occlusa.Shared.Models;

namespace Occlusa.Api.Controllers;

[ApiController]
[Route("api/user-preferences")]
public sealed class UserPreferencesController : ControllerBase
{
    private readonly IOcclusaRepository _repository;

    public UserPreferencesController(IOcclusaRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserWorkspacePreferencesDto>>> GetAll(CancellationToken ct = default)
    {
        var items = await _repository.GetUserWorkspacePreferencesAsync(ct);
        return Ok(items);
    }

    [HttpPut]
    public async Task<ActionResult<UserWorkspacePreferencesDto>> Save(
        [FromBody] SaveUserWorkspacePreferencesRequest request,
        CancellationToken ct = default)
    {
        if (request is null || (string.IsNullOrWhiteSpace(request.StaffKey) && string.IsNullOrWhiteSpace(request.StaffDisplayName)))
        {
            return BadRequest("StaffKey or StaffDisplayName is required.");
        }

        var (saved, error) = await _repository.SaveUserWorkspacePreferencesAsync(request, ct);
        if (error is not null || saved is null)
        {
            return BadRequest(error ?? "Could not save user preferences.");
        }

        return Ok(saved);
    }
}

