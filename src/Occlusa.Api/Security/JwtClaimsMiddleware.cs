using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Occlusa.Api.Options;

namespace Occlusa.Api.Security;

/// <summary>
/// Extracts JWT claims (allowed_branches, role, clinic_group_id, sub) into RequestClaimsContext
/// so repository queries propagate claims to Supabase PostgreSQL RLS via set_config('request.jwt.claims', ...).
/// </summary>
public sealed class JwtClaimsMiddleware
{
    private readonly RequestDelegate _next;

    public JwtClaimsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        RequestClaimsContext claimsContext,
        IOptions<SupabaseOptions> supabaseOptions)
    {
        var defaultGroup = string.IsNullOrWhiteSpace(supabaseOptions.Value.ClinicGroupId)
            ? "OCCLUSA-MY"
            : supabaseOptions.Value.ClinicGroupId;

        claimsContext.ClinicGroupId = defaultGroup;

        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? user.FindFirstValue("sub");
            if (!string.IsNullOrWhiteSpace(sub))
            {
                claimsContext.UserId = sub;
            }

            var role = user.FindFirstValue(ClaimTypes.Role)
                       ?? user.FindFirstValue("user_role")
                       ?? user.FindFirstValue("role");
            if (!string.IsNullOrWhiteSpace(role))
            {
                claimsContext.Role = role;
            }

            var groupClaim = user.FindFirstValue("clinic_group_id");
            if (!string.IsNullOrWhiteSpace(groupClaim))
            {
                claimsContext.ClinicGroupId = groupClaim;
            }

            var branches = ExtractAllowedBranches(user);
            if (branches.Count > 0)
            {
                claimsContext.AllowedBranches = branches;
            }
        }
        else if (context.Request.Headers.TryGetValue("X-Occlusa-User", out var headerUser)
                 && !string.IsNullOrWhiteSpace(headerUser))
        {
            // Local development convenience header when JWT auth is not enabled.
            claimsContext.UserId = headerUser.ToString().Trim();
        }

        if (user?.Identity?.IsAuthenticated != true
            && context.Request.Headers.TryGetValue("X-Occlusa-Allowed-Branches", out var headerBranches)
            && !string.IsNullOrWhiteSpace(headerBranches))
        {
            var parsed = headerBranches.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (parsed.Count > 0)
            {
                claimsContext.AllowedBranches = parsed;
            }
        }

        await _next(context);
    }

    private static List<string> ExtractAllowedBranches(ClaimsPrincipal user)
    {
        var claims = user.FindAll("allowed_branches").ToList();
        if (claims.Count == 0)
        {
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var claim in claims)
        {
            var raw = claim.Value?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            if (raw.StartsWith('['))
            {
                try
                {
                    var array = JsonSerializer.Deserialize<List<string>>(raw);
                    if (array is not null)
                    {
                        result.AddRange(array.Where(b => !string.IsNullOrWhiteSpace(b)));
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed JSON array claim.
                }
            }
            else
            {
                result.Add(raw);
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
