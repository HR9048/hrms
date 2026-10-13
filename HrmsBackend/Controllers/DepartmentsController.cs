using System.Data;
using Dapper;

using HrmsBackend.Common;
using HrmsBackend.Data;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly DbConnectionFactory _db;

    public DepartmentsController(DbConnectionFactory db)
    {
        _db = db;
    }

    // =========================================================
    // Helper: Get department permissions
    // =========================================================

    private async Task<Dictionary<string, int>> GetPermissions(
        int roleId,
        IDbConnection conn)
    {
        const string sql = @"
            SELECT
                COALESCE(rmp.can_view, 0) AS can_view,
                COALESCE(rmp.can_add, 0) AS can_add,
                COALESCE(rmp.can_edit, 0) AS can_edit,
                COALESCE(rmp.can_delete, 0) AS can_delete
            FROM menus m
            LEFT JOIN role_menu_permissions rmp
                ON rmp.menu_id = m.id
                AND rmp.role_id = @roleId
            WHERE m.menu_key = 'department'
              AND m.is_active = 1
            LIMIT 1";

        var row = await conn.QueryFirstOrDefaultAsync(
            sql,
            new { roleId }
        );

        if (row == null)
        {
            return new Dictionary<string, int>
            {
                ["can_view"] = 0,
                ["can_add"] = 0,
                ["can_edit"] = 0,
                ["can_delete"] = 0
            };
        }

        return new Dictionary<string, int>
        {
            ["can_view"] = Convert.ToInt32(row.can_view),
            ["can_add"] = Convert.ToInt32(row.can_add),
            ["can_edit"] = Convert.ToInt32(row.can_edit),
            ["can_delete"] = Convert.ToInt32(row.can_delete)
        };
    }

    private static bool HasPermission(
        Dictionary<string, int> permissions,
        string permission)
    {
        return permissions.TryGetValue(permission, out var value)
               && value == 1;
    }

    // =========================================================
    // GET /api/departments
    // VIEW
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> GetDepartments(){
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        try{
            var permissions = await GetPermissions(user.RoleId, conn);

            if (!HasPermission(permissions, "can_view")){
                return StatusCode(403, new { message ="You do not have permission to view departments."});
            }

            const string sql = @"
                SELECT
                    id,
                    dep_name,
                    dep_short,
                    sequence,
                    active,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by
                FROM departments
                ORDER BY sequence ASC, dep_name ASC";

            var rows = await conn.QueryAsync(sql);
            return Ok(rows);
        }
        catch (Exception ex){
            Console.Error.WriteLine(ex);
            return StatusCode(500, new{message ="Server error while loading departments."});
        }
    }

    // =========================================================
    // GET /api/departments/{id}
    // VIEW
    // =========================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDepartment(int id)
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();

        try{
            var permissions = await GetPermissions(user.RoleId, conn);

            if (!HasPermission(permissions, "can_view")){
                return StatusCode(403, new{message ="You do not have permission to view departments."});
            }

            const string sql = @"
                SELECT
                    id,
                    dep_name,
                    dep_short,
                    sequence,
                    active,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by
                FROM departments
                WHERE id = @id";

            var department = await conn.QueryFirstOrDefaultAsync(sql, new { id });

            if (department == null){
                return NotFound(new{ message = "Department not found."});
            }
            return Ok(department);
        }
        catch (Exception ex){
            Console.Error.WriteLine(ex);
            return StatusCode(500, new {message ="Server error while loading department."});
        }
    }

    // =========================================================
    // POST /api/departments
    // ADD
    // =========================================================

    public record CreateDepartmentRequest(string? DepName, string? DepShort, int? Sequence);

    [HttpPost]
    public async Task<IActionResult> CreateDepartment(
        [FromBody] CreateDepartmentRequest? body)
    {
        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        var depName = body.DepName?.Trim();
        var depShort = body.DepShort?.Trim();

        // ---------------------------------------------------------
        // VALIDATION
        // ---------------------------------------------------------

        if (string.IsNullOrWhiteSpace(depName))
        {
            return BadRequest(new
            {
                message = "Department name is required."
            });
        }

        if (depName.Length > 100)
        {
            return BadRequest(new
            {
                message =
                    "Department name cannot exceed 100 characters."
            });
        }

        if (string.IsNullOrWhiteSpace(depShort))
        {
            return BadRequest(new
            {
                message =
                    "Department short name is required."
            });
        }

        if (depShort.Length > 50)
        {
            return BadRequest(new
            {
                message =
                    "Department short name cannot exceed 50 characters."
            });
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                depShort,
                @"^[A-Za-z0-9 _-]+$"))
        {
            return BadRequest(new
            {
                message =
                    "Department short name can contain only letters, numbers, spaces, hyphens and underscores."
            });
        }

        if (body.Sequence.HasValue &&
            body.Sequence.Value < 0)
        {
            return BadRequest(new
            {
                message = "Sequence cannot be negative."
            });
        }

        using var conn = _db.CreateConnection();

        try
        {
            var permissions =
                await GetPermissions(user.RoleId, conn);

            if (!HasPermission(permissions, "can_add"))
            {
                return StatusCode(403, new
                {
                    message =
                        "You do not have permission to add departments."
                });
            }

            // -----------------------------------------------------
            // Duplicate name
            // -----------------------------------------------------

            var existingName =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM departments
                    WHERE LOWER(dep_name) = LOWER(@depName)
                    LIMIT 1",
                    new { depName }
                );

            if (existingName.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A department with this name already exists."
                });
            }

            // -----------------------------------------------------
            // Duplicate short name
            // -----------------------------------------------------

            var existingShort =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM departments
                    WHERE LOWER(dep_short) = LOWER(@depShort)
                    LIMIT 1",
                    new { depShort }
                );

            if (existingShort.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A department with this short name already exists."
                });
            }

            const string sql = @"
                INSERT INTO departments
                (
                    dep_name,
                    dep_short,
                    sequence,
                    active,
                    created_by
                )
                VALUES
                (
                    @depName,
                    @depShort,
                    @sequence,
                    1,
                    @createdBy
                )";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    depName,
                    depShort,
                    sequence = body.Sequence ?? 0,
                    createdBy = user.EmployeeId
                }
            );

            var id =
                await conn.ExecuteScalarAsync<int>(
                    "SELECT LAST_INSERT_ID()"
                );

            return StatusCode(201, new
            {
                id,
                message =
                    "Department created successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while creating department."
            });
        }
    }

    // =========================================================
    // PUT /api/departments/{id}
    // EDIT
    // =========================================================

    public record UpdateDepartmentRequest(
        string? DepName,
        string? DepShort,
        int? Sequence
    );

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateDepartment(
        int id,
        [FromBody] UpdateDepartmentRequest? body)
    {
        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        var depName = body.DepName?.Trim();
        var depShort = body.DepShort?.Trim();

        // ---------------------------------------------------------
        // VALIDATION
        // ---------------------------------------------------------

        if (string.IsNullOrWhiteSpace(depName))
        {
            return BadRequest(new
            {
                message = "Department name is required."
            });
        }

        if (depName.Length > 100)
        {
            return BadRequest(new
            {
                message =
                    "Department name cannot exceed 100 characters."
            });
        }

        if (string.IsNullOrWhiteSpace(depShort))
        {
            return BadRequest(new
            {
                message =
                    "Department short name is required."
            });
        }

        if (depShort.Length > 50)
        {
            return BadRequest(new
            {
                message =
                    "Department short name cannot exceed 50 characters."
            });
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                depShort,
                @"^[A-Za-z0-9 _-]+$"))
        {
            return BadRequest(new
            {
                message =
                    "Department short name can contain only letters, numbers, spaces, hyphens and underscores."
            });
        }

        if (body.Sequence.HasValue &&
            body.Sequence.Value < 0)
        {
            return BadRequest(new
            {
                message = "Sequence cannot be negative."
            });
        }

        using var conn = _db.CreateConnection();

        try
        {
            var permissions =
                await GetPermissions(user.RoleId, conn);

            if (!HasPermission(permissions, "can_edit"))
            {
                return StatusCode(403, new
                {
                    message =
                        "You do not have permission to edit departments."
                });
            }

            var department =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT id
                    FROM departments
                    WHERE id = @id",
                    new { id }
                );

            if (department == null)
            {
                return NotFound(new
                {
                    message = "Department not found."
                });
            }

            // -----------------------------------------------------
            // Duplicate name
            // -----------------------------------------------------

            var duplicateName =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM departments
                    WHERE LOWER(dep_name) = LOWER(@depName)
                      AND id <> @id
                    LIMIT 1",
                    new
                    {
                        depName,
                        id
                    }
                );

            if (duplicateName.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A department with this name already exists."
                });
            }

            // -----------------------------------------------------
            // Duplicate short name
            // -----------------------------------------------------

            var duplicateShort =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM departments
                    WHERE LOWER(dep_short) = LOWER(@depShort)
                      AND id <> @id
                    LIMIT 1",
                    new
                    {
                        depShort,
                        id
                    }
                );

            if (duplicateShort.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A department with this short name already exists."
                });
            }

            const string sql = @"
                UPDATE departments
                SET
                    dep_name = @depName,
                    dep_short = @depShort,
                    sequence = @sequence,
                    updated_by = @updatedBy
                WHERE id = @id";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    id,
                    depName,
                    depShort,
                    sequence = body.Sequence ?? 0,
                    updatedBy = user.EmployeeId
                }
            );

            return Ok(new
            {
                message =
                    "Department updated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while updating department."
            });
        }
    }

    // =========================================================
    // DELETE /api/departments/{id}
    // DEACTIVATE
    // =========================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDepartment(int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            var permissions =
                await GetPermissions(user.RoleId, conn);

            if (!HasPermission(permissions, "can_delete"))
            {
                return StatusCode(403, new
                {
                    message =
                        "You do not have permission to deactivate departments."
                });
            }

            var department =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT id, dep_name, active
                    FROM departments
                    WHERE id = @id",
                    new { id }
                );

            if (department == null)
            {
                return NotFound(new
                {
                    message = "Department not found."
                });
            }

            if (Convert.ToInt32(department.active) == 0)
            {
                return BadRequest(new
                {
                    message = "Department is already inactive."
                });
            }

            await conn.ExecuteAsync(
                @"
                UPDATE departments
                SET
                    active = 0,
                    updated_by = @updatedBy
                WHERE id = @id",
                new
                {
                    id,
                    updatedBy = user.EmployeeId
                }
            );

            return Ok(new
            {
                message =
                    "Department deactivated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while deactivating department."
            });
        }
    }

    // =========================================================
    // PUT /api/departments/{id}/activate
    // ACTIVATE
    // =========================================================

    [HttpPut("{id:int}/activate")]
    public async Task<IActionResult> ActivateDepartment(int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            var permissions =
                await GetPermissions(user.RoleId, conn);

            if (!HasPermission(permissions, "can_edit"))
            {
                return StatusCode(403, new
                {
                    message =
                        "You do not have permission to activate departments."
                });
            }

            var department =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT id, dep_name, active
                    FROM departments
                    WHERE id = @id",
                    new { id }
                );

            if (department == null)
            {
                return NotFound(new
                {
                    message = "Department not found."
                });
            }

            if (Convert.ToInt32(department.active) == 1)
            {
                return BadRequest(new
                {
                    message = "Department is already active."
                });
            }

            await conn.ExecuteAsync(
                @"
                UPDATE departments
                SET
                    active = 1,
                    updated_by = @updatedBy
                WHERE id = @id",
                new
                {
                    id,
                    updatedBy = user.EmployeeId
                }
            );

            return Ok(new
            {
                message =
                    "Department activated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while activating department."
            });
        }
    }
}