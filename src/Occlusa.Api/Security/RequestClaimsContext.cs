using System.Text.Json;

namespace Occlusa.Api.Security;

/// <summary>
/// Holds the current request's authenticated user and branch permissions for PostgreSQL RLS propagation.
/// </summary>
public sealed class RequestClaimsContext
{
    public string UserId { get; set; } = "staff-hq-01";
    public string Role { get; set; } = "ClinicAdmin";
    public string ClinicGroupId { get; set; } = "OCCLUSA-MY";
    public List<string> AllowedBranches { get; set; } = new() { "HQ-KL", "PJ-SS2" };

    public bool CanAccessBranch(string branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            return false;
        }

        return AllowedBranches.Contains(branchId, StringComparer.OrdinalIgnoreCase);
    }

    public string ToRlsClaimsJson()
    {
        return JsonSerializer.Serialize(new
        {
            sub = UserId,
            role = Role,
            clinic_group_id = ClinicGroupId,
            allowed_branches = AllowedBranches
        });
    }
}
