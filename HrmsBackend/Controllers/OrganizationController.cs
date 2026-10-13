using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HrmsBackend.Controllers;

// Organization Creation + Dynamic Organization Hierarchy module.
// Same shape as AdminController: everything here is SuperAdmin-only, so the
// [Authorize] + [RequireRole] gate is applied once at the class level.
//
// ASSUMPTION: this HRMS is single-tenant (one organization per deployment),
// matching the "first deployment organization setup" flow in the spec. All
// node-type/node/history endpoints operate against "the" organization (the
// first/only row in master_organizations) rather than taking an
// organizationId from the client. If this ever needs to become multi-tenant,
// GetPrimaryOrganizationId() below is the one place to change.
[ApiController]
[Route("api/organization")]
[Authorize]
public class OrganizationController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    public OrganizationController(DbConnectionFactory db) => _db = db;

    // -----------------------------------------------------------
    // Shared helpers (kept private to this controller, same style
    // as SystemController's own GetSetting/SetSetting)
    // -----------------------------------------------------------
    private async Task<string?> GetSetting(string key)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT setting_value FROM system_settings WHERE setting_key = @key", new { key });
    }

    private async Task SetSetting(string key, string value)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO system_settings (setting_key, setting_value) VALUES (@key, @value) " +
            "ON DUPLICATE KEY UPDATE setting_value = @value",
            new { key, value });
    }

    // ===========================================================
    // ORGANIZATION
    // ===========================================================

    public record CreateOrganizationRequest(
        string? OrganizationName, string? OrganizationCode, string? Logo,
        string? Address, string? ContactNumber, string? Email);

    public record UpdateOrganizationRequest(
        string? OrganizationName, string? OrganizationCode, string? Logo,
        string? Address, string? ContactNumber, string? Email, string? Status);

    // GET /api/organization — the (single) organization record
    [HttpGet]
    public async Task<IActionResult> GetOrganization()
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                "SELECT * FROM master_organizations ORDER BY organization_id ASC LIMIT 1")).ToDictList();

            if (rows.Count == 0)
            {
                return NotFound(new { message = "Organization has not been set up yet." });
            }
            return Ok(rows[0]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    // POST /api/organization — one-time creation (mirrors first-deploy-language:
    // blocked once organization_setup_done = 1, and double-checked against an
    // existing row so we can never end up with duplicate organizations).
    [HttpPost]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest? body)
    {
        if (string.IsNullOrEmpty(body?.OrganizationName) || string.IsNullOrEmpty(body?.OrganizationCode))
        {
            return BadRequest(new { message = "organizationName and organizationCode are required" });
        }

        var setupDone = (await GetSetting("organization_setup_done")) == "1";
        if (setupDone)
        {
            return StatusCode(409, new { message = "Organization has already been set up." });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            // belt-and-suspenders: block a duplicate even if the setting was
            // somehow missed (e.g. manual DB edits during setup)
            var existing = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM master_organizations", transaction: tx);
            if (existing > 0)
            {
                await tx.RollbackAsync();
                return StatusCode(409, new { message = "Organization has already been set up." });
            }

            await conn.ExecuteAsync(
                @"INSERT INTO master_organizations
                  (organization_code, organization_name, logo, address, contact_number, email, status, created_by)
                  VALUES (@OrganizationCode, @OrganizationName, @Logo, @Address, @ContactNumber, @Email, 'ACTIVE', @CreatedBy)",
                new
                {
                    body.OrganizationCode, body.OrganizationName, body.Logo, body.Address,
                    body.ContactNumber, body.Email, CreatedBy = user.EmployeeId
                }, tx);

            var organizationId = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);

            // Root node: the hierarchy needs a top anchor for the first real
            // nodes (regions/depots/...) to attach under, so we create a
            // "Organization Root" node type + the root node itself here.
            await conn.ExecuteAsync(
                @"INSERT INTO master_organization_node_types
                  (organization_id, type_name, type_code, description, display_order, status, created_by)
                  VALUES (@organizationId, 'Organization Root', 'ROOT', 'Auto-created root node type', 0, 'ACTIVE', @CreatedBy)",
                new { organizationId, CreatedBy = user.EmployeeId }, tx);

            var rootNodeTypeId = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);

            await conn.ExecuteAsync(
                @"INSERT INTO master_organization_nodes
                  (organization_id, node_type_id, parent_node_id, node_name, node_code, display_order, status, created_by)
                  VALUES (@organizationId, @rootNodeTypeId, NULL, @nodeName, @nodeCode, 0, 'ACTIVE', @CreatedBy)",
                new
                {
                    organizationId, rootNodeTypeId,
                    nodeName = body.OrganizationName, nodeCode = body.OrganizationCode,
                    CreatedBy = user.EmployeeId
                }, tx);

            var rootNodeId = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);

            await conn.ExecuteAsync(
                @"INSERT INTO master_organization_node_history
                  (node_id, organization_id, new_node_name, action_type, changed_by, remarks)
                  VALUES (@rootNodeId, @organizationId, @nodeName, 'CREATE', @ChangedBy, 'Root node created during organization setup')",
                new { rootNodeId, organizationId, nodeName = body.OrganizationName, ChangedBy = user.EmployeeId }, tx);

            // Seed the lock row (UNLOCKED) so the organization, its node
            // types, and its hierarchy nodes can be locked later.
            await conn.ExecuteAsync(
                @"INSERT INTO master_organization_lock (organization_id, status, created_by)
                  VALUES (@organizationId, 'UNLOCKED', @CreatedBy)",
                new { organizationId, CreatedBy = user.EmployeeId }, tx);

            await tx.CommitAsync();

            await SetSetting("organization_setup_done", "1");

            return StatusCode(201, new { organizationId, rootNodeId, message = "Organization created successfully" });
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode == 1062)
        {
            await tx.RollbackAsync();
            return Conflict(new { message = "Organization code already exists" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while creating organization" });
        }
    }

    // PUT /api/organization/:id — edit organization details (blocked while
    // the organization is LOCKED or EDIT_REQUESTED - same lock concept
    // that also covers node types and hierarchy nodes below)
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateOrganization(int id, [FromBody] UpdateOrganizationRequest? body)
    {
        if (string.IsNullOrEmpty(body?.OrganizationName) || string.IsNullOrEmpty(body?.OrganizationCode))
        {
            return BadRequest(new { message = "organizationName and organizationCode are required" });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        try
        {
            var lockStatus = await GetLockStatusAsync(conn, id);
            if (!IsEditableStatus(lockStatus))
            {
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            var affected = await conn.ExecuteAsync(
                @"UPDATE master_organizations
                  SET organization_code = @OrganizationCode, organization_name = @OrganizationName,
                      logo = @Logo, address = @Address, contact_number = @ContactNumber, email = @Email,
                      status = @Status, updated_by = @UpdatedBy
                  WHERE organization_id = @Id",
                new
                {
                    body.OrganizationCode, body.OrganizationName, body.Logo, body.Address,
                    body.ContactNumber, body.Email, Status = body.Status ?? "ACTIVE",
                    UpdatedBy = user.EmployeeId, Id = id
                });

            if (affected == 0)
            {
                return NotFound(new { message = "Organization not found" });
            }

            // Editing while APPROVED_EDIT consumes the permission and
            // re-locks automatically, same as the data-flow hierarchy does.
            if (lockStatus == "APPROVED_EDIT")
            {
                await ConsumeApprovedEditAsync(conn, id, user.EmployeeId);
                return Ok(new { message = "Organization updated. Edit permission used and organization locked again." });
            }

            return Ok(new { message = "Organization updated" });
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode == 1062)
        {
            return Conflict(new { message = "Organization code already exists" });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while updating organization" });
        }
    }

    // =========================================================
    // ORGANIZATION LOCK + EDIT REQUEST
    // One lock covers the whole module: while LOCKED or EDIT_REQUESTED,
    // organization details, node types, AND hierarchy nodes are all
    // blocked from editing below. Only an approved edit request (or
    // being UNLOCKED, before the first lock) allows writes.
    // =========================================================

    private async Task<string> GetLockStatusAsync(MySqlConnection conn, int organizationId, MySqlTransaction? tx = null)
    {
        var status = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT status FROM master_organization_lock WHERE organization_id = @organizationId",
            new { organizationId }, tx);
        return status ?? "UNLOCKED";
    }

    private static bool IsEditableStatus(string status) => status is "UNLOCKED" or "APPROVED_EDIT";

    // Closes out whichever approved request granted the current
    // APPROVED_EDIT session and puts the lock back to LOCKED. Called
    // both when the user explicitly re-locks and when they make an
    // edit while under an approved permission.
    private async Task ConsumeApprovedEditAsync(MySqlConnection conn, int organizationId, int userId)
    {
        var lockRow = await conn.QueryFirstOrDefaultAsync(
            "SELECT id FROM master_organization_lock WHERE organization_id = @organizationId",
            new { organizationId });
        if (lockRow == null) return;

        long lockId = (long)lockRow.id;

        await conn.ExecuteAsync(
            @"UPDATE master_organization_edit_request
              SET edit_used = 1, status = 'USED', updated_at = NOW()
              WHERE lock_id = @lockId AND status = 'APPROVED' AND edit_used = 0",
            new { lockId });

        await conn.ExecuteAsync(
            @"UPDATE master_organization_lock
              SET status = 'LOCKED', locked_by = @UserId, locked_at = NOW(), updated_by = @UserId, updated_at = NOW()
              WHERE id = @lockId",
            new { lockId, UserId = userId });
    }

    public record OrgEditRequestCreateDto(int JobTitleId, int RequestedToUserId, string? Reason);

    // GET /api/organization/lock — current lock status
    [HttpGet("lock")]
    public async Task<IActionResult> GetOrganizationLock()
    {
        using var conn = _db.CreateConnection();
        try
        {
            var organizationId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT organization_id FROM master_organizations ORDER BY organization_id ASC LIMIT 1");
            if (organizationId == null) return Ok(new { status = "UNLOCKED" });

            var status = await GetLockStatusAsync(conn, organizationId.Value);
            return Ok(new { status });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    // POST /api/organization/lock — lock everything (details, node types, nodes)
    [HttpPost("lock")]
    public async Task<IActionResult> LockOrganization()
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var organizationId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT organization_id FROM master_organizations ORDER BY organization_id ASC LIMIT 1", transaction: tx);
            if (organizationId == null)
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Organization has not been set up yet." });
            }

            var lockRow = await conn.QueryFirstOrDefaultAsync(
                "SELECT id, status FROM master_organization_lock WHERE organization_id = @organizationId FOR UPDATE",
                new { organizationId }, tx);
            if (lockRow == null)
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Lock record not found for this organization." });
            }

            string status = lockRow.status;
            if (!IsEditableStatus(status))
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Organization cannot be locked in its current state." });
            }

            if (status == "APPROVED_EDIT")
            {
                await conn.ExecuteAsync(
                    @"UPDATE master_organization_edit_request
                      SET edit_used = 1, status = 'USED', updated_at = NOW()
                      WHERE lock_id = @lockId AND status = 'APPROVED' AND edit_used = 0",
                    new { lockId = (long)lockRow.id }, tx);
            }

            await conn.ExecuteAsync(
                @"UPDATE master_organization_lock
                  SET status = 'LOCKED', locked_by = @UserId, locked_at = NOW(), updated_by = @UserId, updated_at = NOW()
                  WHERE id = @lockId",
                new { lockId = (long)lockRow.id, UserId = user.EmployeeId }, tx);

            await tx.CommitAsync();
            return Ok(new { message = "Organization locked successfully." });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while locking organization." });
        }
    }

    // POST /api/organization/edit-request — only allowed while LOCKED
    [HttpPost("edit-request")]
    public async Task<IActionResult> CreateOrganizationEditRequest([FromBody] OrgEditRequestCreateDto request)
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var organizationId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT organization_id FROM master_organizations ORDER BY organization_id ASC LIMIT 1", transaction: tx);
            if (organizationId == null)
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Organization has not been set up yet." });
            }

            var lockRow = await conn.QueryFirstOrDefaultAsync(
                "SELECT id, status FROM master_organization_lock WHERE organization_id = @organizationId FOR UPDATE",
                new { organizationId }, tx);

            if (lockRow == null || (string)lockRow.status != "LOCKED")
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Edit request can only be created when the organization is locked." });
            }

            var pending = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM master_organization_edit_request
                  WHERE lock_id = @lockId AND requested_by = @userId AND status = 'PENDING'",
                new { lockId = (long)lockRow.id, userId = user.EmployeeId }, tx);
            if (pending > 0)
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "You already have a pending edit request." });
            }

            await conn.ExecuteAsync(
                @"INSERT INTO master_organization_edit_request
                  (lock_id, requested_by, requested_to, requested_job_title_id, reason, status, edit_used, created_at)
                  VALUES (@lockId, @requestedBy, @requestedTo, @jobTitleId, @reason, 'PENDING', 0, NOW())",
                new
                {
                    lockId = (long)lockRow.id, requestedBy = user.EmployeeId, requestedTo = request.RequestedToUserId,
                    jobTitleId = request.JobTitleId, reason = request.Reason
                }, tx);

            var requestId = await conn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()", transaction: tx);

            await conn.ExecuteAsync(
                @"UPDATE master_organization_lock
                  SET status = 'EDIT_REQUESTED', updated_by = @UserId, updated_at = NOW()
                  WHERE id = @lockId",
                new { lockId = (long)lockRow.id, UserId = user.EmployeeId }, tx);

            await tx.CommitAsync();
            return Ok(new { message = "Edit request sent successfully.", requestId });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while creating edit request." });
        }
    }

    // GET /api/organization/edit-requests — pending requests sent TO the current user
    [HttpGet("edit-requests")]
    public async Task<IActionResult> GetOrganizationPendingRequests()
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();

        var rows = await conn.QueryAsync(
            @"SELECT
                r.id, r.requested_by, CONCAT(COALESCE(rb.first_name,''),' ',COALESCE(rb.last_name,'')) AS requested_by_name,
                r.requested_to, r.requested_job_title_id, jt.role_name AS job_title_name,
                r.reason, r.status, r.edit_used, r.created_at
              FROM master_organization_edit_request r
              LEFT JOIN employee_master rb ON rb.employee_id = r.requested_by
              LEFT JOIN roles jt ON jt.id = r.requested_job_title_id
              WHERE r.requested_to = @userId AND r.status = 'PENDING'
              ORDER BY r.created_at DESC",
            new { userId = user.EmployeeId });

        return Ok(rows);
    }

    // POST /api/organization/edit-request/{id}/approve
    [HttpPost("edit-request/{requestId:long}/approve")]
    public async Task<IActionResult> ApproveOrganizationEditRequest(long requestId)
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var request = await conn.QueryFirstOrDefaultAsync(
                "SELECT id, lock_id, requested_to, status FROM master_organization_edit_request WHERE id = @requestId FOR UPDATE",
                new { requestId }, tx);

            if (request == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Edit request not found." });
            }
            if ((long)request.requested_to != user.EmployeeId)
            {
                await tx.RollbackAsync();
                return StatusCode(403, new { message = "You are not authorized to approve this request." });
            }
            if ((string)request.status != "PENDING")
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Request is no longer pending." });
            }

            await conn.ExecuteAsync(
                @"UPDATE master_organization_edit_request
                  SET status = 'APPROVED', approved_by = @UserId, approved_at = NOW(), updated_at = NOW()
                  WHERE id = @requestId",
                new { requestId, UserId = user.EmployeeId }, tx);

            await conn.ExecuteAsync(
                @"UPDATE master_organization_lock
                  SET status = 'APPROVED_EDIT', updated_by = @UserId, updated_at = NOW()
                  WHERE id = @lockId",
                new { lockId = (long)request.lock_id, UserId = user.EmployeeId }, tx);

            await tx.CommitAsync();
            return Ok(new { message = "Edit request approved successfully." });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while approving edit request." });
        }
    }

    // POST /api/organization/edit-request/{id}/reject
    [HttpPost("edit-request/{requestId:long}/reject")]
    public async Task<IActionResult> RejectOrganizationEditRequest(long requestId)
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var request = await conn.QueryFirstOrDefaultAsync(
                "SELECT id, lock_id, requested_to, status FROM master_organization_edit_request WHERE id = @requestId FOR UPDATE",
                new { requestId }, tx);

            if (request == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Edit request not found." });
            }
            if ((long)request.requested_to != user.EmployeeId)
            {
                await tx.RollbackAsync();
                return StatusCode(403, new { message = "You are not authorized to reject this request." });
            }
            if ((string)request.status != "PENDING")
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Request is no longer pending." });
            }

            await conn.ExecuteAsync(
                @"UPDATE master_organization_edit_request
                  SET status = 'REJECTED', rejected_by = @UserId, rejected_at = NOW(), updated_at = NOW()
                  WHERE id = @requestId",
                new { requestId, UserId = user.EmployeeId }, tx);

            await conn.ExecuteAsync(
                @"UPDATE master_organization_lock
                  SET status = 'LOCKED', updated_by = @UserId, updated_at = NOW()
                  WHERE id = @lockId",
                new { lockId = (long)request.lock_id, UserId = user.EmployeeId }, tx);

            await tx.CommitAsync();
            return Ok(new { message = "Edit request rejected." });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while rejecting edit request." });
        }
    }

    // ===========================================================
    // NODE TYPES  (Region / Division / Depot / Department / ... — all
    // user-defined; nothing here is hard-coded)
    // ===========================================================

    public record CreateNodeTypeRequest(string? TypeName, string? TypeCode, string? Description, int? DisplayOrder);
    public record UpdateNodeTypeRequest(string? TypeName, string? TypeCode, string? Description, int? DisplayOrder);
    public record StatusRequest(string? Status);

    // GET /api/organization/node-types
    [HttpGet("node-types")]
    public async Task<IActionResult> GetNodeTypes()
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                @"SELECT nt.* FROM master_organization_node_types nt
                  JOIN master_organizations o ON o.organization_id = nt.organization_id
                  ORDER BY nt.display_order, nt.type_name")).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    // POST /api/organization/node-types
    [HttpPost("node-types")]
    public async Task<IActionResult> CreateNodeType([FromBody] CreateNodeTypeRequest? body)
    {
        if (string.IsNullOrEmpty(body?.TypeName) || string.IsNullOrEmpty(body?.TypeCode))
        {
            return BadRequest(new { message = "typeName and typeCode are required" });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        try
        {
            var organizationId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT organization_id FROM master_organizations ORDER BY organization_id ASC LIMIT 1");
            if (organizationId == null)
            {
                return BadRequest(new { message = "Organization has not been set up yet." });
            }

            var lockStatus = await GetLockStatusAsync(conn, organizationId.Value);
            if (!IsEditableStatus(lockStatus))
            {
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            await conn.ExecuteAsync(
                @"INSERT INTO master_organization_node_types
                  (organization_id, type_name, type_code, description, display_order, status, created_by)
                  VALUES (@organizationId, @TypeName, @TypeCode, @Description, @DisplayOrder, 'ACTIVE', @CreatedBy)",
                new
                {
                    organizationId, body.TypeName, body.TypeCode, body.Description,
                    DisplayOrder = body.DisplayOrder ?? 0, CreatedBy = user.EmployeeId
                });

            var nodeTypeId = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
            return StatusCode(201, new { nodeTypeId, message = "Node type created" });
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode == 1062)
        {
            return Conflict(new { message = "A node type with this code already exists" });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while creating node type" });
        }
    }

    // PUT /api/organization/node-types/:id
    [HttpPut("node-types/{id:int}")]
    public async Task<IActionResult> UpdateNodeType(int id, [FromBody] UpdateNodeTypeRequest? body)
    {
        if (string.IsNullOrEmpty(body?.TypeName) || string.IsNullOrEmpty(body?.TypeCode))
        {
            return BadRequest(new { message = "typeName and typeCode are required" });
        }

        using var conn = _db.CreateConnection();
        try
        {
            var organizationId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT organization_id FROM master_organization_node_types WHERE node_type_id = @id", new { id });
            if (organizationId == null) return NotFound(new { message = "Node type not found" });

            var lockStatus = await GetLockStatusAsync(conn, organizationId.Value);
            if (!IsEditableStatus(lockStatus))
            {
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            var affected = await conn.ExecuteAsync(
                @"UPDATE master_organization_node_types
                  SET type_name = @TypeName, type_code = @TypeCode, description = @Description, display_order = @DisplayOrder
                  WHERE node_type_id = @Id",
                new { body.TypeName, body.TypeCode, body.Description, DisplayOrder = body.DisplayOrder ?? 0, Id = id });

            if (affected == 0) return NotFound(new { message = "Node type not found" });
            return Ok(new { message = "Node type updated" });
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode == 1062)
        {
            return Conflict(new { message = "A node type with this code already exists" });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while updating node type" });
        }
    }

    // PUT /api/organization/node-types/:id/status  { status: 'ACTIVE' | 'INACTIVE' }
    [HttpPut("node-types/{id:int}/status")]
    public async Task<IActionResult> SetNodeTypeStatus(int id, [FromBody] StatusRequest? body)
    {
        if (body?.Status != "ACTIVE" && body?.Status != "INACTIVE")
        {
            return BadRequest(new { message = "status must be ACTIVE or INACTIVE" });
        }

        using var conn = _db.CreateConnection();
        try
        {
            var organizationId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT organization_id FROM master_organization_node_types WHERE node_type_id = @id", new { id });
            if (organizationId == null) return NotFound(new { message = "Node type not found" });

            var lockStatus = await GetLockStatusAsync(conn, organizationId.Value);
            if (!IsEditableStatus(lockStatus))
            {
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            var affected = await conn.ExecuteAsync(
                "UPDATE master_organization_node_types SET status = @Status WHERE node_type_id = @Id",
                new { body.Status, Id = id });

            if (affected == 0) return NotFound(new { message = "Node type not found" });
            return Ok(new { message = $"Node type {(body.Status == "ACTIVE" ? "activated" : "deactivated")}" });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while updating node type status" });
        }
    }

    // ===========================================================
    // NODES  (the dynamic hierarchy itself)
    // ===========================================================

    private class NodeRow
    {
        public int NodeId { get; set; }
        public int OrganizationId { get; set; }
        public int NodeTypeId { get; set; }
        public int? ParentNodeId { get; set; }
        public string NodeName { get; set; } = "";
        public string NodeCode { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public record CreateNodeRequest(
        int? ParentNodeId, int? NodeTypeId, string? NodeName, string? NodeCode, string? Description,
        string? Location, string? Address, string? ContactNumber, string? Email, int? DisplayOrder);

    public record UpdateNodeRequest(
        int? NodeTypeId, string? NodeName, string? NodeCode, string? Description,
        string? Location, string? Address, string? ContactNumber, string? Email, int? DisplayOrder);

    public record MoveNodeRequest(int? NewParentNodeId);

    private static async Task RecordHistory(
        MySqlConnection conn, MySqlTransaction? tx, int nodeId, int organizationId, string actionType, int changedBy,
        int? oldParentNodeId = null, int? newParentNodeId = null,
        int? oldNodeTypeId = null, int? newNodeTypeId = null,
        string? oldNodeName = null, string? newNodeName = null, string? remarks = null)
    {
        await conn.ExecuteAsync(
            @"INSERT INTO master_organization_node_history
              (node_id, organization_id, old_parent_node_id, new_parent_node_id, old_node_type_id, new_node_type_id,
               old_node_name, new_node_name, action_type, changed_by, remarks)
              VALUES (@nodeId, @organizationId, @oldParentNodeId, @newParentNodeId, @oldNodeTypeId, @newNodeTypeId,
                      @oldNodeName, @newNodeName, @actionType, @changedBy, @remarks)",
            new
            {
                nodeId, organizationId, oldParentNodeId, newParentNodeId, oldNodeTypeId, newNodeTypeId,
                oldNodeName, newNodeName, actionType, changedBy, remarks
            }, tx);
    }

    // GET /api/organization/hierarchy — flat list; the FRONTEND builds the tree
    // client-side from node_id / parent_node_id / node_type_id, per spec.
    [HttpGet("hierarchy")]
    public async Task<IActionResult> GetHierarchy()
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                @"SELECT n.*, nt.type_name, nt.type_code
                  FROM master_organization_nodes n
                  JOIN master_organization_node_types nt ON nt.node_type_id = n.node_type_id
                  WHERE n.deleted_at IS NULL
                  ORDER BY n.parent_node_id IS NULL DESC, n.display_order, n.node_name")).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while loading hierarchy" });
        }
    }

    // GET /api/organization/nodes/:id
    [HttpGet("nodes/{id:int}")]
    public async Task<IActionResult> GetNode(int id)
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                @"SELECT n.*, nt.type_name, nt.type_code
                  FROM master_organization_nodes n
                  JOIN master_organization_node_types nt ON nt.node_type_id = n.node_type_id
                  WHERE n.node_id = @id AND n.deleted_at IS NULL",
                new { id })).ToDictList();

            if (rows.Count == 0) return NotFound(new { message = "Node not found" });
            return Ok(rows[0]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }

    // POST /api/organization/nodes
    [HttpPost("nodes")]
    public async Task<IActionResult> CreateNode([FromBody] CreateNodeRequest? body)
    {
        if (body?.NodeTypeId == null || string.IsNullOrEmpty(body?.NodeName) || string.IsNullOrEmpty(body?.NodeCode))
        {
            return BadRequest(new { message = "nodeTypeId, nodeName and nodeCode are required" });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var organizationId = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT organization_id FROM master_organizations ORDER BY organization_id ASC LIMIT 1", transaction: tx);
            if (organizationId == null)
            {
                await tx.RollbackAsync();
                return BadRequest(new { message = "Organization has not been set up yet." });
            }

            var lockStatus = await GetLockStatusAsync(conn, organizationId.Value, tx);
            if (!IsEditableStatus(lockStatus))
            {
                await tx.RollbackAsync();
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            if (body.ParentNodeId != null)
            {
                var parent = await conn.QueryFirstOrDefaultAsync<NodeRow>(
                    "SELECT node_id, status FROM master_organization_nodes WHERE node_id = @id AND deleted_at IS NULL",
                    new { id = body.ParentNodeId }, tx);
                if (parent == null)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { message = "Selected parent node does not exist" });
                }
            }

            await conn.ExecuteAsync(
                @"INSERT INTO master_organization_nodes
                  (organization_id, node_type_id, parent_node_id, node_name, node_code, description,
                   location, address, contact_number, email, display_order, status, created_by)
                  VALUES (@organizationId, @NodeTypeId, @ParentNodeId, @NodeName, @NodeCode, @Description,
                          @Location, @Address, @ContactNumber, @Email, @DisplayOrder, 'ACTIVE', @CreatedBy)",
                new
                {
                    organizationId, body.NodeTypeId, body.ParentNodeId, body.NodeName, body.NodeCode, body.Description,
                    body.Location, body.Address, body.ContactNumber, body.Email, DisplayOrder = body.DisplayOrder ?? 0,
                    CreatedBy = user.EmployeeId
                }, tx);

            var nodeId = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);

            await RecordHistory(conn, tx, nodeId, organizationId.Value, "CREATE", user.EmployeeId,
                newParentNodeId: body.ParentNodeId, newNodeTypeId: body.NodeTypeId, newNodeName: body.NodeName);

            await tx.CommitAsync();
            return StatusCode(201, new { nodeId, message = "Node created" });
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode == 1062)
        {
            await tx.RollbackAsync();
            return Conflict(new { message = "A node with this code already exists in this organization" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while creating node" });
        }
    }

    // PUT /api/organization/nodes/:id — everything EXCEPT parent (see /move)
    [HttpPut("nodes/{id:int}")]
    public async Task<IActionResult> UpdateNode(int id, [FromBody] UpdateNodeRequest? body)
    {
        if (body?.NodeTypeId == null || string.IsNullOrEmpty(body?.NodeName) || string.IsNullOrEmpty(body?.NodeCode))
        {
            return BadRequest(new { message = "nodeTypeId, nodeName and nodeCode are required" });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var existing = await conn.QueryFirstOrDefaultAsync<NodeRow>(
                "SELECT node_id, organization_id, node_type_id, node_name FROM master_organization_nodes WHERE node_id = @id AND deleted_at IS NULL",
                new { id }, tx);
            if (existing == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Node not found" });
            }

            var lockStatus = await GetLockStatusAsync(conn, existing.OrganizationId, tx);
            if (!IsEditableStatus(lockStatus))
            {
                await tx.RollbackAsync();
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            await conn.ExecuteAsync(
                @"UPDATE master_organization_nodes
                  SET node_type_id = @NodeTypeId, node_name = @NodeName, node_code = @NodeCode, description = @Description,
                      location = @Location, address = @Address, contact_number = @ContactNumber, email = @Email,
                      display_order = @DisplayOrder, updated_by = @UpdatedBy
                  WHERE node_id = @Id",
                new
                {
                    body.NodeTypeId, body.NodeName, body.NodeCode, body.Description, body.Location, body.Address,
                    body.ContactNumber, body.Email, DisplayOrder = body.DisplayOrder ?? 0,
                    UpdatedBy = user.EmployeeId, Id = id
                }, tx);

            var nameChanged = existing.NodeName != body.NodeName;
            await RecordHistory(conn, tx, id, existing.OrganizationId, nameChanged ? "RENAME" : "UPDATE", user.EmployeeId,
                oldNodeTypeId: existing.NodeTypeId, newNodeTypeId: body.NodeTypeId,
                oldNodeName: existing.NodeName, newNodeName: body.NodeName);

            await tx.CommitAsync();
            return Ok(new { message = "Node updated" });
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode == 1062)
        {
            await tx.RollbackAsync();
            return Conflict(new { message = "A node with this code already exists in this organization" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while updating node" });
        }
    }

    // PUT /api/organization/nodes/:id/move  { newParentNodeId }
    [HttpPut("nodes/{id:int}/move")]
    public async Task<IActionResult> MoveNode(int id, [FromBody] MoveNodeRequest? body)
    {
        var newParentNodeId = body?.NewParentNodeId;

        if (newParentNodeId == id)
        {
            return BadRequest(new { message = "A node cannot be moved under itself" });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var node = await conn.QueryFirstOrDefaultAsync<NodeRow>(
                "SELECT node_id, organization_id, parent_node_id FROM master_organization_nodes WHERE node_id = @id AND deleted_at IS NULL",
                new { id }, tx);
            if (node == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Node not found" });
            }

            var lockStatus = await GetLockStatusAsync(conn, node.OrganizationId, tx);
            if (!IsEditableStatus(lockStatus))
            {
                await tx.RollbackAsync();
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            if (newParentNodeId != null)
            {
                var newParent = await conn.QueryFirstOrDefaultAsync<NodeRow>(
                    "SELECT node_id, status FROM master_organization_nodes WHERE node_id = @newParentNodeId AND deleted_at IS NULL",
                    new { newParentNodeId }, tx);
                if (newParent == null)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { message = "Target parent node does not exist" });
                }

                // Prevent moving a node under one of its own descendants (circular hierarchy):
                // walk the whole tree and refuse if newParentNodeId shows up under `id`.
                var allNodes = (await conn.QueryAsync<NodeRow>(
                    "SELECT node_id, parent_node_id FROM master_organization_nodes WHERE deleted_at IS NULL", transaction: tx)).ToList();

                var descendants = new HashSet<int>();
                var queue = new Queue<int>();
                queue.Enqueue(id);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var child in allNodes.Where(n => n.ParentNodeId == current))
                    {
                        if (descendants.Add(child.NodeId)) queue.Enqueue(child.NodeId);
                    }
                }

                if (descendants.Contains(newParentNodeId.Value))
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { message = "Cannot move a node under one of its own descendants" });
                }
            }

            await conn.ExecuteAsync(
                "UPDATE master_organization_nodes SET parent_node_id = @newParentNodeId, updated_by = @UpdatedBy WHERE node_id = @id",
                new { newParentNodeId, UpdatedBy = user.EmployeeId, id }, tx);

            await RecordHistory(conn, tx, id, node.OrganizationId, "MOVE", user.EmployeeId,
                oldParentNodeId: node.ParentNodeId, newParentNodeId: newParentNodeId);

            await tx.CommitAsync();
            return Ok(new { message = "Node moved" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while moving node" });
        }
    }

    // PUT /api/organization/nodes/:id/status  { status: 'ACTIVE' | 'INACTIVE' }
    [HttpPut("nodes/{id:int}/status")]
    public async Task<IActionResult> SetNodeStatus(int id, [FromBody] StatusRequest? body)
    {
        if (body?.Status != "ACTIVE" && body?.Status != "INACTIVE")
        {
            return BadRequest(new { message = "status must be ACTIVE or INACTIVE" });
        }

        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var node = await conn.QueryFirstOrDefaultAsync<NodeRow>(
                "SELECT node_id, organization_id, parent_node_id FROM master_organization_nodes WHERE node_id = @id AND deleted_at IS NULL",
                new { id }, tx);
            if (node == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Node not found" });
            }

            var lockStatus = await GetLockStatusAsync(conn, node.OrganizationId, tx);
            if (!IsEditableStatus(lockStatus))
            {
                await tx.RollbackAsync();
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            if (body.Status == "ACTIVE" && node.ParentNodeId != null)
            {
                var parentStatus = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT status FROM master_organization_nodes WHERE node_id = @parentId",
                    new { parentId = node.ParentNodeId }, tx);
                if (parentStatus == "INACTIVE")
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { message = "Cannot activate a node whose parent is inactive" });
                }
            }

            await conn.ExecuteAsync(
                "UPDATE master_organization_nodes SET status = @Status, updated_by = @UpdatedBy WHERE node_id = @id",
                new { body.Status, UpdatedBy = user.EmployeeId, id }, tx);

            await RecordHistory(conn, tx, id, node.OrganizationId, body.Status == "ACTIVE" ? "ACTIVATE" : "DEACTIVATE", user.EmployeeId);

            await tx.CommitAsync();
            return Ok(new { message = $"Node {(body.Status == "ACTIVE" ? "activated" : "deactivated")}" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while updating node status" });
        }
    }

    // DELETE /api/organization/nodes/:id — soft delete, blocked when unsafe
    [HttpDelete("nodes/{id:int}")]
    public async Task<IActionResult> DeleteNode(int id)
    {
        var user = this.GetAuthUser()!;
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var node = await conn.QueryFirstOrDefaultAsync<NodeRow>(
                "SELECT node_id, organization_id, parent_node_id, node_name FROM master_organization_nodes WHERE node_id = @id AND deleted_at IS NULL",
                new { id }, tx);
            if (node == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Node not found" });
            }

            var lockStatus = await GetLockStatusAsync(conn, node.OrganizationId, tx);
            if (!IsEditableStatus(lockStatus))
            {
                await tx.RollbackAsync();
                return StatusCode(423, new { message = "Organization is locked. Request edit permission to make changes." });
            }

            var childCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM master_organization_nodes WHERE parent_node_id = @id AND deleted_at IS NULL",
                new { id }, tx);
            if (childCount > 0)
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Cannot delete a node that has sub-nodes. Move or delete them first." });
            }

            var employeeCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM master_employee_organization_mapping WHERE node_id = @id AND is_current = 1",
                new { id }, tx);
            if (employeeCount > 0)
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Cannot delete a node with employees currently assigned to it. Reassign them first." });
            }

            await conn.ExecuteAsync(
                "UPDATE master_organization_nodes SET deleted_at = NOW(), status = 'INACTIVE', updated_by = @UpdatedBy WHERE node_id = @id",
                new { UpdatedBy = user.EmployeeId, id }, tx);

            await RecordHistory(conn, tx, id, node.OrganizationId, "DELETE", user.EmployeeId, oldNodeName: node.NodeName);

            await tx.CommitAsync();
            return Ok(new { message = "Node deleted" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error while deleting node" });
        }
    }

    // GET /api/organization/nodes/:id/history
    [HttpGet("nodes/{id:int}/history")]
    public async Task<IActionResult> GetNodeHistory(int id)
    {
        using var conn = _db.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync(
                @"SELECT h.*, e.first_name, e.last_name
                  FROM master_organization_node_history h
                  LEFT JOIN employee_master e ON e.employee_id = h.changed_by
                  WHERE h.node_id = @id
                  ORDER BY h.changed_at DESC",
                new { id })).ToDictList();
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error" });
        }
    }
}