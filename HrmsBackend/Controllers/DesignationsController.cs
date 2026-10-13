using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/designations")]
[Authorize]
public class DesignationsController : ControllerBase
{
    private readonly DbConnectionFactory _db;

    public DesignationsController(DbConnectionFactory db)
    {
        _db = db;
    }

    // =========================================================
    // GET PERMISSIONS
    // =========================================================

    private async Task<dynamic?> GetPermission(
        int roleId,
        IDbConnection conn)
    {
        const string sql = @"
            SELECT
                m.id AS menu_id,
                m.route_path,
                m.menu_key,
                COALESCE(rmp.can_view, 0) AS can_view,
                COALESCE(rmp.can_add, 0) AS can_add,
                COALESCE(rmp.can_edit, 0) AS can_edit,
                COALESCE(rmp.can_delete, 0) AS can_delete
            FROM menus m
            LEFT JOIN role_menu_permissions rmp
                ON rmp.menu_id = m.id
                AND rmp.role_id = @roleId
            WHERE m.menu_key = 'designation'
              AND m.is_active = 1
            LIMIT 1";

        return await conn.QueryFirstOrDefaultAsync(
            sql,
            new { roleId }
        );
    }

    private static bool Allowed(
        dynamic permission,
        string field)
    {
        if (permission == null)
            return false;

        return Convert.ToInt32(
            ((IDictionary<string, object>)permission)[field]
        ) == 1;
    }

    // =========================================================
    // GET ALL DESIGNATIONS
    // VIEW
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> GetDesignations()
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            var permission = await GetPermission(user.RoleId, conn);

            if (!Allowed(permission, "can_view"))
            {
                return StatusCode(403, new
                {
                    message = "You do not have permission to view designations."
                });
            }
            const string sql = @"
                SELECT
                    id,
                    name,
                    des_short,
                    sequence,
                    active,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by
                FROM designations
                ORDER BY sequence ASC, name ASC";

            var data =
                await conn.QueryAsync(sql);

            return Ok(data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while loading designations."
            });
        }
    }

    // =========================================================
    // GET SINGLE DESIGNATION
    // VIEW
    // =========================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDesignation(int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            var permission = await GetPermission(user.RoleId, conn);

            if (!Allowed(permission, "can_view"))
            {
                return StatusCode(403, new
                {
                    message = "You do not have permission to view designations."
                });
            }

            const string sql = @"
                SELECT
                    id,
                    name,
                    des_short,
                    sequence,
                    active,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by
                FROM designations
                WHERE id = @id";

            var designation =
                await conn.QueryFirstOrDefaultAsync(
                    sql,
                    new { id }
                );

            if (designation == null)
            {
                return NotFound(new
                {
                    message = "Designation not found."
                });
            }

            return Ok(designation);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while loading designation."
            });
        }
    }

    // =========================================================
    // CREATE
    // ADD
    // =========================================================

    public record CreateDesignationRequest(
        string? Name,
        string? DesShort,
        int? Sequence
    );

    [HttpPost]
    public async Task<IActionResult> CreateDesignation(
        [FromBody] CreateDesignationRequest? body)
    {
        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        var name = body.Name?.Trim();
        var desShort = body.DesShort?.Trim();

        // -----------------------------------------------------
        // VALIDATION
        // -----------------------------------------------------

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new
            {
                message = "Designation name is required."
            });
        }

        if (name.Length > 100)
        {
            return BadRequest(new
            {
                message =
                    "Designation name cannot exceed 100 characters."
            });
        }

        if (string.IsNullOrWhiteSpace(desShort))
        {
            return BadRequest(new
            {
                message =
                    "Designation short name is required."
            });
        }

        if (desShort.Length > 50)
        {
            return BadRequest(new
            {
                message =
                    "Designation short name cannot exceed 50 characters."
            });
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                desShort,
                @"^[A-Za-z0-9 _-]+$"))
        {
            return BadRequest(new
            {
                message =
                    "Short name contains invalid characters."
            });
        }

        if (body.Sequence.HasValue &&
            body.Sequence.Value < 0)
        {
            return BadRequest(new
            {
                message =
                    "Sequence cannot be negative."
            });
        }

        try
        {
            using var conn = _db.CreateConnection();

            var permission = await GetPermission(user.RoleId, conn);

            if (!Allowed(permission, "can_add"))
            {
                return StatusCode(403, new
                {
                    message = "You do not have permission to add designations."
                });
            }

            // Duplicate designation name
            var duplicateName =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM designations
                    WHERE LOWER(name) = LOWER(@name)
                    LIMIT 1",
                    new { name }
                );

            if (duplicateName.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A designation with this name already exists."
                });
            }

            // Duplicate short name
            var duplicateShort =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM designations
                    WHERE LOWER(des_short) = LOWER(@desShort)
                    LIMIT 1",
                    new { desShort }
                );

            if (duplicateShort.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A designation with this short name already exists."
                });
            }

            const string sql = @"
                INSERT INTO designations
                (
                    name,
                    des_short,
                    sequence,
                    active,
                    created_by
                )
                VALUES
                (
                    @name,
                    @desShort,
                    @sequence,
                    1,
                    @createdBy
                )";

            await conn.ExecuteAsync(sql, new
            {
                name,
                desShort,
                sequence = body.Sequence ?? 0,
                createdBy = user.EmployeeId
            });

            var id =
                await conn.ExecuteScalarAsync<int>(
                    "SELECT LAST_INSERT_ID()"
                );

            return StatusCode(201, new
            {
                id,
                message =
                    "Designation created successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while creating designation."
            });
        }
    }

    // =========================================================
    // UPDATE
    // EDIT
    // =========================================================

    public record UpdateDesignationRequest(
        string? Name,
        string? DesShort,
        int? Sequence
    );

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateDesignation(
        int id,
        [FromBody] UpdateDesignationRequest? body)
    {
        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        var name = body.Name?.Trim();
        var desShort = body.DesShort?.Trim();

        // -----------------------------------------------------
        // VALIDATION
        // -----------------------------------------------------

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new
            {
                message =
                    "Designation name is required."
            });
        }

        if (name.Length > 100)
        {
            return BadRequest(new
            {
                message =
                    "Designation name cannot exceed 100 characters."
            });
        }

        if (string.IsNullOrWhiteSpace(desShort))
        {
            return BadRequest(new
            {
                message =
                    "Designation short name is required."
            });
        }

        if (desShort.Length > 50)
        {
            return BadRequest(new
            {
                message =
                    "Designation short name cannot exceed 50 characters."
            });
        }

        if (body.Sequence.HasValue &&
            body.Sequence.Value < 0)
        {
            return BadRequest(new
            {
                message =
                    "Sequence cannot be negative."
            });
        }

        try
        {
            using var conn = _db.CreateConnection();

            var permission = await GetPermission(user.RoleId, conn);

            if (!Allowed(permission, "can_edit"))
            {
                return StatusCode(403, new
                {
                    message = "You do not have permission to edit designations."
                });
            }

            var exists =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM designations
                    WHERE id = @id",
                    new { id }
                );

            if (!exists.HasValue)
            {
                return NotFound(new
                {
                    message =
                        "Designation not found."
                });
            }

            // Duplicate name excluding current record
            var duplicateName =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM designations
                    WHERE LOWER(name) = LOWER(@name)
                      AND id <> @id
                    LIMIT 1",
                    new
                    {
                        name,
                        id
                    }
                );

            if (duplicateName.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A designation with this name already exists."
                });
            }

            // Duplicate short name
            var duplicateShort =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM designations
                    WHERE LOWER(des_short) = LOWER(@desShort)
                      AND id <> @id
                    LIMIT 1",
                    new
                    {
                        desShort,
                        id
                    }
                );

            if (duplicateShort.HasValue)
            {
                return Conflict(new
                {
                    message =
                        "A designation with this short name already exists."
                });
            }

            const string sql = @"
                UPDATE designations
                SET
                    name = @name,
                    des_short = @desShort,
                    sequence = @sequence,
                    updated_by = @updatedBy
                WHERE id = @id";

            await conn.ExecuteAsync(sql, new
            {
                id,
                name,
                desShort,
                sequence = body.Sequence ?? 0,
                updatedBy = user.EmployeeId
            });

            return Ok(new
            {
                message =
                    "Designation updated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while updating designation."
            });
        }
    }

    // =========================================================
    // DEACTIVATE
    // DELETE PERMISSION
    // =========================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeactivateDesignation(
        int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            var permission = await GetPermission(user.RoleId, conn);

            if (!Allowed(permission, "can_delete"))
            {
                return StatusCode(403, new
                {
                    message = "You do not have permission to deactivate designations."
                });
            }

            var designation =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT
                        id,
                        name,
                        active
                    FROM designations
                    WHERE id = @id",
                    new { id }
                );

            if (designation == null)
            {
                return NotFound(new
                {
                    message =
                        "Designation not found."
                });
            }

            if (Convert.ToInt32(designation.active) == 0)
            {
                return BadRequest(new
                {
                    message =
                        "Designation is already inactive."
                });
            }

            await conn.ExecuteAsync(
                @"
                UPDATE designations
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
                    "Designation deactivated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while deactivating designation."
            });
        }
    }

    // =========================================================
    // ACTIVATE
    // This is NOT DELETE permission.
    // Uses EDIT permission.
    // =========================================================

    [HttpPut("{id:int}/activate")]
    public async Task<IActionResult> ActivateDesignation(
        int id)
    {
        var user = this.GetAuthUser()!;

        using var conn = _db.CreateConnection();

        try
        {
            var permission = await GetPermission(user.RoleId, conn);

            if (!Allowed(permission, "can_edit"))
            {
                return StatusCode(403, new
                {
                    message = "You do not have permission to activate designations."
                });
            }

            var designation =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT id, active
                    FROM designations
                    WHERE id = @id",
                    new { id }
                );

            if (designation == null)
            {
                return NotFound(new
                {
                    message =
                        "Designation not found."
                });
            }

            await conn.ExecuteAsync(
                @"
                UPDATE designations
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
                    "Designation activated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message =
                    "Server error while activating designation."
            });
        }
    }
}