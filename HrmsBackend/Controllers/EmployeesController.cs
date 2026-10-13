using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    public EmployeesController(DbConnectionFactory db) => _db = db;

    // GET /api/employees/:id/accounts — all login accounts for one employee (only for employees inside the caller's data scope)
    [HttpGet("{id:int}/accounts")]
    [Authorize]
    [RequireRole("SUPERADMIN", "HR")]
    public async Task<IActionResult> GetAccounts(int id)
    {
        using var conn = _db.CreateConnection();
        try
        {
            var scope = await DataScope.ForUserAsync(conn, User, Convert.ToInt64(this.GetAuthUser()!.EmployeeId));
            if (!scope.Unrestricted)
            {
                var node = await conn.ExecuteScalarAsync<int?>(
                    "SELECT node_id FROM master_employee_organization_mapping WHERE employee_id=@id AND is_current=1 ORDER BY mapping_id DESC LIMIT 1", new { id });
                if (node == null || !scope.Contains(node.Value))
                    return StatusCode(403, new { message = "You do not have access to this employee." });
            }

            var rows = (await conn.QueryAsync(
                @"SELECT ua.id, ua.username, ua.is_active, ua.is_default, r.role_code, r.role_name
                  FROM user_accounts ua JOIN roles r ON r.id = ua.role_id
                  WHERE ua.employee_id = @id",
                new { id })).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    public record AddAccountRequest(int? RoleId, string? Username, string? Password, bool? IsDefault);

    // ---------------------------------------------------------
    // POST /api/employees/:id/accounts — add ONE MORE account/role to an existing employee.
    // ---------------------------------------------------------
    [HttpPost("{id:int}/accounts")]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> AddAccount(int id, [FromBody] AddAccountRequest? body)
    {
        if (body?.RoleId == null || string.IsNullOrEmpty(body?.Username) || string.IsNullOrEmpty(body?.Password))
        {
            return BadRequest(new { message = "roleId, username and password are required" });
        }

        using var conn = _db.CreateConnection();
        try
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(body.Password, workFactor: 10);

            if (body.IsDefault == true)
            {
                await conn.ExecuteAsync("UPDATE user_accounts SET is_default = 0 WHERE employee_id = @id", new { id });
            }

            await conn.ExecuteAsync(
                @"INSERT INTO user_accounts (employee_id, role_id, username, password_hash, is_default)
                  VALUES (@id, @roleId, @username, @hash, @isDefault)",
                new { id, roleId = body.RoleId, username = body.Username, hash, isDefault = body.IsDefault == true ? 1 : 0 });

            return StatusCode(201, new { message = "Account added" });
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode == 1062) // ER_DUP_ENTRY
        {
            return Conflict(new { message = "Username already exists, or this employee already has an account for that role" });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while adding account" });
        }
    }
}