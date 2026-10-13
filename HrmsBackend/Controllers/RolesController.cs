using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly DbConnectionFactory _db;

    public RolesController(DbConnectionFactory db)
    {
        _db = db;
    }

    // =========================================================
    // SUPERADMIN CHECK
    // =========================================================

    private bool IsSuperAdmin()
    {
        var user = this.GetAuthUser();

        return user != null &&
               string.Equals(
                   user.RoleCode,
                   "SUPERADMIN",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private IActionResult AccessDenied()
    {
        return StatusCode(403, new
        {
            message = "Only SUPERADMIN can manage roles."
        });
    }


    // =========================================================
    // GET ALL ROLES
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        if (!IsSuperAdmin())
            return AccessDenied();

        try
        {
            using var conn = _db.CreateConnection();

            const string sql = @"
                SELECT
                    id,
                    role_code,
                    role_name,
                    description,
                    sequence,
                    active,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by
                FROM roles
                ORDER BY sequence ASC, role_name ASC";

            var data = await conn.QueryAsync(sql);

            return Ok(data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message = "Server error while loading roles."
            });
        }
    }


    // =========================================================
    // GET SINGLE ROLE
    // =========================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetRole(int id)
    {
        if (!IsSuperAdmin())
            return AccessDenied();

        try
        {
            using var conn = _db.CreateConnection();

            const string sql = @"
                SELECT
                    id,
                    role_code,
                    role_name,
                    description,
                    sequence,
                    active,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by
                FROM roles
                WHERE id = @id";

            var role = await conn.QueryFirstOrDefaultAsync(
                sql,
                new { id }
            );

            if (role == null)
            {
                return NotFound(new
                {
                    message = "Role not found."
                });
            }

            return Ok(role);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message = "Server error while loading role."
            });
        }
    }


    // =========================================================
    // CREATE ROLE
    // =========================================================

    public record CreateRoleRequest(
        string? RoleCode,
        string? RoleName,
        string? Description,
        int? Sequence
    );


    [HttpPost]
    public async Task<IActionResult> CreateRole(
        [FromBody] CreateRoleRequest? body)
    {
        if (!IsSuperAdmin())
            return AccessDenied();

        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        var roleCode = body.RoleCode?.Trim();
        var roleName = body.RoleName?.Trim();
        var description = body.Description?.Trim();

        // =====================================================
        // VALIDATION
        // =====================================================

        if (string.IsNullOrWhiteSpace(roleCode))
        {
            return BadRequest(new
            {
                message = "Role code is required."
            });
        }

        if (roleCode.Length > 50)
        {
            return BadRequest(new
            {
                message = "Role code cannot exceed 50 characters."
            });
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                roleCode,
                @"^[A-Za-z0-9_-]+$"))
        {
            return BadRequest(new
            {
                message =
                    "Role code can contain only letters, numbers, underscore and hyphen."
            });
        }

        if (string.IsNullOrWhiteSpace(roleName))
        {
            return BadRequest(new
            {
                message = "Role name is required."
            });
        }

        if (roleName.Length > 100)
        {
            return BadRequest(new
            {
                message = "Role name cannot exceed 100 characters."
            });
        }

        if (description != null && description.Length > 255)
        {
            return BadRequest(new
            {
                message = "Description cannot exceed 255 characters."
            });
        }

        if (body.Sequence.HasValue && body.Sequence.Value < 0)
        {
            return BadRequest(new
            {
                message = "Sequence cannot be negative."
            });
        }

        try
        {
            using var conn = _db.CreateConnection();

            // =================================================
            // DUPLICATE ROLE CODE
            // =================================================

            var duplicateCode =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM roles
                    WHERE LOWER(role_code) = LOWER(@roleCode)
                    LIMIT 1",
                    new { roleCode }
                );

            if (duplicateCode.HasValue)
            {
                return Conflict(new
                {
                    message = "A role with this role code already exists."
                });
            }


            // =================================================
            // DUPLICATE ROLE NAME
            // =================================================

            var duplicateName =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM roles
                    WHERE LOWER(role_name) = LOWER(@roleName)
                    LIMIT 1",
                    new { roleName }
                );

            if (duplicateName.HasValue)
            {
                return Conflict(new
                {
                    message = "A role with this role name already exists."
                });
            }


            // =================================================
            // INSERT
            // =================================================

            const string sql = @"
                INSERT INTO roles
                (
                    role_code,
                    role_name,
                    description,
                    sequence,
                    active,
                    created_by
                )
                VALUES
                (
                    @roleCode,
                    @roleName,
                    @description,
                    @sequence,
                    1,
                    @createdBy
                )";

            await conn.ExecuteAsync(sql, new
            {
                roleCode,
                roleName,
                description,
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
                message = "Role created successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message = "Server error while creating role."
            });
        }
    }


    // =========================================================
    // UPDATE ROLE
    // =========================================================

    public record UpdateRoleRequest(
        string? RoleCode,
        string? RoleName,
        string? Description,
        int? Sequence
    );


    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateRole(
        int id,
        [FromBody] UpdateRoleRequest? body)
    {
        if (!IsSuperAdmin())
            return AccessDenied();

        var user = this.GetAuthUser()!;

        if (body == null)
        {
            return BadRequest(new
            {
                message = "Request data is required."
            });
        }

        var roleCode = body.RoleCode?.Trim();
        var roleName = body.RoleName?.Trim();
        var description = body.Description?.Trim();

        // =====================================================
        // VALIDATION
        // =====================================================

        if (string.IsNullOrWhiteSpace(roleCode))
        {
            return BadRequest(new
            {
                message = "Role code is required."
            });
        }

        if (roleCode.Length > 50)
        {
            return BadRequest(new
            {
                message = "Role code cannot exceed 50 characters."
            });
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                roleCode,
                @"^[A-Za-z0-9_-]+$"))
        {
            return BadRequest(new
            {
                message =
                    "Role code can contain only letters, numbers, underscore and hyphen."
            });
        }

        if (string.IsNullOrWhiteSpace(roleName))
        {
            return BadRequest(new
            {
                message = "Role name is required."
            });
        }

        if (roleName.Length > 100)
        {
            return BadRequest(new
            {
                message = "Role name cannot exceed 100 characters."
            });
        }

        if (description != null && description.Length > 255)
        {
            return BadRequest(new
            {
                message = "Description cannot exceed 255 characters."
            });
        }

        if (body.Sequence.HasValue && body.Sequence.Value < 0)
        {
            return BadRequest(new
            {
                message = "Sequence cannot be negative."
            });
        }

        try
        {
            using var conn = _db.CreateConnection();

            // =================================================
            // CHECK ROLE EXISTS
            // =================================================

            var exists =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM roles
                    WHERE id = @id",
                    new { id }
                );

            if (!exists.HasValue)
            {
                return NotFound(new
                {
                    message = "Role not found."
                });
            }


            // =================================================
            // DUPLICATE ROLE CODE
            // =================================================

            var duplicateCode =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM roles
                    WHERE LOWER(role_code) = LOWER(@roleCode)
                      AND id <> @id
                    LIMIT 1",
                    new
                    {
                        roleCode,
                        id
                    }
                );

            if (duplicateCode.HasValue)
            {
                return Conflict(new
                {
                    message = "A role with this role code already exists."
                });
            }


            // =================================================
            // DUPLICATE ROLE NAME
            // =================================================

            var duplicateName =
                await conn.ExecuteScalarAsync<int?>(
                    @"
                    SELECT id
                    FROM roles
                    WHERE LOWER(role_name) = LOWER(@roleName)
                      AND id <> @id
                    LIMIT 1",
                    new
                    {
                        roleName,
                        id
                    }
                );

            if (duplicateName.HasValue)
            {
                return Conflict(new
                {
                    message = "A role with this role name already exists."
                });
            }


            // =================================================
            // UPDATE
            // =================================================

            const string sql = @"
                UPDATE roles
                SET
                    role_code = @roleCode,
                    role_name = @roleName,
                    description = @description,
                    sequence = @sequence,
                    updated_by = @updatedBy
                WHERE id = @id";

            await conn.ExecuteAsync(sql, new
            {
                id,
                roleCode,
                roleName,
                description,
                sequence = body.Sequence ?? 0,
                updatedBy = user.EmployeeId
            });

            return Ok(new
            {
                message = "Role updated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message = "Server error while updating role."
            });
        }
    }


    // =========================================================
    // DEACTIVATE ROLE
    // DELETE = DEACTIVATE
    // =========================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeactivateRole(int id)
    {
        if (!IsSuperAdmin())
            return AccessDenied();

        var user = this.GetAuthUser()!;

        try
        {
            using var conn = _db.CreateConnection();

            var role =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT
                        id,
                        role_code,
                        role_name,
                        active
                    FROM roles
                    WHERE id = @id",
                    new { id }
                );

            if (role == null)
            {
                return NotFound(new
                {
                    message = "Role not found."
                });
            }

            if (Convert.ToInt32(role.active) == 0)
            {
                return BadRequest(new
                {
                    message = "Role is already inactive."
                });
            }

            // Prevent SUPERADMIN role from being deactivated
            if (string.Equals(
                    Convert.ToString(role.role_code),
                    "SUPERADMIN",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message = "SUPERADMIN role cannot be deactivated."
                });
            }

            await conn.ExecuteAsync(
                @"
                UPDATE roles
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
                message = "Role deactivated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message = "Server error while deactivating role."
            });
        }
    }


    // =========================================================
    // ACTIVATE ROLE
    // =========================================================

    [HttpPut("{id:int}/activate")]
    public async Task<IActionResult> ActivateRole(int id)
    {
        if (!IsSuperAdmin())
            return AccessDenied();

        var user = this.GetAuthUser()!;

        try
        {
            using var conn = _db.CreateConnection();

            var role =
                await conn.QueryFirstOrDefaultAsync(
                    @"
                    SELECT
                        id,
                        role_code,
                        role_name,
                        active
                    FROM roles
                    WHERE id = @id",
                    new { id }
                );

            if (role == null)
            {
                return NotFound(new
                {
                    message = "Role not found."
                });
            }

            if (Convert.ToInt32(role.active) == 1)
            {
                return BadRequest(new
                {
                    message = "Role is already active."
                });
            }

            await conn.ExecuteAsync(
                @"
                UPDATE roles
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
                message = "Role activated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            return StatusCode(500, new
            {
                message = "Server error while activating role."
            });
        }
    }
}