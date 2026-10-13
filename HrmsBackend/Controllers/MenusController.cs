using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/menus")]
public class MenusController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    public MenusController(DbConnectionFactory db) => _db = db;

    private class MenuFlatRow
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string MenuKey { get; set; } = "";
        public string? RoutePath { get; set; }
        public string? Icon { get; set; }
        public int OrderNo { get; set; }
        public string Label { get; set; } = "";
    }

    // Serializes to { id, key, label, path, icon, children } — matching buildTree() in menus.js exactly.
    public class MenuNode
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Path { get; set; }
        public string? Icon { get; set; }
        public List<MenuNode> Children { get; set; } = new();
    }

    private static List<MenuNode> BuildTree(List<MenuFlatRow> flat, int? parentId = null)
    {
        return flat
            .Where(m => m.ParentId == parentId)
            .OrderBy(m => m.OrderNo)
            .Select(m => new MenuNode
            {
                Id = m.Id,
                Key = m.MenuKey,
                Label = m.Label,
                Path = m.RoutePath,
                Icon = m.Icon,
                Children = BuildTree(flat, m.Id)
            })
            .ToList();
    }

    // ---------------------------------------------------------
    // GET /api/menus?lang=en — nav tree for the logged-in account's role
    // ---------------------------------------------------------
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetMenuTree([FromQuery] string? lang)
    {
        var user = this.GetAuthUser()!;
        var langCode = string.IsNullOrEmpty(lang) ? "en" : lang;

        using var conn = _db.CreateConnection();
        try
        {
            var languageId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT id FROM languages WHERE code = @langCode", new { langCode }) ?? 1;

            List<MenuFlatRow> flat;

            if (user.RoleCode == "SUPERADMIN")
            {
                flat = (await conn.QueryAsync<MenuFlatRow>(
                    @"SELECT m.id, m.parent_id, m.menu_key, m.route_path, m.icon, m.order_no,
                             COALESCE(mt.label, m.menu_key) AS label
                      FROM menus m
                      LEFT JOIN menu_translations mt ON mt.menu_id = m.id AND mt.language_id = @languageId
                      WHERE m.is_active = 1
                      ORDER BY m.order_no",
                    new { languageId })).ToList();
            }
            else
            {
                flat = (await conn.QueryAsync<MenuFlatRow>(
                    @"SELECT m.id, m.parent_id, m.menu_key, m.route_path, m.icon, m.order_no,
                             COALESCE(mt.label, m.menu_key) AS label
                      FROM menus m
                      JOIN role_menu_permissions rmp ON rmp.menu_id = m.id AND rmp.role_id = @roleId AND rmp.can_view = 1
                      LEFT JOIN menu_translations mt ON mt.menu_id = m.id AND mt.language_id = @languageId
                      WHERE m.is_active = 1
                      ORDER BY m.order_no",
                    new { roleId = user.RoleId, languageId })).ToList();

                // Pull in ancestor "grouping" nodes so a visible sub-menu still
                // shows its parent even without a direct permission row.
                var haveIds = flat.Select(r => r.Id).ToHashSet();
                var changed = true;
                while (changed)
                {
                    changed = false;
                    var missingParentIds = flat
                        .Where(r => r.ParentId.HasValue && !haveIds.Contains(r.ParentId.Value))
                        .Select(r => r.ParentId!.Value)
                        .Distinct()
                        .ToList();

                    if (missingParentIds.Count == 0) break;

                    var parents = (await conn.QueryAsync<MenuFlatRow>(
                        @"SELECT m.id, m.parent_id, m.menu_key, m.route_path, m.icon, m.order_no,
                                 COALESCE(mt.label, m.menu_key) AS label
                          FROM menus m
                          LEFT JOIN menu_translations mt ON mt.menu_id = m.id AND mt.language_id = @languageId
                          WHERE m.id IN @missingParentIds",
                        new { languageId, missingParentIds })).ToList();

                    foreach (var p in parents) haveIds.Add(p.Id);
                    flat.AddRange(parents);
                    changed = true;
                }
            }

            return Ok(BuildTree(flat));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while loading menu" });
        }
    }

    // ===========================================================
    // SUPERADMIN: MENU BUILDER
    // ===========================================================

    // GET /api/menus/admin/all — flat list for the menu-builder UI
    [HttpGet("admin/all")]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> AdminAll()
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                @"SELECT m.*, GROUP_CONCAT(CONCAT(mt.language_id, ':', mt.label) SEPARATOR '|') AS translations
                  FROM menus m LEFT JOIN menu_translations mt ON mt.menu_id = m.id
                  GROUP BY m.id ORDER BY m.parent_id IS NULL DESC, m.order_no")).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    public record TranslationInput(int LanguageId, string Label);
    public record CreateMenuRequest(
        int? ParentId, string? MenuKey, string? RoutePath, string? Icon, int? OrderNo,
        List<TranslationInput>? Translations);

    // POST /api/menus — create a menu / sub-menu / sub-sub-menu
    [HttpPost]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> CreateMenu([FromBody] CreateMenuRequest? body)
    {
        if (string.IsNullOrEmpty(body?.MenuKey) || body?.Translations == null || body.Translations.Count == 0)
        {
            return BadRequest(new { message = "menuKey and at least one translation are required" });
        }

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            await conn.ExecuteAsync(
                @"INSERT INTO menus (parent_id, menu_key, route_path, icon, order_no)
                  VALUES (@ParentId, @MenuKey, @RoutePath, @Icon, @OrderNo)",
                new { body.ParentId, body.MenuKey, body.RoutePath, body.Icon, OrderNo = body.OrderNo ?? 0 }, tx);

            var menuId = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);

            foreach (var t in body.Translations)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO menu_translations (menu_id, language_id, label) VALUES (@menuId, @LanguageId, @Label)",
                    new { menuId, t.LanguageId, t.Label }, tx);
            }

            // auto-grant SuperAdmin (role_id = 1) full access to the new menu item
            await conn.ExecuteAsync(
                @"INSERT INTO role_menu_permissions (role_id, menu_id, can_view, can_add, can_edit, can_delete)
                  VALUES (1, @menuId, 1, 1, 1, 1)",
                new { menuId }, tx);

            await tx.CommitAsync();
            return StatusCode(201, new { id = menuId, message = "Menu created" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while creating menu" });
        }
    }

    public record UpdateMenuRequest(
        int? ParentId, string? MenuKey, string? RoutePath, string? Icon, int? OrderNo, bool? IsActive,
        List<TranslationInput>? Translations);

    // PUT /api/menus/:id — edit a menu node + its translations
    [HttpPut("{id:int}")]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> UpdateMenu(int id, [FromBody] UpdateMenuRequest? body)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            await conn.ExecuteAsync(
                @"UPDATE menus SET parent_id = @ParentId, menu_key = @MenuKey, route_path = @RoutePath,
                         icon = @Icon, order_no = @OrderNo, is_active = @IsActive
                  WHERE id = @Id",
                new
                {
                    body?.ParentId, body?.MenuKey, body?.RoutePath, body?.Icon,
                    OrderNo = body?.OrderNo ?? 0,
                    IsActive = (body?.IsActive ?? false) ? 1 : 0,
                    Id = id
                }, tx);

            if (body?.Translations != null)
            {
                foreach (var t in body.Translations)
                {
                    await conn.ExecuteAsync(
                        @"INSERT INTO menu_translations (menu_id, language_id, label) VALUES (@Id, @LanguageId, @Label)
                          ON DUPLICATE KEY UPDATE label = @Label",
                        new { Id = id, t.LanguageId, t.Label }, tx);
                }
            }

            await tx.CommitAsync();
            return Ok(new { message = "Menu updated" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while updating menu" });
        }
    }

    // DELETE /api/menus/:id — cascades to children + translations + permissions via FK
    [HttpDelete("{id:int}")]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> DeleteMenu(int id)
    {
        using var conn = _db.CreateConnection();
        try
        {
            await conn.ExecuteAsync("DELETE FROM menus WHERE id = @id", new { id });
            return Ok(new { message = "Menu deleted" });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while deleting menu" });
        }
    }

    // ===========================================================
    // SUPERADMIN: ROLE <-> MENU ACCESS
    // ===========================================================

    // GET /api/menus/admin/access/:roleId
    [HttpGet("admin/access/{roleId:int}")]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> GetAccess(int roleId)
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                @"SELECT m.id AS menu_id, m.menu_key,
                         COALESCE(rmp.can_view, 0) AS can_view,
                         COALESCE(rmp.can_add, 0) AS can_add,
                         COALESCE(rmp.can_edit, 0) AS can_edit,
                         COALESCE(rmp.can_delete, 0) AS can_delete
                  FROM menus m
                  LEFT JOIN role_menu_permissions rmp ON rmp.menu_id = m.id AND rmp.role_id = @roleId
                  ORDER BY m.parent_id IS NULL DESC, m.order_no",
                new { roleId })).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    public record PermissionInput(int MenuId, bool? CanView, bool? CanAdd, bool? CanEdit, bool? CanDelete);
    public record UpdateAccessRequest(List<PermissionInput>? Permissions);

    // PUT /api/menus/admin/access/:roleId
    [HttpPut("admin/access/{roleId:int}")]
    [Authorize]
    [RequireRole("SUPERADMIN")]
    public async Task<IActionResult> UpdateAccess(int roleId, [FromBody] UpdateAccessRequest? body)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            foreach (var p in body?.Permissions ?? new List<PermissionInput>())
            {
                var canView = (p.CanView ?? false) ? 1 : 0;
                var canAdd = (p.CanAdd ?? false) ? 1 : 0;
                var canEdit = (p.CanEdit ?? false) ? 1 : 0;
                var canDelete = (p.CanDelete ?? false) ? 1 : 0;

                await conn.ExecuteAsync(
                    @"INSERT INTO role_menu_permissions (role_id, menu_id, can_view, can_add, can_edit, can_delete)
                      VALUES (@roleId, @menuId, @canView, @canAdd, @canEdit, @canDelete)
                      ON DUPLICATE KEY UPDATE can_view = @canView, can_add = @canAdd, can_edit = @canEdit, can_delete = @canDelete",
                    new { roleId, menuId = p.MenuId, canView, canAdd, canEdit, canDelete }, tx);
            }

            await tx.CommitAsync();
            return Ok(new { message = "Access updated" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while updating access" });
        }
    }


    [HttpGet("my-access")]
    [Authorize]
    public async Task<IActionResult> GetMyAccess()
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        try
        {
            List<Dictionary<string, object?>> rows;

            if (user.RoleCode == "SUPERADMIN")
            {
                rows = (await conn.QueryAsync(
                    @"SELECT id AS menu_id, route_path, menu_key,
                             1 AS can_view, 1 AS can_add, 1 AS can_edit, 1 AS can_delete
                      FROM menus")).ToDictList();
            }
            else
            {
                rows = (await conn.QueryAsync(
                    @"SELECT m.id AS menu_id, m.route_path, m.menu_key,
                             COALESCE(rmp.can_view, 0) AS can_view,
                             COALESCE(rmp.can_add, 0) AS can_add,
                             COALESCE(rmp.can_edit, 0) AS can_edit,
                             COALESCE(rmp.can_delete, 0) AS can_delete
                      FROM menus m
                      LEFT JOIN role_menu_permissions rmp ON rmp.menu_id = m.id AND rmp.role_id = @roleId",
                    new { roleId = user.RoleId })).ToDictList();
            }

            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }
}
