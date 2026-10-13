using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Common;

/// <summary>
/// Mirrors the shape of req.user set by src/middleware/auth.js:
/// { accountId, employeeId, roleId, roleCode, username }
/// </summary>
public record AuthUser(int AccountId, int EmployeeId, int RoleId, string RoleCode, string Username);

public static class ControllerBaseExtensions
{
    /// <summary>
    /// Reads the claims placed on the token by TokenService.SignToken, the same
    /// way req.user was populated by requireAuth via jwt.verifyToken.
    /// Returns null if the request isn't authenticated.
    /// </summary>
    public static AuthUser? GetAuthUser(this ControllerBase controller)
    {
        var user = controller.User;
        if (user.Identity?.IsAuthenticated != true) return null;

        int.TryParse(user.FindFirst("accountId")?.Value, out var accountId);
        int.TryParse(user.FindFirst("employeeId")?.Value, out var employeeId);
        int.TryParse(user.FindFirst("roleId")?.Value, out var roleId);
        var roleCode = user.FindFirst("roleCode")?.Value ?? "";
        var username = user.FindFirst("username")?.Value ?? "";

        return new AuthUser(accountId, employeeId, roleId, roleCode, username);
    }
}
