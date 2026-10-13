using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly TokenService _tokenService;

    public AuthController(DbConnectionFactory db, TokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public record LoginRequest(string? Username, string? Password);
    public record SwitchAccountRequest(int? TargetAccountId);

    // Row shape shared by login / switch-account / me queries.
    // Only the columns actually used are declared — Dapper ignores the rest
    // of the `ua.*` wildcard automatically.
    private class AccountRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int RoleId { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string RoleCode { get; set; } = "";
        public string RoleName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string? LastName { get; set; }
        public string EmployeeCode { get; set; } = "";
    }

    private static string BuildEmployeeName(string firstName, string? lastName) =>
        $"{firstName} {lastName ?? ""}".Trim();

    // ---------------------------------------------------------
    // POST /api/auth/login
    // ---------------------------------------------------------
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? body)
    {
        if (string.IsNullOrEmpty(body?.Username) || string.IsNullOrEmpty(body?.Password))
        {
            return BadRequest(new { message = "Username and password are required" });
        }

        using var conn = _db.CreateConnection();
        try
        {
            var account = await conn.QueryFirstOrDefaultAsync<AccountRow>(
                @"SELECT ua.*, r.role_code, r.role_name, e.first_name, e.last_name, e.employee_code
                  FROM user_accounts ua
                  JOIN roles r ON r.id = ua.role_id
                  JOIN employee_master e ON e.employee_id = ua.employee_id
                  WHERE ua.username = @Username AND ua.is_active = 1",
                new { body.Username });

            if (account == null)
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            var passwordOk = BCrypt.Net.BCrypt.Verify(body.Password, account.PasswordHash);
            if (!passwordOk)
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            var siblings = (await conn.QueryAsync(
                @"SELECT ua.id, ua.username, ua.role_id, r.role_code, r.role_name
                  FROM user_accounts ua
                  JOIN roles r ON r.id = ua.role_id
                  WHERE ua.employee_id = @EmployeeId AND ua.is_active = 1
                  ORDER BY ua.is_default DESC, r.role_name ASC",
                new { account.EmployeeId })).ToDictList();

            await conn.ExecuteAsync(
                "INSERT INTO login_logs (user_account_id, ip_address) VALUES (@AccountId, @Ip)",
                new { AccountId = account.Id, Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            var token = _tokenService.SignToken(
                new AccountForToken(account.Id, account.EmployeeId, account.RoleId, account.RoleCode, account.Username));

            return Ok(new
            {
                token,
                account = new
                {
                    accountId = account.Id,
                    username = account.Username,
                    roleCode = account.RoleCode,
                    roleName = account.RoleName,
                    employeeId = account.EmployeeId,
                    employeeName = BuildEmployeeName(account.FirstName, account.LastName),
                    employeeCode = account.EmployeeCode
                },
                accounts = siblings
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error during login" });
        }
    }

    // ---------------------------------------------------------
    // POST /api/auth/switch-account
    // ---------------------------------------------------------
    [HttpPost("switch-account")]
    [Authorize]
    public async Task<IActionResult> SwitchAccount([FromBody] SwitchAccountRequest? body)
    {
        if (body?.TargetAccountId == null)
        {
            return BadRequest(new { message = "targetAccountId is required" });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        try
        {
            var target = await conn.QueryFirstOrDefaultAsync<AccountRow>(
                @"SELECT ua.*, r.role_code, r.role_name, e.first_name, e.last_name, e.employee_code
                  FROM user_accounts ua
                  JOIN roles r ON r.id = ua.role_id
                  JOIN employee_master e ON e.employee_id = ua.employee_id
                  WHERE ua.id = @Id AND ua.is_active = 1",
                new { Id = body.TargetAccountId });

            if (target == null)
            {
                return NotFound(new { message = "Account not found" });
            }

            // security: only allow switching between accounts of the SAME employee
            if (target.EmployeeId != user.EmployeeId)
            {
                return StatusCode(403, new { message = "Cannot switch to an account that is not yours" });
            }

            var token = _tokenService.SignToken(
                new AccountForToken(target.Id, target.EmployeeId, target.RoleId, target.RoleCode, target.Username));

            return Ok(new
            {
                token,
                account = new
                {
                    accountId = target.Id,
                    username = target.Username,
                    roleCode = target.RoleCode,
                    roleName = target.RoleName,
                    employeeId = target.EmployeeId,
                    employeeName = BuildEmployeeName(target.FirstName, target.LastName),
                    employeeCode = target.EmployeeCode
                }
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while switching account" });
        }
    }

    // ---------------------------------------------------------
    // GET /api/auth/me
    // ---------------------------------------------------------
    private class MeRow
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string RoleCode { get; set; } = "";
        public string RoleName { get; set; } = "";
        public int EmployeeId { get; set; }
        public string FirstName { get; set; } = "";
        public string? LastName { get; set; }
        public string EmployeeCode { get; set; } = "";
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        try
        {
            var a = await conn.QueryFirstOrDefaultAsync<MeRow>(
                @"SELECT ua.id, ua.username, r.role_code, r.role_name, e.employee_id AS employee_id,
                         e.first_name, e.last_name, e.employee_code
                  FROM user_accounts ua
                  JOIN roles r ON r.id = ua.role_id
                  JOIN employee_master e ON e.employee_id = ua.employee_id
                  WHERE ua.id = @AccountId",
                new { user.AccountId });

            if (a == null)
            {
                return NotFound(new { message = "Account not found" });
            }

            var siblings = (await conn.QueryAsync(
                @"SELECT ua.id, ua.username, ua.role_id, r.role_code, r.role_name
                  FROM user_accounts ua JOIN roles r ON r.id = ua.role_id
                  WHERE ua.employee_id = @EmployeeId AND ua.is_active = 1",
                new { a.EmployeeId })).ToDictList();

            return Ok(new
            {
                account = new
                {
                    accountId = a.Id,
                    username = a.Username,
                    roleCode = a.RoleCode,
                    roleName = a.RoleName,
                    employeeId = a.EmployeeId,
                    employeeName = BuildEmployeeName(a.FirstName, a.LastName),
                    employeeCode = a.EmployeeCode
                },
                accounts = siblings
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }
}
