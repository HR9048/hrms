using System.Data;
using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HrmsBackend.Controllers;

public abstract class TransferBase : ControllerBase
{
    protected readonly DbConnectionFactory Db;
    protected TransferBase(DbConnectionFactory db) { Db = db; }

    protected async Task<IActionResult> Run(Func<MySqlConnection, Task<object?>> action)
    {
        try
        {
            await using var conn = Db.CreateConnection();
            await conn.OpenAsync();
            var result = await action(conn);
            return result is IActionResult ar ? ar : Ok(result);
        }
        catch (TrException ex) { return StatusCode(ex.Status, new { message = ex.Message, checks = ex.Checks }); }
        catch (MySqlException ex) when (ex.Number == 1062) { return Conflict(new { message = "A record with the same unique value already exists." }); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return StatusCode(500, new { message = "Server error." }); }
    }

    protected long MyId() => Convert.ToInt64(this.GetAuthUser()!.EmployeeId);
    protected string? MyRole() => User.FindFirst("roleCode")?.Value;

    protected async Task<Actor> ActorAsync(IDbConnection c) => new Actor
    {
        Id = MyId(),
        Role = MyRole(),
        Super = string.Equals(MyRole(), "SUPERADMIN", StringComparison.OrdinalIgnoreCase),
        Scope = await DataScope.ForUserAsync(c, User, MyId())
    };

    // permission ticked in Menu Access for a page (matched on menus.route_path); super admin always passes
    protected async Task<bool> MenuPermAsync(IDbConnection c, Actor a, string route, string perm)
    {
        if (a.Super) return true;
        if (perm is not ("can_view" or "can_add" or "can_edit" or "can_delete")) return false;
        var roleId = this.GetAuthUser()!.RoleId;
        return await c.ExecuteScalarAsync<int>($@"SELECT COUNT(*) FROM menus m JOIN role_menu_permissions rmp ON rmp.menu_id = m.id AND rmp.role_id = @roleId
            WHERE m.is_active = 1 AND rmp.{perm} = 1 AND LOWER(m.route_path) = @route", new { roleId, route = route.ToLowerInvariant() }) > 0;
    }

    protected async Task RequireMenuAsync(IDbConnection c, Actor a, string route, string perm, string what)
    {
        if (!await MenuPermAsync(c, a, route, perm)) throw new TrException($"You do not have permission to {what}.", 403);
    }
}