using System.Data;
using System.Text.Json;
using Dapper;

using HrmsBackend.Common;
using HrmsBackend.Data;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/organization-data-flow")]
[Authorize]
public class OrganizationDataFlowController : ControllerBase
{
    private readonly DbConnectionFactory _db;

    public OrganizationDataFlowController(DbConnectionFactory db)
    {
        _db = db;
    }

    // =========================================================
    // GET
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var organizationId = await GetOrganizationId();

        using var connection = _db.CreateConnection();

        const string configSql = """
            SELECT
                id,
                status,
                version_no
            FROM master_organization_data_flow_config
            WHERE organization_id = @OrganizationId
            ORDER BY id DESC
            LIMIT 1;
            """;

        var config =
            await connection.QueryFirstOrDefaultAsync<dynamic>(
                configSql,
                new { OrganizationId = organizationId }
            );

        // -----------------------------------------------------
        // Available nodes = active nodes that are not already
        // used somewhere else in the org tree (leaf candidates).
        // -----------------------------------------------------

        const string availableSql = """
            SELECT
                n.node_id AS Id,
                n.node_name AS Name,
                n.node_code AS Code,
                nt.type_name AS NodeType
            FROM master_organization_nodes n
            LEFT JOIN master_organization_node_types nt
                ON nt.node_type_id = n.node_type_id
            WHERE n.organization_id = @OrganizationId
              AND n.status = 'ACTIVE'
              AND NOT EXISTS (
                  SELECT 1 FROM master_organization_nodes child
                  WHERE child.parent_node_id = n.node_id
                    AND child.organization_id = n.organization_id
                    AND child.status = 'ACTIVE'
              )
            ORDER BY n.node_name;
            """;

        var availableNodes =
            (
                await connection.QueryAsync<OrganizationNodeDto>(
                    availableSql,
                    new { OrganizationId = organizationId }
                )
            ).ToList();

        if (config == null)
        {
            return Ok(new OrganizationDataFlowResponse
            {
                Status = OrgDataFlowStatus.Draft,
                VersionNo = 1,
                Tree = new List<OrganizationDataFlowNodeDto>(),
                AvailableNodes = availableNodes
            });
        }

        // NOTE: df.id (DataFlowId) is selected in addition to
        // organization_node_id because parent_data_flow_id points
        // at master_organization_data_flow.id, NOT at the
        // organization node id. Building the tree using the wrong
        // key was the reason the hierarchy came back flat/empty
        // after a reload even though the save had succeeded.
        const string nodeSql = """
            SELECT
                df.id AS DataFlowId,
                df.organization_node_id AS Id,
                onode.node_name AS Name,
                onode.node_code AS Code,
                nt.type_name AS NodeType,
                df.parent_data_flow_id AS ParentDataFlowId,
                df.sort_order AS SortOrder
            FROM master_organization_data_flow df
            INNER JOIN master_organization_nodes onode
                ON onode.node_id = df.organization_node_id
            LEFT JOIN master_organization_node_types nt
                ON nt.node_type_id = onode.node_type_id
            WHERE df.config_id = @ConfigId
              AND df.is_active = 1
            ORDER BY
                df.parent_data_flow_id,
                df.sort_order;
            """;

        var rows =
            (
                await connection.QueryAsync<dynamic>(
                    nodeSql,
                    new { ConfigId = (long)config.id }
                )
            ).ToList();

        var tree = BuildTree(rows);

        return Ok(new OrganizationDataFlowResponse
        {
            Status = config.status,
            VersionNo = config.version_no,
            Tree = tree,
            AvailableNodes = availableNodes
        });
    }

    // =========================================================
    // SAVE
    // =========================================================

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveOrganizationDataFlowRequest request)
    {
        var organizationId = await GetOrganizationId();
        var user = this.GetAuthUser()!;
        var userId = user.EmployeeId;
        using var connection = _db.CreateConnection();
        EnsureOpen(connection);
        using var transaction = connection.BeginTransaction();

        try
        {
            var config = await GetConfigForUpdateAsync(connection, transaction, organizationId);

            if (config == null)
            {
                var configId =
                    await connection.ExecuteScalarAsync<long>(
                        """
                        INSERT INTO master_organization_data_flow_config
                        (organization_id, status, version_no, created_by, created_at)
                        VALUES
                        (@OrganizationId, 'DRAFT', 1, @UserId, NOW());

                        SELECT LAST_INSERT_ID();
                        """,
                        new { OrganizationId = organizationId, UserId = userId },
                        transaction
                    );

                config = new FlowConfig { Id = configId, Status = OrgDataFlowStatus.Draft, VersionNo = 1 };
            }

            if (config.Status == OrgDataFlowStatus.Locked || config.Status == OrgDataFlowStatus.EditRequested)
            {
                throw new InvalidOperationException("Organization Data Flow Hierarchy is locked.");
            }

            if (config.VersionNo != request.VersionNo)
            {
                throw new InvalidOperationException("Hierarchy version has changed. Please reload the page.");
            }

            await ValidateNodesAsync(connection, transaction, organizationId, request.Tree);

            var oldSnapshot = await GetActiveFlowSnapshotAsync(connection, transaction, config.Id);

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow
                SET is_active = 0, updated_by = @UserId, updated_at = NOW()
                WHERE config_id = @ConfigId;
                """,
                new { ConfigId = config.Id, UserId = userId },
                transaction
            );

            await InsertTreeAsync(connection, transaction, config.Id, request.Tree, userId);

            var newSnapshot = await GetActiveFlowSnapshotAsync(connection, transaction, config.Id);
            var changesSummary = BuildChangesSummary(oldSnapshot, newSnapshot);

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_config
                SET updated_by = @UserId, updated_at = NOW()
                WHERE id = @ConfigId;
                """,
                new { ConfigId = config.Id, UserId = userId },
                transaction
            );

            await RecordFlowHistoryAsync(
                connection, transaction, config.Id, organizationId, "SAVE",
                oldStatus: config.Status, newStatus: config.Status,
                oldVersionNo: request.VersionNo, newVersionNo: request.VersionNo,
                changedBy: userId, changesSummary: changesSummary
            );

            transaction.Commit();

            return Ok(new { message = "Organization Data Flow Hierarchy saved successfully.", versionNo = request.VersionNo });
        }
        catch (InvalidOperationException ex)
        {
            transaction.Rollback();
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================
    // LOCK
    // =========================================================

    [HttpPost("lock")]
    public async Task<IActionResult> Lock()
    {
        var organizationId = await GetOrganizationId();
        var user = this.GetAuthUser()!;
        var userId = user.EmployeeId;

        using var connection = _db.CreateConnection();
        EnsureOpen(connection);
        using var transaction = connection.BeginTransaction();

        try
        {
            var config = await GetConfigForUpdateAsync(connection, transaction, organizationId);

            if (config == null ||
                (config.Status != OrgDataFlowStatus.Draft &&
                 config.Status != OrgDataFlowStatus.Unlocked &&
                 config.Status != OrgDataFlowStatus.ApprovedEdit))
            {
                transaction.Rollback();
                return BadRequest(new { message = "Hierarchy cannot be locked in its current state." });
            }

            var oldStatus = config.Status;

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_config
                SET status = @Status, locked_by = @UserId, locked_at = NOW(),
                    updated_by = @UserId, updated_at = NOW()
                WHERE id = @ConfigId;
                """,
                new { ConfigId = config.Id, UserId = userId, Status = OrgDataFlowStatus.Locked },
                transaction
            );

            await RecordFlowHistoryAsync(
                connection, transaction, config.Id, organizationId, "LOCK",
                oldStatus: oldStatus, newStatus: OrgDataFlowStatus.Locked,
                oldVersionNo: config.VersionNo, newVersionNo: config.VersionNo,
                changedBy: userId
            );

            transaction.Commit();

            return Ok(new { message = "Organization Data Flow Hierarchy locked successfully." });
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================
    // JOB TITLES / USERS (for edit request)
    // =========================================================

    [HttpGet("request/job-titles")]
    public async Task<IActionResult> GetJobTitles()
    {
        using var connection = _db.CreateConnection();

        const string sql = """
            SELECT id AS Id, role_name AS Name
            FROM roles
            WHERE active = 1
            ORDER BY role_name;
            """;

        var result = await connection.QueryAsync<JobTitleDto>(sql);
        return Ok(result.ToList());
    }

    [HttpGet("request/users")]
    public async Task<IActionResult> GetUsers([FromQuery] long jobTitleId)
    {
        using var connection = _db.CreateConnection();

        const string sql = """
            SELECT
                e.employee_id AS Id,
                CONCAT(COALESCE(e.first_name, ''), ' ', COALESCE(e.last_name, '')) AS Name,
                u.username AS Username
            FROM employee_master e
            INNER JOIN user_accounts u ON u.employee_id = e.employee_id
            WHERE u.role_id = @JobTitleId
              AND e.status = 'ACTIVE'
              AND u.is_active = 1
            ORDER BY e.first_name;
            """;

        var result = await connection.QueryAsync<OrganizationUserDto>(sql, new { JobTitleId = jobTitleId });
        return Ok(result.ToList());
    }

    // =========================================================
    // EDIT REQUEST: CREATE
    // =========================================================

    [HttpPost("edit-request")]
    public async Task<IActionResult> CreateEditRequest([FromBody] EditRequestCreateDto request)
    {
        var organizationId = await GetOrganizationId();
        var user = this.GetAuthUser()!;
        var userId = user.EmployeeId;

        using var connection = _db.CreateConnection();
        EnsureOpen(connection);
        using var transaction = connection.BeginTransaction();

        try
        {
            var config =
                await connection.QueryFirstOrDefaultAsync<dynamic>(
                    """
                    SELECT id, status
                    FROM master_organization_data_flow_config
                    WHERE organization_id = @OrganizationId
                    ORDER BY id DESC
                    LIMIT 1
                    FOR UPDATE;
                    """,
                    new { OrganizationId = organizationId },
                    transaction
                );

            if (config == null)
            {
                throw new InvalidOperationException("Data Flow configuration not found.");
            }

            if (config.status != OrgDataFlowStatus.Locked)
            {
                throw new InvalidOperationException("Edit request can only be created when the hierarchy is locked.");
            }

            var pending =
                await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM master_organization_data_flow_edit_request
                    WHERE config_id = @ConfigId
                      AND requested_by = @RequestedBy
                      AND status = 'PENDING';
                    """,
                    new { ConfigId = (long)config.id, RequestedBy = userId },
                    transaction
                );

            if (pending > 0)
            {
                throw new InvalidOperationException("You already have a pending edit request.");
            }

            var requestId =
                await connection.ExecuteScalarAsync<long>(
                    """
                    INSERT INTO master_organization_data_flow_edit_request
                    (config_id, requested_by, requested_to, requested_job_title_id, reason, status, edit_used, created_at)
                    VALUES
                    (@ConfigId, @RequestedBy, @RequestedTo, @JobTitleId, @Reason, 'PENDING', 0, NOW());

                    SELECT LAST_INSERT_ID();
                    """,
                    new
                    {
                        ConfigId = (long)config.id,
                        RequestedBy = userId,
                        RequestedTo = request.RequestedToUserId,
                        JobTitleId = request.JobTitleId,
                        Reason = request.Reason
                    },
                    transaction
                );

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_config
                SET status = 'EDIT_REQUESTED', updated_by = @UserId, updated_at = NOW()
                WHERE id = @ConfigId;
                """,
                new { ConfigId = (long)config.id, UserId = userId },
                transaction
            );

            await RecordFlowHistoryAsync(
                connection, transaction, (long)config.id, organizationId, "EDIT_REQUEST_CREATED",
                oldStatus: (string)config.status, newStatus: "EDIT_REQUESTED",
                oldVersionNo: null, newVersionNo: null,
                changedBy: userId, remarks: $"Requested to user {request.RequestedToUserId}: {request.Reason}"
            );

            transaction.Commit();

            return Ok(new { message = "Edit request sent successfully.", requestId });
        }
        catch (InvalidOperationException ex)
        {
            transaction.Rollback();
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================
    // EDIT REQUEST: PENDING LIST
    // =========================================================

    [HttpGet("edit-requests")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var user = this.GetAuthUser()!;
        var userId = user.EmployeeId;

        using var connection = _db.CreateConnection();

        const string sql = """
            SELECT
                r.id AS Id,
                r.requested_by AS RequestedBy,
                CONCAT(COALESCE(rb.first_name, ''), ' ', COALESCE(rb.last_name, '')) AS RequestedByName,
                r.requested_to AS RequestedTo,
                CONCAT(COALESCE(rt.first_name, ''), ' ', COALESCE(rt.last_name, '')) AS RequestedToName,
                r.requested_job_title_id AS RequestedJobTitleId,
                jt.role_name AS JobTitleName,
                r.reason AS Reason,
                r.status AS Status,
                r.edit_used AS EditUsed,
                r.created_at AS CreatedAt
            FROM master_organization_data_flow_edit_request r
            LEFT JOIN employee_master rb ON rb.employee_id = r.requested_by
            LEFT JOIN employee_master rt ON rt.employee_id = r.requested_to
            LEFT JOIN roles jt ON jt.id = r.requested_job_title_id
            WHERE r.requested_to = @UserId
              AND r.status = 'PENDING'
            ORDER BY r.created_at DESC;
            """;

        var result = await connection.QueryAsync<DataFlowEditRequestDto>(sql, new { UserId = userId });
        return Ok(result.ToList());
    }

    // =========================================================
    // EDIT REQUEST: APPROVE
    // =========================================================

    [HttpPost("edit-request/{requestId:long}/approve")]
    public async Task<IActionResult> Approve(long requestId)
    {
        var user = this.GetAuthUser()!;
        var approverId = user.EmployeeId;

        using var connection = _db.CreateConnection();
        EnsureOpen(connection);
        using var transaction = connection.BeginTransaction();

        try
        {
            var request =
                await connection.QueryFirstOrDefaultAsync<dynamic>(
                    """
                    SELECT r.id, r.config_id, r.requested_to, r.status, c.organization_id, c.status AS config_status
                    FROM master_organization_data_flow_edit_request r
                    INNER JOIN master_organization_data_flow_config c ON c.id = r.config_id
                    WHERE r.id = @RequestId
                    FOR UPDATE;
                    """,
                    new { RequestId = requestId },
                    transaction
                );

            if (request == null)
            {
                throw new InvalidOperationException("Edit request not found.");
            }

            if ((long)request.requested_to != approverId)
            {
                transaction.Rollback();
                return StatusCode(403, new { message = "You are not authorized to approve this request." });
            }

            if (request.status != "PENDING")
            {
                throw new InvalidOperationException("Request is no longer pending.");
            }

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_edit_request
                SET status = 'APPROVED', approved_by = @ApproverId, approved_at = NOW(), updated_at = NOW()
                WHERE id = @RequestId;
                """,
                new { RequestId = requestId, ApproverId = approverId },
                transaction
            );

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_config
                SET status = 'APPROVED_EDIT', updated_by = @ApproverId, updated_at = NOW()
                WHERE id = @ConfigId;
                """,
                new { ConfigId = (long)request.config_id, ApproverId = approverId },
                transaction
            );

            await RecordFlowHistoryAsync(
                connection, transaction, (long)request.config_id, (long)request.organization_id, "EDIT_REQUEST_APPROVED",
                oldStatus: (string)request.config_status, newStatus: "APPROVED_EDIT",
                oldVersionNo: null, newVersionNo: null,
                changedBy: approverId, remarks: $"Edit request #{requestId} approved"
            );

            transaction.Commit();

            return Ok(new { message = "Edit request approved successfully." });
        }
        catch (InvalidOperationException ex)
        {
            transaction.Rollback();
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================
    // EDIT REQUEST: REJECT
    // =========================================================

    [HttpPost("edit-request/{requestId:long}/reject")]
    public async Task<IActionResult> Reject(long requestId)
    {
        var user = this.GetAuthUser()!;
        var approverId = user.EmployeeId;

        using var connection = _db.CreateConnection();
        EnsureOpen(connection);
        using var transaction = connection.BeginTransaction();

        try
        {
            var request =
                await connection.QueryFirstOrDefaultAsync<dynamic>(
                    """
                    SELECT r.id, r.config_id, r.requested_to, r.status, c.organization_id, c.status AS config_status
                    FROM master_organization_data_flow_edit_request r
                    INNER JOIN master_organization_data_flow_config c ON c.id = r.config_id
                    WHERE r.id = @RequestId
                    FOR UPDATE;
                    """,
                    new { RequestId = requestId },
                    transaction
                );

            if (request == null)
            {
                throw new InvalidOperationException("Edit request not found.");
            }

            if ((long)request.requested_to != approverId)
            {
                transaction.Rollback();
                return StatusCode(403, new { message = "You are not authorized to reject this request." });
            }

            if (request.status != "PENDING")
            {
                throw new InvalidOperationException("Request is no longer pending.");
            }

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_edit_request
                SET status = 'REJECTED', rejected_by = @ApproverId, rejected_at = NOW(), updated_at = NOW()
                WHERE id = @RequestId;
                """,
                new { RequestId = requestId, ApproverId = approverId },
                transaction
            );

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_config
                SET status = 'LOCKED', updated_by = @ApproverId, updated_at = NOW()
                WHERE id = @ConfigId;
                """,
                new { ConfigId = (long)request.config_id, ApproverId = approverId },
                transaction
            );

            await RecordFlowHistoryAsync(
                connection, transaction, (long)request.config_id, (long)request.organization_id, "EDIT_REQUEST_REJECTED",
                oldStatus: (string)request.config_status, newStatus: "LOCKED",
                oldVersionNo: null, newVersionNo: null,
                changedBy: approverId, remarks: $"Edit request #{requestId} rejected"
            );

            transaction.Commit();

            return Ok(new { message = "Edit request rejected." });
        }
        catch (InvalidOperationException ex)
        {
            transaction.Rollback();
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================
    // SUBMIT APPROVED EDIT
    // =========================================================

    [HttpPost("submit-approved-edit")]
    public async Task<IActionResult> SubmitApprovedEdit([FromBody] SaveOrganizationDataFlowRequest request)
    {
        var organizationId = await GetOrganizationId();
        var user = this.GetAuthUser()!;
        var userId = user.EmployeeId;

        using var connection = _db.CreateConnection();
        EnsureOpen(connection);
        using var transaction = connection.BeginTransaction();

        try
        {
            var config = await GetConfigForUpdateAsync(connection, transaction, organizationId);

            if (config == null)
            {
                throw new InvalidOperationException("Configuration not found.");
            }

            if (config.Status != OrgDataFlowStatus.ApprovedEdit)
            {
                throw new InvalidOperationException("No approved edit permission exists.");
            }

            if (config.VersionNo != request.VersionNo)
            {
                throw new InvalidOperationException("Hierarchy version has changed. Please reload.");
            }

            var editRequest =
                await connection.QueryFirstOrDefaultAsync<dynamic>(
                    """
                    SELECT id, requested_by, status, edit_used
                    FROM master_organization_data_flow_edit_request
                    WHERE config_id = @ConfigId
                      AND requested_by = @UserId
                      AND status = 'APPROVED'
                      AND edit_used = 0
                    ORDER BY approved_at DESC
                    LIMIT 1
                    FOR UPDATE;
                    """,
                    new { ConfigId = config.Id, UserId = userId },
                    transaction
                );

            if (editRequest == null)
            {
                throw new InvalidOperationException("No unused edit permission is available.");
            }

            await ValidateNodesAsync(connection, transaction, organizationId, request.Tree);

            var oldSnapshot = await GetActiveFlowSnapshotAsync(connection, transaction, config.Id);

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow
                SET is_active = 0, updated_by = @UserId, updated_at = NOW()
                WHERE config_id = @ConfigId;
                """,
                new { ConfigId = config.Id, UserId = userId },
                transaction
            );

            await InsertTreeAsync(connection, transaction, config.Id, request.Tree, userId);

            var newSnapshot = await GetActiveFlowSnapshotAsync(connection, transaction, config.Id);
            var changesSummary = BuildChangesSummary(oldSnapshot, newSnapshot);

            var newVersion = config.VersionNo + 1;

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_edit_request
                SET edit_used = 1, status = 'USED', updated_at = NOW()
                WHERE id = @RequestId;
                """,
                new { RequestId = (long)editRequest.id },
                transaction
            );

            await connection.ExecuteAsync(
                """
                UPDATE master_organization_data_flow_config
                SET status = 'LOCKED', version_no = @VersionNo, locked_by = @UserId, locked_at = NOW(),
                    updated_by = @UserId, updated_at = NOW()
                WHERE id = @ConfigId;
                """,
                new { ConfigId = config.Id, VersionNo = newVersion, UserId = userId },
                transaction
            );

            await RecordFlowHistoryAsync(
                connection, transaction, config.Id, organizationId, "SUBMIT_APPROVED_EDIT",
                oldStatus: OrgDataFlowStatus.ApprovedEdit, newStatus: OrgDataFlowStatus.Locked,
                oldVersionNo: config.VersionNo, newVersionNo: newVersion,
                changedBy: userId, changesSummary: changesSummary,
                remarks: $"Edit request #{editRequest.id} used"
            );

            transaction.Commit();

            return Ok(new { message = "Changes submitted successfully. Hierarchy locked again.", versionNo = newVersion });
        }
        catch (InvalidOperationException ex)
        {
            transaction.Rollback();
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================
    // HISTORY (who changed what, and when)
    // =========================================================

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var organizationId = await GetOrganizationId();

        using var connection = _db.CreateConnection();

        const string sql = """
            SELECT
                h.id AS Id,
                h.action_type AS ActionType,
                h.old_values AS OldValues,
                h.new_values AS NewValues,
                h.remarks AS Remarks,
                h.changed_by AS ChangedBy,
                CONCAT(COALESCE(e.first_name, ''), ' ', COALESCE(e.last_name, '')) AS ChangedByName,
                h.changed_at AS ChangedAt
            FROM master_change_history h
            LEFT JOIN employee_master e ON e.employee_id = h.changed_by
            WHERE h.organization_id = @OrganizationId
              AND h.entity_type IN ('DATA_FLOW', 'DATA_FLOW_LOCK')
            ORDER BY h.changed_at DESC
            LIMIT 200;
            """;

        var result = await connection.QueryAsync<DataFlowHistoryDto>(sql, new { OrganizationId = organizationId });
        return Ok(result.ToList());
    }

    // =========================================================
    // PRIVATE HELPERS
    // =========================================================

    private static void EnsureOpen(IDbConnection connection)
    {
        // BeginTransaction() requires an already-open connection.
        // Dapper's Query/Execute helpers open the connection for you
        // automatically, but that does not happen before an explicit
        // BeginTransaction() call, so every write endpoint that uses
        // a transaction must open the connection itself first -
        // otherwise BeginTransaction() throws and nothing is ever
        // saved to the database.
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
    }

    // ---------------------------------------------------------
    // CHANGE TRACKING: who changed what, and when.
    // Every write endpoint (save, lock, edit-request create /
    // approve / reject, submit-approved-edit) writes one row here.
    // ---------------------------------------------------------

    private async Task RecordFlowHistoryAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long configId,
        long organizationId,
        string actionType,
        string? oldStatus,
        string? newStatus,
        int? oldVersionNo,
        int? newVersionNo,
        long changedBy,
        string? changesSummary = null,
        string? remarks = null)
    {
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        if (oldStatus != newStatus)
        {
            oldValues["status"] = oldStatus;
            newValues["status"] = newStatus;
        }

        if (oldVersionNo != newVersionNo)
        {
            oldValues["versionNo"] = oldVersionNo;
            newValues["versionNo"] = newVersionNo;
        }

        if (!string.IsNullOrEmpty(changesSummary))
        {
            // changesSummary is already a JSON object {"added":[...],"removed":[...],"reparented":[...]} -
            // merge its keys straight into new_values rather than double-encoding it as a string.
            using var doc = JsonDocument.Parse(changesSummary);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                newValues[prop.Name] = prop.Value.EnumerateArray().Select(x => x.GetString()).ToList();
            }
        }

        var oldJson = oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null;
        var newJson = newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null;

        await connection.ExecuteAsync(
            """
            INSERT INTO master_change_history
            (organization_id, entity_type, entity_id, action_type, old_values, new_values, remarks, changed_by, changed_at)
            VALUES
            (@OrganizationId, 'DATA_FLOW', @ConfigId, @ActionType, @OldValues, @NewValues, @Remarks, @ChangedBy, NOW());
            """,
            new
            {
                ConfigId = configId,
                OrganizationId = organizationId,
                ActionType = actionType,
                OldValues = oldJson,
                NewValues = newJson,
                Remarks = remarks,
                ChangedBy = changedBy
            },
            transaction
        );
    }

    // Snapshot of the currently-active hierarchy, keyed by
    // organization_node_id (stable across saves, unlike the
    // data-flow row's own auto-increment id) so two snapshots
    // taken before/after a save can be diffed node-by-node.
    private async Task<List<FlowNodeSnapshot>> GetActiveFlowSnapshotAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long configId)
    {
        const string sql = """
            SELECT
                df.organization_node_id AS OrganizationNodeId,
                parentNode.organization_node_id AS ParentOrganizationNodeId,
                onode.node_name AS NodeName
            FROM master_organization_data_flow df
            INNER JOIN master_organization_nodes onode
                ON onode.node_id = df.organization_node_id
            LEFT JOIN master_organization_data_flow parentNode
                ON parentNode.id = df.parent_data_flow_id
            WHERE df.config_id = @ConfigId
              AND df.is_active = 1;
            """;

        var rows = await connection.QueryAsync<FlowNodeSnapshot>(sql, new { ConfigId = configId }, transaction);
        return rows.ToList();
    }

    // Builds a small JSON summary of what a save actually changed
    // (nodes added, removed, or moved to a different parent) so the
    // history table records real data, not just "a save happened".
    private string? BuildChangesSummary(List<FlowNodeSnapshot> oldSnapshot, List<FlowNodeSnapshot> newSnapshot)
    {
        var oldMap = oldSnapshot.ToDictionary(x => x.OrganizationNodeId);
        var newMap = newSnapshot.ToDictionary(x => x.OrganizationNodeId);

        var added = newSnapshot
            .Where(n => !oldMap.ContainsKey(n.OrganizationNodeId))
            .Select(n => n.NodeName)
            .ToList();

        var removed = oldSnapshot
            .Where(n => !newMap.ContainsKey(n.OrganizationNodeId))
            .Select(n => n.NodeName)
            .ToList();

        var reparented = newSnapshot
            .Where(n => oldMap.TryGetValue(n.OrganizationNodeId, out var old) &&
                        old.ParentOrganizationNodeId != n.ParentOrganizationNodeId)
            .Select(n => n.NodeName)
            .ToList();

        if (added.Count == 0 && removed.Count == 0 && reparented.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(new { added, removed, reparented });
    }

    private class FlowNodeSnapshot
    {
        public long OrganizationNodeId { get; set; }
        public long? ParentOrganizationNodeId { get; set; }
        public string NodeName { get; set; } = string.Empty;
    }

    private async Task<long> GetOrganizationId()
    {
        using var connection = _db.CreateConnection();

        const string sql = """
            SELECT organization_id
            FROM master_organizations
            WHERE status = 1
            ORDER BY organization_id
            LIMIT 1;
            """;

        var organizationId = await connection.QueryFirstOrDefaultAsync<long?>(sql);

        if (!organizationId.HasValue)
        {
            throw new UnauthorizedAccessException("No active organization found.");
        }

        return organizationId.Value;
    }

    private async Task<FlowConfig?> GetConfigForUpdateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long organizationId)
    {
        const string sql = """
            SELECT id AS Id, status AS Status, version_no AS VersionNo
            FROM master_organization_data_flow_config
            WHERE organization_id = @OrganizationId
            ORDER BY id DESC
            LIMIT 1
            FOR UPDATE;
            """;

        return await connection.QueryFirstOrDefaultAsync<FlowConfig>(
            sql,
            new { OrganizationId = organizationId },
            transaction
        );
    }

    private async Task ValidateNodesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long organizationId,
        List<OrganizationDataFlowItemDto> items)
    {
        if (items == null || items.Count == 0)
        {
            throw new InvalidOperationException("Hierarchy cannot be empty.");
        }

        var nodeIds = items.Select(x => x.OrganizationNodeId).Distinct().ToList();

        const string sql = """
            SELECT COUNT(*)
            FROM master_organization_nodes
            WHERE organization_id = @OrganizationId
              AND status = 'ACTIVE'
              AND node_id IN @NodeIds;
            """;

        var count =
            await connection.ExecuteScalarAsync<int>(
                sql,
                new { OrganizationId = organizationId, NodeIds = nodeIds },
                transaction
            );

        if (count != nodeIds.Count)
        {
            throw new InvalidOperationException("One or more organization nodes are invalid.");
        }

        var seen = new HashSet<long>();

        foreach (var item in items)
        {
            if (!seen.Add(item.OrganizationNodeId))
            {
                throw new InvalidOperationException($"Duplicate organization node: {item.OrganizationNodeId}");
            }

            if (item.ParentOrganizationNodeId.HasValue && item.ParentOrganizationNodeId.Value == item.OrganizationNodeId)
            {
                throw new InvalidOperationException("A node cannot be its own parent.");
            }

            if (item.ParentOrganizationNodeId.HasValue && !nodeIds.Contains(item.ParentOrganizationNodeId.Value))
            {
                throw new InvalidOperationException("Parent node does not exist in the submitted hierarchy.");
            }
        }

        ValidateCircularReferences(items);
    }

    private void ValidateCircularReferences(List<OrganizationDataFlowItemDto> items)
    {
        var parentMap = items.ToDictionary(x => x.OrganizationNodeId, x => x.ParentOrganizationNodeId);

        foreach (var item in items)
        {
            var visited = new HashSet<long>();
            long? current = item.ParentOrganizationNodeId;

            while (current.HasValue)
            {
                if (!visited.Add(current.Value) || current.Value == item.OrganizationNodeId)
                {
                    throw new InvalidOperationException("Circular hierarchy detected.");
                }

                parentMap.TryGetValue(current.Value, out var next);
                current = next;
            }
        }
    }

    private async Task InsertTreeAsync(
    IDbConnection connection,
    IDbTransaction transaction,
    long configId,
    List<OrganizationDataFlowItemDto> items,
    long userId)
    {
        // Renumber siblings 1..n per parent group, regardless of what
        // sort_order the client sent - keeps ordering gap-free and
        // consistent even after nodes are removed/reordered.
        var normalized = NormalizeSortOrder(items);

        // First pass: upsert every node (parent_data_flow_id left NULL
        // for now - it is only known once every row has an id). Using
        // ON DUPLICATE KEY UPDATE instead of a plain INSERT avoids
        // colliding with the row's own (organization_node_id, config_id)
        // unique key when the node already exists from a previous save
        // (it may just be sitting there with is_active = 0).
        var dataFlowIds = new Dictionary<long, long>();

        foreach (var item in normalized)
        {
            await connection.ExecuteAsync(
                """
            INSERT INTO master_organization_data_flow
            (config_id, organization_node_id, parent_data_flow_id, sort_order, is_active, created_by, created_at)
            VALUES
            (@ConfigId, @OrganizationNodeId, NULL, @SortOrder, 1, @UserId, NOW())
            ON DUPLICATE KEY UPDATE
                parent_data_flow_id = NULL,
                sort_order = @SortOrder,
                is_active = 1,
                updated_by = @UserId,
                updated_at = NOW();
            """,
                new
                {
                    ConfigId = configId,
                    OrganizationNodeId = item.OrganizationNodeId,
                    SortOrder = item.SortOrder,
                    UserId = userId
                },
                transaction
            );

            var id =
                await connection.ExecuteScalarAsync<long>(
                    """
                SELECT id
                FROM master_organization_data_flow
                WHERE config_id = @ConfigId
                  AND organization_node_id = @OrganizationNodeId
                LIMIT 1;
                """,
                    new { ConfigId = configId, OrganizationNodeId = item.OrganizationNodeId },
                    transaction
                );

            dataFlowIds[item.OrganizationNodeId] = id;
        }

        // Second pass: wire up parent_data_flow_id now that every row
        // (existing and newly inserted) has a known id.
        foreach (var item in normalized)
        {
            if (!item.ParentOrganizationNodeId.HasValue)
            {
                continue;
            }

            if (!dataFlowIds.TryGetValue(item.ParentOrganizationNodeId.Value, out var parentDataFlowId))
            {
                throw new InvalidOperationException("Parent node does not exist in the submitted hierarchy.");
            }

            await connection.ExecuteAsync(
                """
            UPDATE master_organization_data_flow
            SET parent_data_flow_id = @ParentId
            WHERE id = @Id;
            """,
                new { Id = dataFlowIds[item.OrganizationNodeId], ParentId = parentDataFlowId },
                transaction
            );
        }
    }

    private List<OrganizationDataFlowItemDto> NormalizeSortOrder(
        List<OrganizationDataFlowItemDto> items)
    {
        var result = new List<OrganizationDataFlowItemDto>();

        var groups = items
            .Select((item, index) => (item, index))
            .GroupBy(x => x.item.ParentOrganizationNodeId);

        foreach (var group in groups)
        {
            var siblings = group.OrderBy(x => x.index).ToList();

            for (var i = 0; i < siblings.Count; i++)
            {
                var original = siblings[i].item;

                result.Add(new OrganizationDataFlowItemDto
                {
                    OrganizationNodeId = original.OrganizationNodeId,
                    ParentOrganizationNodeId = original.ParentOrganizationNodeId,
                    SortOrder = i + 1
                });
            }
        }

        return result;
    }

    private List<OrganizationDataFlowNodeDto> BuildTree(List<dynamic> rows)
    {
        // Keyed by the data-flow row's own id (df.id), NOT by the
        // organization node id - parent_data_flow_id always refers
        // to the former.
        var nodes = new Dictionary<long, OrganizationDataFlowNodeDto>();
        var parentOf = new Dictionary<long, long?>();

        foreach (var row in rows)
        {
            var dataFlowId = Convert.ToInt64(row.DataFlowId);

            nodes[dataFlowId] = new OrganizationDataFlowNodeDto
            {
                Id = Convert.ToInt64(row.Id),
                Name = row.Name?.ToString() ?? "",
                Code = row.Code?.ToString() ?? "",
                NodeType = row.NodeType?.ToString() ?? ""
            };

            parentOf[dataFlowId] =
                row.ParentDataFlowId == null
                    ? null
                    : Convert.ToInt64(row.ParentDataFlowId);
        }

        var roots = new List<OrganizationDataFlowNodeDto>();

        foreach (var pair in nodes)
        {
            var dataFlowId = pair.Key;
            var parentId = parentOf[dataFlowId];

            if (!parentId.HasValue || !nodes.ContainsKey(parentId.Value))
            {
                roots.Add(pair.Value);
            }
            else
            {
                nodes[parentId.Value].Children.Add(pair.Value);
            }
        }

        return roots;
    }

    private class FlowConfig
    {
        public long Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int VersionNo { get; set; }
    }
}

// =============================================================
// STATUS CONSTANTS
// =============================================================

public static class OrgDataFlowStatus
{
    public const string Draft = "DRAFT";
    public const string Unlocked = "UNLOCKED";
    public const string Locked = "LOCKED";
    public const string EditRequested = "EDIT_REQUESTED";
    public const string ApprovedEdit = "APPROVED_EDIT";
    public const string Rejected = "REJECTED";
}

// =============================================================
// DTOs (previously OrganizationDataFlowDtos.cs)
// =============================================================

public class OrganizationDataFlowResponse
{
    public string Status { get; set; } = "DRAFT";
    public int VersionNo { get; set; }
    public List<OrganizationDataFlowNodeDto> Tree { get; set; } = new();
    public List<OrganizationNodeDto> AvailableNodes { get; set; } = new();
}

public class OrganizationDataFlowNodeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? NodeType { get; set; }
    public List<OrganizationDataFlowNodeDto> Children { get; set; } = new();
}

public class OrganizationNodeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? NodeType { get; set; }
}

public class SaveOrganizationDataFlowRequest
{
    public int VersionNo { get; set; }
    public List<OrganizationDataFlowItemDto> Tree { get; set; } = new();
}

public class OrganizationDataFlowItemDto
{
    public long OrganizationNodeId { get; set; }
    public long? ParentOrganizationNodeId { get; set; }
    public int SortOrder { get; set; }
}

public class EditRequestCreateDto
{
    public long JobTitleId { get; set; }
    public long RequestedToUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class JobTitleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class OrganizationUserDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }
}

public class DataFlowEditRequestDto
{
    public long Id { get; set; }
    public long RequestedBy { get; set; }
    public string? RequestedByName { get; set; }
    public long RequestedTo { get; set; }
    public string? RequestedToName { get; set; }
    public long RequestedJobTitleId { get; set; }
    public string? JobTitleName { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool EditUsed { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DataFlowHistoryDto
{
    public long Id { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Remarks { get; set; }
    public long ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
    public DateTime ChangedAt { get; set; }
}