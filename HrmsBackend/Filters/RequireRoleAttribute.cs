using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HrmsBackend.Filters;

/// <summary>
/// Equivalent of src/middleware/role.js's requireRole(...allowedRoleCodes).
/// Usage: [RequireRole("SUPERADMIN")] or [RequireRole("SUPERADMIN", "HR")]
/// Always combine with [Authorize] (which plays the role of requireAuth).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _allowedRoleCodes;

    public RequireRoleAttribute(params string[] allowedRoleCodes)
    {
        _allowedRoleCodes = allowedRoleCodes;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new JsonResult(new { message = "Not authenticated" })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        var roleCode = user.FindFirst("roleCode")?.Value;
        if (roleCode == null || !_allowedRoleCodes.Contains(roleCode))
        {
            context.Result = new JsonResult(new { message = "You do not have permission to perform this action" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
