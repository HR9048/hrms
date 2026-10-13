using HrmsBackend.Common;
using HrmsBackend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/data-scope")]
[Authorize]
public class DataScopeController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    public DataScopeController(DbConnectionFactory db) => _db = db;

    // GET /api/data-scope -> { scope, organizations, nodeTypes, nodes } limited to what the logged-in user may see
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            await using var c = _db.CreateConnection();
            await c.OpenAsync();
            var scope = await DataScope.ForUserAsync(c, User, Convert.ToInt64(this.GetAuthUser()!.EmployeeId));
            return Ok(await DataScope.OrgLookupsAsync(c, scope));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error." });
        }
    }
}