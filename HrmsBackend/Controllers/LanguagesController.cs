using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/languages")]
public class LanguagesController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    public LanguagesController(DbConnectionFactory db) => _db = db;

    // GET /api/languages — public, active languages only (login page / navbar toggle)
    [HttpGet]
    public async Task<IActionResult> GetActive()
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                "SELECT id, code, name FROM languages WHERE is_active = 1 ORDER BY (code = 'en') DESC, name"
            )).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    // GET /api/languages/all — public, every language incl. inactive
    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                "SELECT id, code, name, is_active FROM languages ORDER BY (code = 'en') DESC, name"
            )).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }
}
