using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

// All routes here are SuperAdmin-only lookup/reference data used by the admin forms
// (equivalent of router.use(requireAuth, requireRole('SUPERADMIN')) in admin.js).
[ApiController]
[Route("api/admin")]
[Authorize]
[RequireRole("SUPERADMIN")]
public class AdminController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    public AdminController(DbConnectionFactory db) => _db = db;

    [HttpGet("roles")]
    public async Task<IActionResult> Roles()
    {
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync(
            "SELECT id, role_code, role_name FROM roles where active = 1 ORDER BY sequence")).ToDictList();
        return Ok(rows);
    }

    [HttpGet("departments")]
    public async Task<IActionResult> Departments()
    {
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync(
            "SELECT id, name FROM departments where active = 1 ORDER BY sequence")).ToDictList();
        return Ok(rows);
    }

    [HttpGet("designations")]
    public async Task<IActionResult> Designations()
    {
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync(
            "SELECT id, name FROM designations where active = 1 ORDER BY sequence")).ToDictList();
        return Ok(rows);
    }

    public record NameRequest(string? Name);

    [HttpPost("departments")]
    public async Task<IActionResult> AddDepartment([FromBody] NameRequest? body)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("INSERT INTO departments (name) VALUES (@Name)", new { body?.Name });
        var id = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
        return StatusCode(201, new { id });
    }

    [HttpPost("designations")]
    public async Task<IActionResult> AddDesignation([FromBody] NameRequest? body)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("INSERT INTO designations (name) VALUES (@Name)", new { body?.Name });
        var id = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
        return StatusCode(201, new { id });
    }
}
