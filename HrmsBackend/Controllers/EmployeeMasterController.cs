using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Filters;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/employee-master")]
[Authorize]
public class EmployeeMasterController : ControllerBase
{
    // =========================================================
    // SETTINGS
    // =========================================================

    private const string AdminRole = "SUPERADMIN";

    // Logins can be created by SUPERADMIN and by any role with the EDIT permission on one of these menu pages
    // (matched against menus.route_path). Only SUPERADMIN may grant the SUPERADMIN role.
    private const string AddPageRoute = "/employee/add";
    private const string ListPageRoute = "/employee/All";

    // role_code of the plain employee role: its login username is the employee code
    private const string EmployeeRoleCode = "EMPLOYEE";
    private const int MinPasswordLength = 6;

    // the employee master field that holds the profile picture (optional: if it doesn't exist, the profile shows initials)
    private const string ProfilePhotoLabel = "Profile Photo";
    private const string ProfilePhotoColumn = "cf_profile_photo";

    // menu_key of your Employee page in `menus`; null = any logged-in user may use the employee endpoints
    private static readonly string? EmployeeMenuKey = null;

    private const int MaxCustomFields = 80;       // per table
    private const int ExportLimit = 50000;

    private sealed record LookupMeta(string Table, string IdCol, string NameCol);

    private static readonly Dictionary<string, LookupMeta> Lookup = new()
    {
        ["department"] = new("departments", "id", "dep_name"),
        ["designation"] = new("designations", "id", "name")
    };

    private readonly DbConnectionFactory _db;
    private readonly FileStorageClient _files;

    private record OwnDoc(string Section, string Label, string FileId, string FileName, bool IsPhoto);

    public EmployeeMasterController(DbConnectionFactory db, FileStorageClient files)
    {
        _db = db;
        _files = files;
    }

    // =========================================================
    // TYPES
    // =========================================================

    private sealed class MasterException : Exception
    {
        public int Status { get; }
        public Dictionary<string, string>? Errors { get; }
        public MasterException(string message, int status = 400, Dictionary<string, string>? errors = null) : base(message)
        {
            Status = status;
            Errors = errors;
        }
    }

    public class Section
    {
        public int Id { get; set; }
        public string SectionKey { get; set; } = "";
        public string Title { get; set; } = "";
        public string TableName { get; set; } = "";
        public int IsMain { get; set; }
        public int IsMandatory { get; set; }
        public int IsFinalized { get; set; }
        public int SortOrder { get; set; }
    }

    public class FieldRules
    {
        public int? MinLength { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool? NoFuture { get; set; }
        public bool? NoPast { get; set; }
        public int? MinAgeYears { get; set; }
        public int? MaxAgeYears { get; set; }
        public string? MinDate { get; set; }
        public string? MaxDate { get; set; }
        public List<string>? Accept { get; set; }
        public int? MaxSizeMB { get; set; }
    }

    public class EmployeeField
    {
        public int Id { get; set; }
        public int SectionId { get; set; }
        public string ColumnName { get; set; } = "";
        public string Label { get; set; } = "";
        public string DataType { get; set; } = "TEXT";
        public int? MaxLength { get; set; }
        public int IsRequired { get; set; }
        public int IsUnique { get; set; }
        public int ForceUpper { get; set; }
        public int ShowInList { get; set; }
        public int IsSearchable { get; set; }
        public string? ValidationRegex { get; set; }
        public string? FormatHint { get; set; }
        public string? HelpText { get; set; }
        public string? OptionsJson { get; set; }
        public string? RulesJson { get; set; }
        public string? LookupSource { get; set; }
        public int SortOrder { get; set; }
        public int IsCore { get; set; }

        public bool Req => IsRequired == 1;
        public bool Uniq => IsUnique == 1;
        public bool Upper => ForceUpper == 1;
        public bool InList => ShowInList == 1;
        public bool Searchable => IsSearchable == 1;
        public bool Core => IsCore == 1;

        private FieldRules? _rules;
        public FieldRules Rules => _rules ??= ParseRules(RulesJson);
    }

    public class MasterConfig
    {
        public string CodeMode { get; set; } = "MANUAL";
        public string? CodePattern { get; set; }
    }

    public class LookupItem { public int Id { get; set; } public string Name { get; set; } = ""; public int Active { get; set; } = 1; }
    public class RoleItem { public int Id { get; set; } public string Name { get; set; } = ""; public string Code { get; set; } = ""; }

    private class NodeRow { public int NodeId { get; set; } public int OrganizationId { get; set; } }

    private class MappingRow
    {
        public int MappingId { get; set; }
        public int OrganizationId { get; set; }
        public int NodeId { get; set; }
        public DateTime EffectiveFrom { get; set; }
    }

    public class HistoryItem
    {
        public string Action { get; set; } = "";
        public string? ColumnName { get; set; }
        public string? Label { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime ChangedAt { get; set; }
        public long? ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
    }

    public class ListQuery
    {
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? NodeId { get; set; }
        public int? DepartmentId { get; set; }
        public int? DesignationId { get; set; }
        public string? Status { get; set; }

        public int? OrgId { get; set; }
    }

    public record OptionItem(string Value, string Label);
    public record SectionInput(string? Key, string Title, bool IsMandatory);

    public record FieldInput(
        int SectionId, string Label, string DataType, string? Key, int? MaxLength, bool IsRequired, bool IsUnique,
        bool ForceUpper, bool ShowInList, bool IsSearchable, string? ValidationRegex, string? FormatHint, string? HelpText,
        List<OptionItem>? Options, FieldRules? Rules);

    public record FieldUpdate(
        string Label, bool IsRequired, bool IsUnique, bool ForceUpper, bool ShowInList, bool IsSearchable,
        string? ValidationRegex, string? FormatHint, string? HelpText, List<OptionItem>? Options, FieldRules? Rules);

    public record SettingsInput(string CodeMode, string? CodePattern);
    public record OrderInput(List<int> Ids);

    public record AccountInput(int? RoleId, string? Username, string? Password);

    public record AddAccountRequest(int? RoleId, string? Username, string? Password);
    private record AccountPlan(int Index, int RoleId, string? Username, string Password, bool UseEmployeeCode);

    public class OrgInput { public int? NodeId { get; set; } public string? EffectiveFrom { get; set; } }

    public class EmployeeSaveRequest
    {
        public OrgInput? Organization { get; set; }
        public Dictionary<string, Dictionary<string, JsonElement>>? Sections { get; set; }
        public List<AccountInput>? Accounts { get; set; }   // create only
        public string? Version { get; set; }
    }

    private record AuditEntry(string SectionKey, string Column, string? Old, string? New, EmployeeField? F);

    // =========================================================
    // CONSTANTS
    // =========================================================

    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9_]{0,30}$", RegexOptions.Compiled);
    private static readonly Regex SectionKeyRx = new("^[a-z][a-z0-9_]{1,29}$", RegexOptions.Compiled);
    private static readonly Regex EmailRx = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
    private static readonly Regex PhoneRx = new(@"^[0-9+\-\s()]{6,20}$", RegexOptions.Compiled);
    private static readonly Regex UsernameRx = new(@"^[A-Za-z0-9._@\-]{3,50}$", RegexOptions.Compiled);
    private static readonly Regex FileIdRx = new("^[0-9a-f]{32}$", RegexOptions.Compiled);
    private static readonly Regex TokenRx = new(@"\{(YYYY|YY|MM|SEQ:\d{1,2})\}", RegexOptions.Compiled);
    private static readonly Regex SpacesRx = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex DupRx = new(@"Duplicate entry '(.*)' for key", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly HashSet<string> ReservedKeys = new() { "basic", "contact", "work", "organization", "master", "audit" };
    private static readonly HashSet<string> CreatableTypes = new() { "TEXT", "NUMBER", "DECIMAL", "DATE", "DROPDOWN", "BOOLEAN", "FILE", "EMAIL", "PHONE" };
    private static readonly HashSet<string> UniqueTypes = new() { "TEXT", "NUMBER", "EMAIL", "PHONE" };
    private static readonly HashSet<string> TextTypes = new() { "TEXT", "EMAIL", "PHONE" };
    private static readonly HashSet<string> RequiredLocked = new() { "employee_code", "first_name", "status" };
    private static readonly HashSet<string> FixedColumns = new() { "employee_code", "first_name", "last_name", "status" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions RulesJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SectionCols = "id AS Id, section_key AS SectionKey, title AS Title, table_name AS TableName, is_main AS IsMain, is_mandatory AS IsMandatory, is_finalized AS IsFinalized, sort_order AS SortOrder";

    private const string FieldCols = @"id AS Id, section_id AS SectionId, column_name AS ColumnName, label AS Label, data_type AS DataType,
        max_length AS MaxLength, is_required AS IsRequired, is_unique AS IsUnique, force_upper AS ForceUpper,
        show_in_list AS ShowInList, is_searchable AS IsSearchable, validation_regex AS ValidationRegex,
        format_hint AS FormatHint, help_text AS HelpText, options_json AS OptionsJson, rules_json AS RulesJson,
        lookup_source AS LookupSource, sort_order AS SortOrder, is_core AS IsCore";

    // =========================================================
    // PIPELINE
    // =========================================================

    private async Task<IActionResult> Run(Func<MySqlConnection, Task<object?>> action)
    {
        try
        {
            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            var result = await action(conn);
            return result is IActionResult ar ? ar : Ok(result);
        }
        catch (MasterException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Message, errors = ex.Errors });
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            var m = DupRx.Match(ex.Message);
            return Conflict(new { message = m.Success ? $"The value \"{m.Groups[1].Value}\" already exists for a unique field." : "A record with the same unique value already exists." });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, new { message = "Server error." });
        }
    }

    private async Task RequirePermissionAsync(IDbConnection conn, string permission)
    {
        if (EmployeeMenuKey == null) return;

        var user = this.GetAuthUser()!;
        var row = await conn.QueryFirstOrDefaultAsync(@"
            SELECT COALESCE(rmp.can_view,0) AS can_view, COALESCE(rmp.can_add,0) AS can_add, COALESCE(rmp.can_edit,0) AS can_edit
            FROM menus m
            LEFT JOIN role_menu_permissions rmp ON rmp.menu_id = m.id AND rmp.role_id = @roleId
            WHERE m.menu_key = @key AND m.is_active = 1
            LIMIT 1", new { roleId = user.RoleId, key = EmployeeMenuKey });

        var allowed = row is IDictionary<string, object> d && d.TryGetValue(permission, out var v) && Convert.ToInt32(v) == 1;
        if (!allowed) throw new MasterException("You do not have permission to perform this action.", 403);
    }

    private long CurrentUserId() => Convert.ToInt64(this.GetAuthUser()!.EmployeeId);
    private string? CurrentRole() => User.FindFirst("roleCode")?.Value;
    private bool IsSuperAdmin() => string.Equals(CurrentRole(), "SUPERADMIN", StringComparison.OrdinalIgnoreCase);
    // SUPERADMIN, or a role with EDIT permission on the employee add/list page
    private async Task<bool> CanManageAccountsAsync(IDbConnection c)
    {
        if (IsSuperAdmin()) return true;

        var roleId = this.GetAuthUser()!.RoleId;
        var n = await c.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)
            FROM menus m
            JOIN role_menu_permissions rmp ON rmp.menu_id = m.id AND rmp.role_id = @roleId
            WHERE m.is_active = 1 AND rmp.can_edit = 1 AND LOWER(m.route_path) IN (@r1, @r2)",
            new { roleId, r1 = AddPageRoute.ToLowerInvariant(), r2 = ListPageRoute.ToLowerInvariant() });
        return n > 0;
    }
    private Task<DataScopeInfo> ScopeAsync(IDbConnection c) => DataScope.ForUserAsync(c, User, CurrentUserId());

    // 404 when the employee does not exist, 403 when it sits outside the caller's data scope
    private static async Task EnsureAccessAsync(IDbConnection c, DataScopeInfo scope, long employeeId)
    {
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM employee_master WHERE employee_id=@employeeId", new { employeeId }) == 0)
            throw new MasterException("Employee not found.", 404);
        if (scope.Unrestricted) return;

        var node = await c.ExecuteScalarAsync<int?>(@"
            SELECT node_id FROM master_employee_organization_mapping
            WHERE employee_id=@employeeId AND is_current=1 ORDER BY mapping_id DESC LIMIT 1", new { employeeId });
        if (node == null || !scope.Contains(node.Value))
            throw new MasterException("You do not have access to this employee.", 403);
    }

    // =========================================================
    // ENDPOINTS - setup
    // =========================================================

    [HttpGet]
    public Task<IActionResult> Master() => Run(async c => await GetMasterAsync(c));

    [HttpGet("lookups")]
    public Task<IActionResult> Lookups() => Run(async c =>
    {
        var scope = await ScopeAsync(c);
        var org = await DataScope.OrgLookupsAsync(c, scope);

        var roles = (await c.QueryAsync<RoleItem>("SELECT id AS Id, role_name AS Name, role_code AS Code FROM roles WHERE active=1 ORDER BY sequence, role_name")).ToList();
        if (!IsSuperAdmin()) roles = roles.Where(r => !r.Code.Equals("SUPERADMIN", StringComparison.OrdinalIgnoreCase)).ToList();

        return new
        {
            departments = await c.QueryAsync<LookupItem>("SELECT id AS Id, dep_name AS Name, active AS Active FROM departments ORDER BY sequence, dep_name"),
            designations = await c.QueryAsync<LookupItem>("SELECT id AS Id, name AS Name, active AS Active FROM designations ORDER BY sequence, name"),
            organizations = org.Organizations,
            nodeTypes = org.NodeTypes,
            nodes = org.Nodes,
            scope = org.Scope,
            roles,
            me = new { canManageAccounts = await CanManageAccountsAsync(c), isSuperAdmin = IsSuperAdmin() }
        };
    });

    [HttpPost("sections")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> CreateSection([FromBody] SectionInput body)
        => Run(async c => new { id = await CreateSectionAsync(c, body) });

    [HttpPut("sections/{id:int}")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> UpdateSection(int id, [FromBody] SectionInput body)
        => Run(async c => { await UpdateSectionAsync(c, id, body); return new { message = "Saved." }; });

    [HttpDelete("sections/{id:int}")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> DeleteSection(int id)
        => Run(async c => { await DeleteSectionAsync(c, id); return new { message = "Deleted." }; });

    [HttpPost("sections/{id:int}/finalize")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> FinalizeSection(int id)
    {
        var userId = CurrentUserId();
        return Run(async c => { await FinalizeSectionAsync(c, id, userId); return new { message = "Section finalized." }; });
    }

    [HttpPut("sections/{id:int}/fields/order")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> Reorder(int id, [FromBody] OrderInput body)
        => Run(async c => { await ReorderAsync(c, id, body.Ids); return new { message = "Saved." }; });

    [HttpPost("fields")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> CreateField([FromBody] FieldInput body)
        => Run(async c => new { id = await CreateFieldAsync(c, body) });

    [HttpPut("fields/{id:int}")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> UpdateField(int id, [FromBody] FieldUpdate body)
        => Run(async c => { await UpdateFieldAsync(c, id, body); return new { message = "Saved." }; });

    [HttpDelete("fields/{id:int}")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> DeleteField(int id)
        => Run(async c => { await DeleteFieldAsync(c, id); return new { message = "Deleted." }; });

    [HttpPut("settings")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> Settings([FromBody] SettingsInput body)
        => Run(async c => { await SaveSettingsAsync(c, body); return new { message = "Saved." }; });

    // =========================================================
    // ENDPOINTS - employees (all limited to the caller's data scope)
    // =========================================================

    [HttpGet("employees")]
    public Task<IActionResult> List([FromQuery] ListQuery q)
        => Run(async c =>
        {
            await RequirePermissionAsync(c, "can_view");
            return await ListEmployeesAsync(c, q, await ScopeAsync(c));
        });

    // GET me - the logged-in employee's own profile (read-only, nothing outside their own record)
    [HttpGet("me")]
    public Task<IActionResult> MyProfile()
        => Run(async c => await BuildMyProfileAsync(c, CurrentUserId()));

    // GET me/files/{fileId}/link?download=true - signed link to one of the caller's OWN documents
    [HttpGet("me/files/{fileId}/link")]
    public Task<IActionResult> MyFileLink(string fileId, [FromQuery] bool download = false)
        => Run(async c =>
        {
            var doc = (await LoadOwnDocsAsync(c, CurrentUserId())).FirstOrDefault(d => d.FileId == fileId)
                ?? throw new MasterException("Document not found.", 404);

            try
            {
                var signed = await _files.SignAsync(fileId, download, download ? doc.FileName : null);
                return new { url = signed.url, expiresAt = signed.expiresAt };
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new MasterException("The file is no longer available.", 404);
            }
            catch (HttpRequestException)
            {
                throw new MasterException("File storage service is unreachable.", 502);
            }
        });

    // =========================================================
    // MY PROFILE (the logged-in employee's own record, read-only)
    // =========================================================

    private static bool IsPhotoField(EmployeeField f) =>
    f.DataType == "FILE" &&
    (f.ColumnName == ProfilePhotoColumn || string.Equals(f.Label.Trim(), ProfilePhotoLabel, StringComparison.OrdinalIgnoreCase));

    private static bool IsImageName(string name) =>
        new[] { "jpg", "jpeg", "png", "gif", "webp" }.Contains(Path.GetExtension(name).TrimStart('.').ToLowerInvariant());

    // every uploaded document of one employee, across all finalized sections
    private static async Task<List<OwnDoc>> LoadOwnDocsAsync(IDbConnection c, long employeeId)
    {
        var secs = (await GetSectionsAsync(c)).Where(s => s.IsFinalized == 1).ToList();
        var fileFields = (await GetFieldsAsync(c)).Where(f => f.DataType == "FILE").ToList();

        var docs = new List<OwnDoc>();
        foreach (var s in secs)
        {
            var sf = fileFields.Where(f => f.SectionId == s.Id).ToList();
            if (sf.Count == 0) continue;

            var sql = s.IsMain == 1
                ? "SELECT * FROM employee_master WHERE employee_id=@employeeId"
                : $"SELECT * FROM `{s.TableName}` WHERE employee_id=@employeeId";
            var row = await c.QueryFirstOrDefaultAsync(sql, new { employeeId }) as IDictionary<string, object>;
            if (row == null) continue;

            foreach (var f in sf)
            {
                row.TryGetValue(f.ColumnName, out var v);
                var (fid, name) = FileParts(Convert.ToString(v));
                if (fid != null) docs.Add(new OwnDoc(s.Title, f.Label, fid, string.IsNullOrEmpty(name) ? "File" : name, IsPhotoField(f)));
            }
        }
        return docs;
    }

    private static async Task<string?> LookupNameAsync(IDbConnection c, EmployeeField f, object? v)
    {
        if (v == null || v is DBNull || f.LookupSource == null || !Lookup.TryGetValue(f.LookupSource, out var m)) return null;
        return await c.ExecuteScalarAsync<string?>($"SELECT {m.NameCol} FROM {m.Table} WHERE {m.IdCol}=@id", new { id = Convert.ToInt32(v) });
    }

    private static async Task<object> BuildMyProfileAsync(IDbConnection c, long employeeId)
    {
        var main = await c.QueryFirstOrDefaultAsync("SELECT * FROM employee_master WHERE employee_id=@employeeId", new { employeeId }) as IDictionary<string, object>
            ?? throw new MasterException("Your employee record was not found.", 404);

        var secs = (await GetSectionsAsync(c)).Where(s => s.IsFinalized == 1).ToList();
        var all = await GetFieldsAsync(c);

        // details, one card per section (documents are listed separately)
        var sections = new List<object>();
        foreach (var s in secs)
        {
            var row = s.IsMain == 1
                ? main
                : await c.QueryFirstOrDefaultAsync($"SELECT * FROM `{s.TableName}` WHERE employee_id=@employeeId", new { employeeId }) as IDictionary<string, object>;

            var items = new List<object>();
            foreach (var f in all.Where(f => f.SectionId == s.Id && f.DataType != "FILE"))
            {
                object? v = null;
                row?.TryGetValue(f.ColumnName, out v);
                var text = f.DataType == "LOOKUP" ? await LookupNameAsync(c, f, v) : DisplayValue(f, v);
                items.Add(new { label = f.Label, type = f.DataType, value = text });
            }
            if (items.Count > 0) sections.Add(new { key = s.SectionKey, title = s.Title, items });
        }

        // organization: the organization name plus one entry per level (Unit, Workplace ...), named after the node type
        object? organization = null;
        var map = await CurrentMappingAsync(c, employeeId);
        if (map != null)
        {
            var look = await DataScope.OrgLookupsAsync(c, new DataScopeInfo { Unrestricted = true });
            var byId = look.Nodes.ToDictionary(x => x.Id);
            var typeName = look.NodeTypes.ToDictionary(x => x.Id, x => x.Name);

            var chain = new List<ScopedNode>();
            var cur = byId.TryGetValue(map.NodeId, out var start) ? start : null;
            var guard = 0;
            while (cur != null && guard++ < 50)
            {
                chain.Insert(0, cur);
                cur = cur.ParentId != null && byId.TryGetValue(cur.ParentId.Value, out var up) ? up : null;
            }

            var levels = new List<object>();
            foreach (var node in chain.Where(x => !x.IsRoot)) // the real root only repeats the organization name
            {
                var type = node.TypeId != null && typeName.TryGetValue(node.TypeId.Value, out var tn) ? tn : "Level";
                levels.Add(new { type, name = node.Name });
            }

            organization = new
            {
                name = look.Organizations.FirstOrDefault(o => o.Id == map.OrganizationId)?.Name,
                levels,
                since = map.EffectiveFrom.ToString("yyyy-MM-dd")
            };
        }

        var roles = (await c.QueryAsync<string>(@"
            SELECT r.role_name FROM user_accounts ua JOIN roles r ON r.id = ua.role_id
            WHERE ua.employee_id=@employeeId AND ua.is_active=1 ORDER BY ua.is_default DESC", new { employeeId })).ToList();

        var docs = await LoadOwnDocsAsync(c, employeeId);
        // the profile picture is shown at the top; used only when the field exists, has a file and that file is an image
        var photoDoc = docs.FirstOrDefault(d => d.IsPhoto && IsImageName(d.FileName));

        return new
        {
            employeeId,
            employeeCode = Convert.ToString(main["employee_code"]),
            firstName = Convert.ToString(main["first_name"]),
            lastName = main.TryGetValue("last_name", out var ln) && ln != null ? Convert.ToString(ln) : null,
            status = Convert.ToString(main["status"]),
            organization,
            sections,
            photo = photoDoc == null ? null : new { fileId = photoDoc.FileId, fileName = photoDoc.FileName },
            documents = docs.Where(d => d != photoDoc).Select(d => new { section = d.Section, label = d.Label, fileId = d.FileId, fileName = d.FileName }).ToList(),
            roles
        };
    }
    [HttpGet("employees/export")]
    public Task<IActionResult> Export([FromQuery] ListQuery q)
        => Run(async c =>
        {
            await RequirePermissionAsync(c, "can_view");
            var bytes = await BuildCsvAsync(c, q, await ScopeAsync(c));
            return File(bytes, "text/csv", $"employees-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        });

    [HttpGet("employees/{id:long}")]
    public Task<IActionResult> Get(long id)
        => Run(async c =>
        {
            await RequirePermissionAsync(c, "can_view");
            await EnsureAccessAsync(c, await ScopeAsync(c), id);
            return await GetEmployeeAsync(c, id) ?? throw new MasterException("Employee not found.", 404);
        });

    [HttpGet("employees/{id:long}/history")]
    public Task<IActionResult> History(long id)
        => Run(async c =>
        {
            await RequirePermissionAsync(c, "can_view");
            await EnsureAccessAsync(c, await ScopeAsync(c), id);
            return await c.QueryAsync<HistoryItem>(@"
                SELECT a.action AS Action, a.column_name AS ColumnName,
                       CASE WHEN a.column_name = '__org' THEN 'Organization' ELSE d.label END AS Label,
                       a.old_value AS OldValue, a.new_value AS NewValue, a.changed_at AS ChangedAt, a.changed_by AS ChangedBy,
                       NULLIF(TRIM(CONCAT(COALESCE(u.first_name,''),' ',COALESCE(u.last_name,''))),'') AS ChangedByName
                FROM employee_audit_log a
                LEFT JOIN employee_sections s ON s.section_key = a.section_key
                LEFT JOIN employee_field_definitions d ON d.section_id = s.id AND d.column_name = a.column_name
                LEFT JOIN employee_master u ON u.employee_id = a.changed_by
                WHERE a.employee_id = @id
                ORDER BY a.id DESC
                LIMIT 200", new { id });
        });

    [HttpPost("employees")]
    public Task<IActionResult> Create([FromBody] EmployeeSaveRequest body)
    {
        var userId = CurrentUserId();
        return Run(async c =>
        {
            await RequirePermissionAsync(c, "can_add");
            return new { employeeId = await CreateEmployeeAsync(c, body, userId, await ScopeAsync(c)) };
        });
    }

    [HttpPut("employees/{id:long}")]
    public Task<IActionResult> Update(long id, [FromBody] EmployeeSaveRequest body)
    {
        var userId = CurrentUserId();
        return Run(async c =>
        {
            await RequirePermissionAsync(c, "can_edit");
            await UpdateEmployeeAsync(c, id, body, userId, await ScopeAsync(c));
            return new { message = "Saved." };
        });
    }

    // GET employees/{id}/accounts - logins of one employee (needs edit access on the employee pages)
    [HttpGet("employees/{id:long}/accounts")]
    public Task<IActionResult> GetAccounts(long id)
        => Run(async c =>
        {
            await EnsureAccessAsync(c, await ScopeAsync(c), id);
            if (!await CanManageAccountsAsync(c)) throw new MasterException("You do not have permission to view login accounts.", 403);

            return (await c.QueryAsync(@"
                SELECT ua.id, ua.username, ua.is_active, ua.is_default, r.role_code, r.role_name
                FROM user_accounts ua JOIN roles r ON r.id = ua.role_id
                WHERE ua.employee_id = @id", new { id })).ToDictList();
        });

    // POST employees/{id}/accounts - add ONE more login to an existing employee
    [HttpPost("employees/{id:long}/accounts")]
    public Task<IActionResult> AddAccount(long id, [FromBody] AddAccountRequest body)
        => Run(async c =>
        {
            await RequirePermissionAsync(c, "can_edit");
            await EnsureAccessAsync(c, await ScopeAsync(c), id);

            var errors = new Dictionary<string, string>();
            var plans = await ValidateAccountsAsync(c, new List<AccountInput> { new(body.RoleId, body.Username, body.Password) },
                await CanManageAccountsAsync(c), IsSuperAdmin(), errors);

            if (plans.Count == 1 && await c.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM user_accounts WHERE employee_id=@id AND role_id=@roleId", new { id, roleId = plans[0].RoleId }) > 0)
                errors["accounts.0.roleId"] = "This employee already has a login for that role.";

            if (errors.Count > 0) throw new MasterException(errors.Values.First(), 422, errors);

            var code = await c.ExecuteScalarAsync<string>("SELECT employee_code FROM employee_master WHERE employee_id=@id", new { id }) ?? "";
            var hasAny = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_accounts WHERE employee_id=@id", new { id }) > 0;

            using var tx = c.BeginTransaction();
            await InsertAccountsAsync(c, tx, id, code, plans, firstIsDefault: !hasAny);
            tx.Commit();
            return new { message = "Account added." };
        });

    // =========================================================
    // READ HELPERS
    // =========================================================

    private static async Task<List<Section>> GetSectionsAsync(IDbConnection c) =>
        (await c.QueryAsync<Section>($"SELECT {SectionCols} FROM employee_sections ORDER BY sort_order, id")).ToList();

    private static async Task<Section> GetSectionAsync(IDbConnection c, int id) =>
        await c.QueryFirstOrDefaultAsync<Section>($"SELECT {SectionCols} FROM employee_sections WHERE id=@id", new { id })
        ?? throw new MasterException("Section not found.", 404);

    private static async Task<List<EmployeeField>> GetFieldsAsync(IDbConnection c) =>
        (await c.QueryAsync<EmployeeField>($"SELECT {FieldCols} FROM employee_field_definitions ORDER BY sort_order, id")).ToList();

    private static async Task<MasterConfig> GetConfigAsync(IDbConnection c) =>
        await c.QueryFirstAsync<MasterConfig>("SELECT code_mode AS CodeMode, code_pattern AS CodePattern FROM employee_master_config WHERE id=1");

    private static Section AssertDraft(Section s)
    {
        if (s.IsFinalized == 1) throw new MasterException($"'{s.Title}' is finalized and can no longer be changed.", 409);
        return s;
    }

    private static FieldRules ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new FieldRules();
        try { return JsonSerializer.Deserialize<FieldRules>(json, JsonOpts) ?? new FieldRules(); }
        catch { return new FieldRules(); }
    }

    private static List<OptionItem> ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<OptionItem>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }

    private static object FieldDto(EmployeeField f) => new
    {
        id = f.Id,
        sectionId = f.SectionId,
        columnName = f.ColumnName,
        label = f.Label,
        dataType = f.DataType,
        maxLength = f.MaxLength,
        isRequired = f.Req,
        isUnique = f.Uniq,
        forceUpper = f.Upper,
        showInList = f.InList,
        isSearchable = f.Searchable,
        validationRegex = f.ValidationRegex,
        formatHint = f.FormatHint,
        helpText = f.HelpText,
        options = ParseOptions(f.OptionsJson),
        rules = f.Rules,
        lookupSource = f.LookupSource,
        sortOrder = f.SortOrder,
        isCore = f.Core
    };

    private static async Task<object> GetMasterAsync(IDbConnection c)
    {
        var cfg = await GetConfigAsync(c);
        var secs = await GetSectionsAsync(c);
        var fields = await GetFieldsAsync(c);

        return new
        {
            config = new { codeMode = cfg.CodeMode, codePattern = cfg.CodePattern },
            sections = secs.Select(s => new
            {
                id = s.Id,
                key = s.SectionKey,
                title = s.Title,
                isMain = s.IsMain == 1,
                isMandatory = s.IsMandatory == 1,
                isFinalized = s.IsFinalized == 1,
                fields = fields.Where(f => f.SectionId == s.Id).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).Select(FieldDto).ToList()
            }).ToList()
        };
    }

    // =========================================================
    // SECTIONS (schema)
    // =========================================================

    private static string Slug(string s) => Regex.Replace(s.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<int> CreateSectionAsync(IDbConnection c, SectionInput i)
    {
        var title = (i.Title ?? "").Trim();
        if (title.Length == 0 || title.Length > 100) throw new MasterException("Section title is required (max 100 characters).");

        var key = Slug(string.IsNullOrWhiteSpace(i.Key) ? title : i.Key);
        if (!SectionKeyRx.IsMatch(key) || ReservedKeys.Contains(key))
            throw new MasterException("Section key must start with a letter, use only letters/digits/underscores (max 30) and not be a reserved name.");

        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM employee_sections WHERE section_key=@key", new { key }) > 0)
            throw new MasterException("A section with this key already exists.", 409);

        var table = "emp_sec_" + key;
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table", new { table }) > 0)
            throw new MasterException($"A table named '{table}' already exists.", 409);

        await c.ExecuteAsync($@"CREATE TABLE `{table}` (
            employee_id INT NOT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT NULL,
            updated_by BIGINT NULL,
            PRIMARY KEY (employee_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");

        try
        {
            try
            {
                await c.ExecuteAsync($"ALTER TABLE `{table}` ADD CONSTRAINT `fk_{table}_emp` FOREIGN KEY (employee_id) REFERENCES employee_master(employee_id) ON DELETE CASCADE");
            }
            catch (Exception ex) { Console.Error.WriteLine($"FK for {table} not created: {ex.Message}"); } // e.g. employee_master is not InnoDB

            var order = await c.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(sort_order),0)+10 FROM employee_sections");
            await c.ExecuteAsync(@"INSERT INTO employee_sections (section_key, title, table_name, is_main, is_mandatory, is_finalized, sort_order)
                VALUES (@key, @title, @table, 0, @m, 0, @order)", new { key, title, table, m = i.IsMandatory ? 1 : 0, order });
            return await c.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
        }
        catch
        {
            try { await c.ExecuteAsync($"DROP TABLE `{table}`"); } catch { /* best effort */ }
            throw;
        }
    }

    private static async Task UpdateSectionAsync(IDbConnection c, int id, SectionInput i)
    {
        var s = AssertDraft(await GetSectionAsync(c, id));
        var title = (i.Title ?? "").Trim();
        if (title.Length == 0 || title.Length > 100) throw new MasterException("Section title is required (max 100 characters).");

        await c.ExecuteAsync("UPDATE employee_sections SET title=@title, is_mandatory=@m WHERE id=@id",
            new { id, title, m = s.IsMain == 1 ? s.IsMandatory : (i.IsMandatory ? 1 : 0) });
    }

    private static async Task DeleteSectionAsync(IDbConnection c, int id)
    {
        var s = AssertDraft(await GetSectionAsync(c, id));
        if (s.IsMain == 1) throw new MasterException("Built-in sections cannot be deleted.");

        await c.ExecuteAsync("DELETE FROM employee_field_definitions WHERE section_id=@id", new { id });
        await c.ExecuteAsync("DELETE FROM employee_sections WHERE id=@id", new { id });
        await c.ExecuteAsync($"DROP TABLE IF EXISTS `{s.TableName}`");
    }

    private static async Task FinalizeSectionAsync(IDbConnection c, int id, long userId)
    {
        var s = AssertDraft(await GetSectionAsync(c, id));

        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM employee_field_definitions WHERE section_id=@id", new { id }) == 0)
            throw new MasterException("Add at least one field before finalizing this section.");

        if (s.SectionKey == "basic")
        {
            var cfg = await GetConfigAsync(c);
            if (cfg.CodeMode == "AUTO") ValidateCodePattern(cfg.CodePattern);
        }

        await c.ExecuteAsync("UPDATE employee_sections SET is_finalized=1, finalized_by=@userId, finalized_at=NOW() WHERE id=@id", new { id, userId });
    }

    // =========================================================
    // FIELDS (schema, draft sections only)
    // =========================================================

    private static void ValidateLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > 100)
            throw new MasterException("Label is required (max 100 characters).");
    }

    private static void AssertRegex(string pattern)
    {
        try { _ = new Regex($"^(?:{pattern})$", RegexOptions.None, TimeSpan.FromMilliseconds(200)); }
        catch (ArgumentException) { throw new MasterException("The format pattern is not a valid regular expression."); }
    }

    private static string SerializeOptions(List<OptionItem>? options)
    {
        var list = (options ?? new())
            .Select(o => new OptionItem((o.Value ?? "").Trim(), (o.Label ?? "").Trim()))
            .Where(o => o.Value.Length > 0 && o.Label.Length > 0).ToList();

        if (list.Count == 0) throw new MasterException("Add at least one dropdown option.");
        if (list.Any(o => o.Value.Length > 100)) throw new MasterException("Option values can be at most 100 characters.");
        if (list.Select(o => o.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
            throw new MasterException("Dropdown options must be unique.");

        return JsonSerializer.Serialize(list, JsonOpts);
    }

    private static string? NormDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new MasterException("Dates in rules must be valid.");
        return d.ToString("yyyy-MM-dd");
    }

    // Keeps only the rules that make sense for the type; returns null when there are none
    private static string? NormalizeRules(string type, FieldRules? r)
    {
        if (r == null) return null;
        var o = new FieldRules();

        switch (type)
        {
            case "TEXT":
            case "EMAIL":
            case "PHONE":
                if (r.MinLength is > 0)
                {
                    if (r.MinLength > 255) throw new MasterException("Minimum length is too large.");
                    o.MinLength = r.MinLength;
                }
                break;

            case "NUMBER":
            case "DECIMAL":
                o.Min = r.Min;
                o.Max = r.Max;
                if (o.Min != null && o.Max != null && o.Min > o.Max) throw new MasterException("Minimum value cannot be greater than the maximum.");
                break;

            case "DATE":
                if (r.NoFuture == true) o.NoFuture = true;
                if (r.NoPast == true) o.NoPast = true;
                if (o.NoFuture == true && o.NoPast == true) throw new MasterException("A date cannot be both 'no future' and 'no past'.");
                if (r.MinAgeYears is > 0) o.MinAgeYears = Math.Min(r.MinAgeYears.Value, 150);
                if (r.MaxAgeYears is > 0) o.MaxAgeYears = Math.Min(r.MaxAgeYears.Value, 150);
                if (o.MinAgeYears > o.MaxAgeYears) throw new MasterException("Minimum age cannot be greater than maximum age.");
                o.MinDate = NormDate(r.MinDate);
                o.MaxDate = NormDate(r.MaxDate);
                if (o.MinDate != null && o.MaxDate != null && string.CompareOrdinal(o.MinDate, o.MaxDate) > 0)
                    throw new MasterException("Earliest date cannot be after the latest date.");
                break;

            case "FILE":
                var acc = (r.Accept ?? new()).Select(x => x.Trim().TrimStart('.').ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
                if (acc.Any(x => !Regex.IsMatch(x, "^[a-z0-9]{1,10}$"))) throw new MasterException("File types must be plain extensions like pdf, jpg, png.");
                if (acc.Count > 0) o.Accept = acc;
                if (r.MaxSizeMB is > 0) o.MaxSizeMB = Math.Clamp(r.MaxSizeMB.Value, 1, 50);
                break;
        }

        var json = JsonSerializer.Serialize(o, RulesJsonOpts);
        return json == "{}" ? null : json;
    }

    private static async Task<int> CreateFieldAsync(IDbConnection c, FieldInput i)
    {
        var s = AssertDraft(await GetSectionAsync(c, i.SectionId));
        ValidateLabel(i.Label);

        var type = (i.DataType ?? "").ToUpperInvariant();
        if (!CreatableTypes.Contains(type)) throw new MasterException("Unsupported field type.");

        var key = Slug(string.IsNullOrWhiteSpace(i.Key) ? i.Label : i.Key);
        if (!KeyPattern.IsMatch(key))
            throw new MasterException("Field key can only use letters, digits and underscores (max 31 characters).");
        var column = "cf_" + key; // validated identifier - the only thing ever interpolated into DDL
        var table = s.TableName;   // comes from our own sections table

        if (i.IsUnique && !UniqueTypes.Contains(type)) throw new MasterException("This field type cannot be unique.");

        var regex = TextTypes.Contains(type) ? NullIfEmpty(i.ValidationRegex) : null;
        if (regex != null) AssertRegex(regex);
        var options = type == "DROPDOWN" ? SerializeOptions(i.Options) : null;
        var rulesJson = NormalizeRules(type, i.Rules);

        if (await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND column_name=@column",
                new { table, column }) > 0)
            throw new MasterException($"A column named '{column}' already exists in this table.", 409);

        // ---- guards for wide tables ----
        var customCount = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM employee_field_definitions d JOIN employee_sections x ON x.id=d.section_id WHERE x.table_name=@table AND d.is_core=0", new { table });
        if (customCount >= MaxCustomFields) throw new MasterException($"A maximum of {MaxCustomFields} custom fields per table is supported.");

        var maxLen = Math.Clamp(i.MaxLength ?? 100, 1, 255);
        var addBytes = type switch { "TEXT" => maxLen * 4, "EMAIL" => 600, "PHONE" => 80, "DROPDOWN" => 400, _ => 0 };
        var usedBytes = await c.ExecuteScalarAsync<long>(@"
            SELECT COALESCE(SUM(CASE WHEN data_type IN ('varchar','char') THEN character_maximum_length * 4 ELSE 0 END), 0)
            FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table", new { table });
        if (usedBytes + addBytes > 56000)
            throw new MasterException("This table has no room for more text columns. Use shorter lengths or fewer text fields.");

        var sqlType = type switch
        {
            "TEXT" => $"VARCHAR({maxLen})",
            "EMAIL" => "VARCHAR(150)",
            "PHONE" => "VARCHAR(20)",
            "NUMBER" => "BIGINT",
            "DECIMAL" => "DECIMAL(15,2)",
            "DATE" => "DATE",
            "BOOLEAN" => "TINYINT(1)",
            "DROPDOWN" => "VARCHAR(100)",
            _ => "TEXT" // FILE: {"fileId","fileName"}
        };

        await c.ExecuteAsync($"ALTER TABLE `{table}` ADD COLUMN `{column}` {sqlType} NULL");
        try
        {
            if (i.IsUnique)
                await c.ExecuteAsync($"ALTER TABLE `{table}` ADD UNIQUE KEY `uq_{column}` (`{column}`)");

            var order = await c.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(sort_order),0)+10 FROM employee_field_definitions WHERE section_id=@sid", new { sid = s.Id });

            await c.ExecuteAsync(@"
                INSERT INTO employee_field_definitions
                  (section_id, column_name, label, data_type, max_length, is_required, is_unique, force_upper, show_in_list, is_searchable,
                   validation_regex, format_hint, help_text, options_json, rules_json, sort_order, is_core)
                VALUES
                  (@sid, @column, @label, @type, @maxLen, @req, @uniq, @upper, @inList, @searchable,
                   @regex, @hint, @help, @options, @rules, @order, 0)",
                new
                {
                    sid = s.Id,
                    column,
                    label = i.Label.Trim(),
                    type,
                    maxLen = type == "TEXT" ? maxLen : (int?)null,
                    req = i.IsRequired ? 1 : 0,
                    uniq = i.IsUnique ? 1 : 0,
                    upper = i.ForceUpper && TextTypes.Contains(type) ? 1 : 0,
                    inList = i.ShowInList ? 1 : 0,
                    searchable = i.IsSearchable && TextTypes.Contains(type) ? 1 : 0,
                    regex,
                    hint = NullIfEmpty(i.FormatHint),
                    help = NullIfEmpty(i.HelpText),
                    options,
                    rules = rulesJson,
                    order
                });

            return await c.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
        }
        catch
        {
            try { await c.ExecuteAsync($"ALTER TABLE `{table}` DROP COLUMN `{column}`"); } catch { /* best effort */ }
            throw;
        }
    }

    // Presentation + rules only. To change type / length / uniqueness of a custom field: delete and re-create (draft only).
    private static async Task UpdateFieldAsync(IDbConnection c, int id, FieldUpdate u)
    {
        var f = await c.QueryFirstOrDefaultAsync<EmployeeField>($"SELECT {FieldCols} FROM employee_field_definitions WHERE id=@id", new { id })
            ?? throw new MasterException("Field not found.", 404);
        AssertDraft(await GetSectionAsync(c, f.SectionId));
        ValidateLabel(u.Label);

        var required = f.Core && RequiredLocked.Contains(f.ColumnName) ? f.Req : u.IsRequired;
        var unique = f.Core ? (f.ColumnName is "email" or "phone" ? u.IsUnique : f.Uniq) : f.Uniq;
        var regex = TextTypes.Contains(f.DataType) ? NullIfEmpty(u.ValidationRegex) : null;
        if (regex != null) AssertRegex(regex);
        var options = f.DataType == "DROPDOWN" && !f.Core ? SerializeOptions(u.Options) : f.OptionsJson;
        var rulesJson = NormalizeRules(f.DataType, u.Rules);

        await c.ExecuteAsync(@"
            UPDATE employee_field_definitions
            SET label=@label, is_required=@required, is_unique=@unique, force_upper=@upper, show_in_list=@inList,
                is_searchable=@searchable, validation_regex=@regex, format_hint=@hint, help_text=@help,
                options_json=@options, rules_json=@rules
            WHERE id=@id",
            new
            {
                id,
                label = u.Label.Trim(),
                required = required ? 1 : 0,
                unique = unique ? 1 : 0,
                upper = u.ForceUpper && TextTypes.Contains(f.DataType) ? 1 : 0,
                inList = u.ShowInList ? 1 : 0,
                searchable = u.IsSearchable && TextTypes.Contains(f.DataType) ? 1 : 0,
                regex,
                hint = NullIfEmpty(u.FormatHint),
                help = NullIfEmpty(u.HelpText),
                options,
                rules = rulesJson
            });
    }

    private static async Task DeleteFieldAsync(IDbConnection c, int id)
    {
        var f = await c.QueryFirstOrDefaultAsync<EmployeeField>($"SELECT {FieldCols} FROM employee_field_definitions WHERE id=@id", new { id })
            ?? throw new MasterException("Field not found.", 404);
        var s = AssertDraft(await GetSectionAsync(c, f.SectionId));
        if (f.Core) throw new MasterException("Base fields cannot be deleted.");

        await c.ExecuteAsync($"ALTER TABLE `{s.TableName}` DROP COLUMN `{f.ColumnName}`"); // also drops its unique index
        await c.ExecuteAsync("DELETE FROM employee_field_definitions WHERE id=@id", new { id });
    }

    private static async Task ReorderAsync(IDbConnection c, int sectionId, List<int> ids)
    {
        AssertDraft(await GetSectionAsync(c, sectionId));

        var existing = (await c.QueryAsync<int>("SELECT id FROM employee_field_definitions WHERE section_id=@sectionId", new { sectionId })).OrderBy(x => x).ToList();
        if (!existing.SequenceEqual(ids.OrderBy(x => x))) throw new MasterException("The order must list every field of the section exactly once.");

        for (var i = 0; i < ids.Count; i++)
            await c.ExecuteAsync("UPDATE employee_field_definitions SET sort_order=@o WHERE id=@id", new { o = (i + 1) * 10, id = ids[i] });
    }

    // ---- employee code settings (locked once Basic Details is finalized) ----

    private static string RenderCode(string pattern, long seq, DateTime now) =>
        TokenRx.Replace(pattern, m => m.Groups[1].Value switch
        {
            "YYYY" => now.ToString("yyyy"),
            "YY" => now.ToString("yy"),
            "MM" => now.ToString("MM"),
            var s => seq.ToString().PadLeft(int.Parse(s[4..]), '0') // "SEQ:5"
        });

    private static void ValidateCodePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new MasterException("Enter a code pattern, e.g. EMP-{YYYY}-{SEQ:5}.");

        if (TokenRx.Matches(pattern).Count(m => m.Groups[1].Value.StartsWith("SEQ")) != 1)
            throw new MasterException("The pattern must contain exactly one {SEQ:n} token.");

        if (!Regex.IsMatch(TokenRx.Replace(pattern, ""), @"^[A-Za-z0-9\-_/.]*$"))
            throw new MasterException("Besides {YYYY}, {YY}, {MM} and {SEQ:n}, only letters, digits and - _ / . are allowed.");

        if (RenderCode(pattern, 1, DateTime.Now).Length > 30)
            throw new MasterException("The generated code would exceed 30 characters.");
    }

    private static async Task SaveSettingsAsync(IDbConnection c, SettingsInput s)
    {
        var basic = await c.QueryFirstOrDefaultAsync<Section>($"SELECT {SectionCols} FROM employee_sections WHERE section_key='basic'");
        if (basic == null || basic.IsFinalized == 1) throw new MasterException("The employee code rule is locked because Basic Details is finalized.", 409);

        var mode = (s.CodeMode ?? "").ToUpperInvariant();
        if (mode != "MANUAL" && mode != "AUTO") throw new MasterException("Invalid code mode.");
        if (mode == "AUTO") ValidateCodePattern(s.CodePattern);

        await c.ExecuteAsync("UPDATE employee_master_config SET code_mode=@mode, code_pattern=@pattern WHERE id=1",
            new { mode, pattern = mode == "AUTO" ? s.CodePattern!.Trim() : null });
    }

    // =========================================================
    // VALUE HELPERS
    // =========================================================

    private static string? Raw(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => string.IsNullOrWhiteSpace(e.GetString()) ? null : e.GetString()!.Trim(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetRawText()
    };

    private static bool IsTouched(Dictionary<string, JsonElement> input) =>
        input.Values.Any(v => { var r = Raw(v); return r != null && r != "false"; });

    private static bool MatchesFormat(string pattern, string value)
    {
        try { return Regex.IsMatch(value, $"^(?:{pattern})$", RegexOptions.None, TimeSpan.FromMilliseconds(200)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static (string? Id, string? Name) FileParts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? Get(string p) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Get("fileId"), Get("fileName") ?? Get("originalFileName")); // old rows used originalFileName
        }
        catch (JsonException) { return (null, null); }
    }

    // value as shown to humans (list, export, audit)
    private static string? DisplayValue(EmployeeField f, object? v)
    {
        if (v == null || v is DBNull) return null;
        var inv = CultureInfo.InvariantCulture;

        switch (f.DataType)
        {
            case "DROPDOWN":
                {
                    var s = Convert.ToString(v, inv);
                    return ParseOptions(f.OptionsJson).FirstOrDefault(o => o.Value == s)?.Label ?? s;
                }
            case "BOOLEAN":
                {
                    var b = Convert.ToString(v, inv);
                    return b is "1" or "True" or "true" ? "Yes" : "No";
                }
            case "DATE":
                return v is DateTime dt ? dt.ToString("yyyy-MM-dd") : v is DateOnly d ? d.ToString("yyyy-MM-dd") : Convert.ToString(v, inv);
            case "DECIMAL":
                return v is decimal m ? m.ToString("F2", inv) : Convert.ToString(v, inv);
            case "FILE":
                {
                    var (id, name) = FileParts(Convert.ToString(v, inv));
                    return id == null ? null : string.IsNullOrEmpty(name) ? "File" : name;
                }
            default:
                return Convert.ToString(v, inv);
        }
    }

    // canonical string used to detect changes
    private static string? Norm(object? v) => v switch
    {
        null => null,
        DBNull => null,
        DateTime d => d.ToString("yyyy-MM-dd"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        decimal m => m.ToString("F2", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)
    };

    private static async Task<string?> AuditTextAsync(IDbConnection c, EmployeeField f, string? norm)
    {
        if (norm == null) return null;

        string? text;
        if (f.DataType == "LOOKUP" && f.LookupSource != null && Lookup.TryGetValue(f.LookupSource, out var m) && int.TryParse(norm, out var id))
            text = await c.ExecuteScalarAsync<string?>($"SELECT {m.NameCol} FROM {m.Table} WHERE {m.IdCol}=@id", new { id }) ?? $"#{id}";
        else
            text = DisplayValue(f, norm);

        return text is { Length: > 500 } ? text[..500] : text;
    }

    // value sent to the browser when editing
    private static object? OutValue(EmployeeField f, object? v)
    {
        if (v == null || v is DBNull) return f.DataType == "BOOLEAN" ? false : null;

        switch (f.DataType)
        {
            case "DATE":
                return v is DateTime dt ? dt.ToString("yyyy-MM-dd") : v is DateOnly d ? d.ToString("yyyy-MM-dd") : Convert.ToString(v);
            case "BOOLEAN":
                return Convert.ToInt32(v) == 1;
            case "FILE":
                {
                    var (id, name) = FileParts(Convert.ToString(v));
                    return id == null ? null : new { fileId = id, fileName = string.IsNullOrEmpty(name) ? "File" : name };
                }
            default:
                return v;
        }
    }

    // =========================================================
    // EMPLOYEE VALIDATION
    // =========================================================

    private static async Task<(bool Ok, object? Value, string? Error)> ConvertAsync(IDbConnection c, EmployeeField f, string raw)
    {
        var rl = f.Rules;

        switch (f.DataType)
        {
            case "TEXT":
            case "EMAIL":
            case "PHONE":
                {
                    if (f.DataType == "TEXT") raw = SpacesRx.Replace(raw, " ");   // "John   Smith" -> "John Smith"
                    if (f.DataType == "EMAIL") raw = raw.ToLowerInvariant();

                    var max = f.DataType == "EMAIL" ? 150 : f.DataType == "PHONE" ? 20 : f.MaxLength ?? 100;
                    if (raw.Length > max) return (false, null, $"Maximum {max} characters.");
                    if (rl.MinLength is int minLen && raw.Length < minLen) return (false, null, $"Minimum {minLen} characters.");
                    if (f.DataType == "EMAIL" && !EmailRx.IsMatch(raw)) return (false, null, "Enter a valid email address.");
                    if (f.DataType == "PHONE" && !PhoneRx.IsMatch(raw)) return (false, null, "Enter a valid phone number.");
                    if (!string.IsNullOrEmpty(f.ValidationRegex) && !MatchesFormat(f.ValidationRegex, raw))
                        return (false, null, string.IsNullOrEmpty(f.FormatHint) ? "Invalid format." : $"Invalid format. Expected: {f.FormatHint}");
                    return (true, raw, null);
                }
            case "NUMBER":
                {
                    if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return (false, null, "Enter a whole number.");
                    if (rl.Min is decimal mn && n < mn) return (false, null, $"Must be at least {mn:0.##}.");
                    if (rl.Max is decimal mx && n > mx) return (false, null, $"Must be at most {mx:0.##}.");
                    return (true, n, null);
                }
            case "DECIMAL":
                {
                    if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return (false, null, "Enter a valid number.");
                    if (rl.Min is decimal mn && d < mn) return (false, null, $"Must be at least {mn:0.##}.");
                    if (rl.Max is decimal mx && d > mx) return (false, null, $"Must be at most {mx:0.##}.");
                    return (true, Math.Round(d, 2), null);
                }
            case "DATE":
                {
                    if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) || dt.Year < 1900 || dt.Year > 2100)
                        return (false, null, "Enter a valid date.");

                    var date = dt.Date;
                    var today = DateTime.Today;
                    if (rl.NoFuture == true && date > today) return (false, null, "Date cannot be in the future.");
                    if (rl.NoPast == true && date < today) return (false, null, "Date cannot be in the past.");
                    if (rl.MinAgeYears is int minAge && date > today.AddYears(-minAge)) return (false, null, $"Age must be at least {minAge} years.");
                    if (rl.MaxAgeYears is int maxAge && date < today.AddYears(-maxAge)) return (false, null, $"Age cannot be more than {maxAge} years.");
                    if (rl.MinDate != null && date < DateTime.ParseExact(rl.MinDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)) return (false, null, $"Date must be on or after {rl.MinDate}.");
                    if (rl.MaxDate != null && date > DateTime.ParseExact(rl.MaxDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)) return (false, null, $"Date must be on or before {rl.MaxDate}.");
                    return (true, date, null);
                }
            case "BOOLEAN":
                return raw is "true" or "1" ? (true, 1, null)
                    : raw is "false" or "0" ? (true, 0, null)
                    : (false, null, "Invalid value.");
            case "DROPDOWN":
                return ParseOptions(f.OptionsJson).Any(o => o.Value == raw)
                    ? (true, raw, null) : (false, null, "Choose a value from the list.");
            case "LOOKUP":
                {
                    if (!int.TryParse(raw, out var id) || f.LookupSource == null || !Lookup.TryGetValue(f.LookupSource, out var m)
                        || await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {m.Table} WHERE {m.IdCol}=@id", new { id }) == 0)
                        return (false, null, "Choose a valid option.");
                    return (true, id, null);
                }
            case "FILE":
                {
                    var (fileId, fileName) = FileParts(raw);
                    fileName = fileName?.Trim();
                    if (fileId == null || !FileIdRx.IsMatch(fileId) || string.IsNullOrEmpty(fileName) || fileName.Length > 255)
                        return (false, null, "Invalid file.");

                    var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
                    if (rl.Accept is { Count: > 0 } acc && !acc.Contains(ext))
                        return (false, null, $"Only {string.Join(", ", acc)} files are allowed.");

                    return (true, JsonSerializer.Serialize(new { fileId, fileName }), null);
                }
        }
        return (false, null, "Unsupported field.");
    }

    // names the existing owner of a duplicated unique value
    private static async Task<string?> DuplicateMessageAsync(IDbConnection c, Section s, EmployeeField f, object value, long? existingId)
    {
        var hit = await c.QueryFirstOrDefaultAsync(
            $"SELECT employee_id FROM `{s.TableName}` WHERE `{f.ColumnName}`=@v AND (@id IS NULL OR employee_id<>@id) LIMIT 1",
            new { v = value, id = existingId }) as IDictionary<string, object>;
        if (hit == null) return null;

        var eid = Convert.ToInt64(hit["employee_id"]);
        var who = await c.QueryFirstOrDefaultAsync(
            "SELECT employee_code, first_name, last_name FROM employee_master WHERE employee_id=@eid", new { eid }) as IDictionary<string, object>;

        var shown = value is DateTime dt ? dt.ToString("yyyy-MM-dd") : Convert.ToString(value, CultureInfo.InvariantCulture);
        var owner = $"employee #{eid}";
        if (who != null)
        {
            var name = string.Join(" ", new[] { Convert.ToString(who["first_name"]), Convert.ToString(who["last_name"]) }.Where(x => !string.IsNullOrWhiteSpace(x)));
            owner = $"employee {Convert.ToString(who["employee_code"])} ({name})";
        }
        return $"{f.Label} \"{shown}\" already exists for {owner}.";
    }

    // errors are keyed "<sectionKey>.<column>" so the form can show each one under its field
    private static async Task BuildSectionValuesAsync(
        IDbConnection c, Section s, List<EmployeeField> sf, MasterConfig cfg, Dictionary<string, JsonElement> input,
        long? existingId, Dictionary<string, object?> values, Dictionary<string, string> errors)
    {
        var known = sf.Select(f => f.ColumnName).ToHashSet();
        foreach (var k in input.Keys.Where(k => !known.Contains(k))) errors[$"{s.SectionKey}.{k}"] = "Unknown field.";

        foreach (var f in sf)
        {
            var key = $"{s.SectionKey}.{f.ColumnName}";

            // generated on create (AUTO mode) and immutable after creation
            if (s.IsMain == 1 && f.ColumnName == "employee_code" && (existingId != null || cfg.CodeMode == "AUTO")) continue;

            var raw = input.TryGetValue(f.ColumnName, out var el) ? Raw(el) : null;
            if (raw == null)
            {
                if (f.Req) errors[key] = "Required.";
                values[f.ColumnName] = null;
                continue;
            }

            if (f.Upper) raw = raw.ToUpperInvariant();

            var (ok, value, err) = await ConvertAsync(c, f, raw);
            if (!ok) { errors[key] = err!; continue; }

            if (f.Uniq && value != null)
            {
                var dup = await DuplicateMessageAsync(c, s, f, value, existingId);
                if (dup != null) { errors[key] = dup; continue; }
            }

            values[f.ColumnName] = value;
        }
    }

    // ---- organization ----

    private static async Task<NodeRow?> ValidateOrgAsync(IDbConnection c, OrgInput? org, DataScopeInfo scope, Dictionary<string, string> errors)
    {
        if (org?.NodeId == null) { errors["organization"] = "Select an organization node."; return null; }

        var n = await c.QueryFirstOrDefaultAsync<NodeRow>(
            "SELECT node_id AS NodeId, organization_id AS OrganizationId FROM master_organization_nodes WHERE node_id=@id AND deleted_at IS NULL",
            new { id = org.NodeId });

        if (n == null) { errors["organization"] = "Choose a valid organization node."; return null; }
        if (!scope.Contains(n.NodeId)) { errors["organization"] = "You are not allowed to assign employees to this node."; return null; }
        return n;
    }

    private static DateTime? ParseOrgDate(string? s, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d.Year < 1900 || d.Year > 2100)
        {
            errors["organization.effectiveFrom"] = "Enter a valid date.";
            return null;
        }
        return d.Date;
    }

    private static async Task<string?> NodePathAsync(IDbConnection c, int? nodeId)
    {
        if (nodeId == null) return null;

        var names = new List<string>();
        int? cur = nodeId;
        var guard = 0;
        while (cur != null && guard++ < 30)
        {
            var row = await c.QueryFirstOrDefaultAsync("SELECT node_name, parent_node_id FROM master_organization_nodes WHERE node_id=@id", new { id = cur }) as IDictionary<string, object>;
            if (row == null) break;
            names.Insert(0, Convert.ToString(row["node_name"]) ?? "");
            cur = row["parent_node_id"] is null or DBNull ? null : Convert.ToInt32(row["parent_node_id"]);
        }
        return string.Join(" › ", names);
    }

    private static Task<MappingRow?> CurrentMappingAsync(IDbConnection c, long employeeId, IDbTransaction? tx = null) =>
        c.QueryFirstOrDefaultAsync<MappingRow>(@"
            SELECT mapping_id AS MappingId, organization_id AS OrganizationId, node_id AS NodeId, effective_from AS EffectiveFrom
            FROM master_employee_organization_mapping WHERE employee_id=@employeeId AND is_current=1
            ORDER BY mapping_id DESC LIMIT 1", new { employeeId }, tx);

    private static Task InsertMappingAsync(IDbConnection c, IDbTransaction tx, long employeeId, NodeRow node, DateTime from, long userId) =>
        c.ExecuteAsync(@"INSERT INTO master_employee_organization_mapping (employee_id, organization_id, node_id, effective_from, is_current, assigned_by)
            VALUES (@employeeId, @orgId, @nodeId, @from, 1, @userId)",
            new { employeeId, orgId = node.OrganizationId, nodeId = node.NodeId, from, userId }, tx);

    // ---- login accounts (create only) ----

    // ---- login accounts ----

    private static async Task<List<AccountPlan>> ValidateAccountsAsync(
        IDbConnection c, List<AccountInput>? input, bool canManage, bool isSuper, Dictionary<string, string> errors)
    {
        var result = new List<AccountPlan>();
        if (input == null) return result;

        // rows left completely empty are ignored; indexes stay those of the form so errors land on the right row
        var rows = input.Select((a, i) => (A: a, I: i))
            .Where(x => x.A != null && (x.A.RoleId != null || !string.IsNullOrWhiteSpace(x.A.Username) || !string.IsNullOrEmpty(x.A.Password)))
            .ToList();
        if (rows.Count == 0) return result;

        if (!canManage) { errors["accounts"] = "You do not have permission to create login accounts."; return result; }

        var roles = new Dictionary<int, string>();
        foreach (var r in await c.QueryAsync("SELECT id, role_code FROM roles WHERE active=1"))
        {
            var d = (IDictionary<string, object>)r;
            roles[Convert.ToInt32(d["id"])] = Convert.ToString(d["role_code"]) ?? "";
        }

        var seenRoles = new HashSet<int>();
        var seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (a, i) in rows)
        {
            var k = $"accounts.{i}";
            var before = errors.Count;

            string? code = null;
            if (a.RoleId == null || !roles.TryGetValue(a.RoleId.Value, out code)) errors[$"{k}.roleId"] = "Choose a role.";
            else if (code!.Equals("SUPERADMIN", StringComparison.OrdinalIgnoreCase) && !isSuper) errors[$"{k}.roleId"] = "Only a super admin can grant this role.";
            else if (!seenRoles.Add(a.RoleId.Value)) errors[$"{k}.roleId"] = "Each role can be used only once per employee.";

            // the plain employee role always logs in with the employee code (resolved when the employee is saved)
            var useCode = code != null && code.Equals(EmployeeRoleCode, StringComparison.OrdinalIgnoreCase);

            var u = (a.Username ?? "").Trim();
            if (!useCode)
            {
                if (!UsernameRx.IsMatch(u)) errors[$"{k}.username"] = "3–50 characters: letters, digits and . _ @ - only.";
                else if (!seenUsers.Add(u)) errors[$"{k}.username"] = "This username is used twice in the form.";
                else if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_accounts WHERE username=@u", new { u }) > 0)
                    errors[$"{k}.username"] = $"Username \"{u}\" is already taken.";
            }

            var p = a.Password ?? "";
            if (p.Length < MinPasswordLength) errors[$"{k}.password"] = $"At least {MinPasswordLength} characters.";
            else if (Encoding.UTF8.GetByteCount(p) > 72) errors[$"{k}.password"] = "Too long (maximum 72 bytes).";

            if (errors.Count == before) result.Add(new AccountPlan(i, a.RoleId!.Value, useCode ? null : u, p, useCode));
        }
        return result;
    }

    // Writes the logins inside the caller's transaction. Employee-role logins use the employee code as the username.
    private static async Task InsertAccountsAsync(IDbConnection c, IDbTransaction tx, long employeeId, string employeeCode,
        List<AccountPlan> plans, bool firstIsDefault)
    {
        for (var i = 0; i < plans.Count; i++)
        {
            var a = plans[i];
            var username = a.UseEmployeeCode ? employeeCode : a.Username!;

            if (a.UseEmployeeCode &&
                await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_accounts WHERE username=@username", new { username }, tx) > 0)
                throw new MasterException("Please fix the highlighted fields.", 422,
                    new Dictionary<string, string> { [$"accounts.{a.Index}.username"] = $"The employee code \"{username}\" is already used as a login username." });

            var hash = BCrypt.Net.BCrypt.HashPassword(a.Password, workFactor: 10);
            await c.ExecuteAsync(
                @"INSERT INTO user_accounts (employee_id, role_id, username, password_hash, is_default)
                  VALUES (@employeeId, @roleId, @username, @hash, @isDefault)",
                new { employeeId, roleId = a.RoleId, username, hash, isDefault = firstIsDefault && i == 0 ? 1 : 0 }, tx);
        }
    }

    // =========================================================
    // EMPLOYEE CREATE / UPDATE / GET
    // =========================================================

    private async Task<long> CreateEmployeeAsync(IDbConnection c, EmployeeSaveRequest req, long userId, DataScopeInfo scope)
    {
        var secs = (await GetSectionsAsync(c)).Where(s => s.IsFinalized == 1).ToList();
        if (!secs.Any(s => s.SectionKey == "basic"))
            throw new MasterException("Finalize the Basic Details section before adding employees.", 409);

        var all = await GetFieldsAsync(c);
        var cfg = await GetConfigAsync(c);
        var errors = new Dictionary<string, string>();

        var mainValues = new Dictionary<string, object?>();
        var sectionRows = new List<(Section S, Dictionary<string, object?> Values)>();

        foreach (var s in secs)
        {
            var input = req.Sections != null && req.Sections.TryGetValue(s.SectionKey, out var d) ? d : new Dictionary<string, JsonElement>();
            var sf = all.Where(f => f.SectionId == s.Id).ToList();

            if (s.IsMain == 1)
            {
                await BuildSectionValuesAsync(c, s, sf, cfg, input, null, mainValues, errors);
                continue;
            }

            // optional document sections that were left empty are simply not created
            if (!IsTouched(input) && s.IsMandatory != 1) continue;

            var vals = new Dictionary<string, object?>();
            await BuildSectionValuesAsync(c, s, sf, cfg, input, null, vals, errors);
            sectionRows.Add((s, vals));
        }

        var node = await ValidateOrgAsync(c, req.Organization, scope, errors);
        var effFrom = ParseOrgDate(req.Organization?.EffectiveFrom, errors) ?? DateTime.Today;
        var accounts = await ValidateAccountsAsync(c, req.Accounts, await CanManageAccountsAsync(c), IsSuperAdmin(), errors);
        if (errors.Count > 0) throw new MasterException("Please fix the highlighted fields.", 422, errors);

        using var tx = c.BeginTransaction();

        if (cfg.CodeMode == "AUTO")
        {
            // atomic sequence: LAST_INSERT_ID(expr) returns the pre-increment value on this connection
            await c.ExecuteAsync("UPDATE employee_master_config SET code_next_seq = LAST_INSERT_ID(code_next_seq) + 1 WHERE id=1", transaction: tx);
            var seq = await c.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()", transaction: tx);
            mainValues["employee_code"] = RenderCode(cfg.CodePattern!, seq, DateTime.Now);
        }

        mainValues["created_by"] = userId;
        mainValues["updated_by"] = userId;

        var cols = mainValues.Keys.ToList();
        var p = new DynamicParameters();
        for (var i = 0; i < cols.Count; i++) p.Add($"p{i}", mainValues[cols[i]]);

        await c.ExecuteAsync(
            $"INSERT INTO employee_master ({string.Join(",", cols.Select(k => $"`{k}`"))}) VALUES ({string.Join(",", cols.Select((_, i) => $"@p{i}"))})", p, tx);
        var id = await c.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()", transaction: tx);

        foreach (var (s, vals) in sectionRows)
        {
            var present = vals.Where(kv => kv.Value != null).ToList();
            if (present.Count == 0) continue;

            var sp = new DynamicParameters();
            sp.Add("eid", id);
            sp.Add("by", userId);
            for (var i = 0; i < present.Count; i++) sp.Add($"p{i}", present[i].Value);

            await c.ExecuteAsync(
                $"INSERT INTO `{s.TableName}` (employee_id, created_by, updated_by{string.Concat(present.Select(kv => $", `{kv.Key}`"))}) " +
                $"VALUES (@eid, @by, @by{string.Concat(present.Select((_, i) => $", @p{i}"))})", sp, tx);
        }

        await InsertMappingAsync(c, tx, id, node!, effFrom, userId);

        // login accounts - same transaction, so an employee is never saved without the logins that were entered
        await InsertAccountsAsync(c, tx, id, Convert.ToString(mainValues["employee_code"]) ?? "", accounts, firstIsDefault: true);

        await c.ExecuteAsync("INSERT INTO employee_audit_log (employee_id, action, changed_by) VALUES (@id,'CREATE',@userId)", new { id, userId }, tx);

        tx.Commit();
        return id;
    }

    private async Task UpdateEmployeeAsync(IDbConnection c, long id, EmployeeSaveRequest req, long userId, DataScopeInfo scope)
    {
        await EnsureAccessAsync(c, scope, id);

        if (!int.TryParse(req.Version, out var ver)) throw new MasterException("Reload the employee and try again.");

        var main = await c.QueryFirstOrDefaultAsync("SELECT * FROM employee_master WHERE employee_id=@id", new { id }) as IDictionary<string, object>
            ?? throw new MasterException("Employee not found.", 404);
        if (Convert.ToInt32(main["row_version"]) != ver)
            throw new MasterException("This employee was changed by someone else while you were editing. Reload to see the latest data.", 409);

        var cfg = await GetConfigAsync(c);
        var secs = (await GetSectionsAsync(c)).Where(s => s.IsFinalized == 1).ToList();
        var all = await GetFieldsAsync(c);
        var errors = new Dictionary<string, string>();

        var mainValues = new Dictionary<string, object?>();
        var plans = new List<(Section S, Dictionary<string, object?>? Values, IDictionary<string, object>? Existing)>();

        foreach (var s in secs)
        {
            var input = req.Sections != null && req.Sections.TryGetValue(s.SectionKey, out var d) ? d : new Dictionary<string, JsonElement>();
            var sf = all.Where(f => f.SectionId == s.Id).ToList();

            if (s.IsMain == 1)
            {
                await BuildSectionValuesAsync(c, s, sf, cfg, input, id, mainValues, errors);
                continue;
            }

            var existing = await c.QueryFirstOrDefaultAsync($"SELECT * FROM `{s.TableName}` WHERE employee_id=@id", new { id }) as IDictionary<string, object>;

            if (!IsTouched(input) && s.IsMandatory != 1) { plans.Add((s, null, existing)); continue; } // section emptied

            var vals = new Dictionary<string, object?>();
            await BuildSectionValuesAsync(c, s, sf, cfg, input, id, vals, errors);
            plans.Add((s, vals, existing));
        }

        var node = await ValidateOrgAsync(c, req.Organization, scope, errors);
        var cur = await CurrentMappingAsync(c, id);
        var transfer = node != null && (cur == null || cur.NodeId != node.NodeId);
        DateTime effFrom = DateTime.Today;
        if (transfer)
        {
            effFrom = ParseOrgDate(req.Organization?.EffectiveFrom, errors) ?? DateTime.Today;
            if (cur != null && effFrom <= cur.EffectiveFrom.Date && !errors.ContainsKey("organization.effectiveFrom"))
                errors["organization.effectiveFrom"] = $"Must be after the current assignment date ({cur.EffectiveFrom:yyyy-MM-dd}).";
        }

        if (errors.Count > 0) throw new MasterException("Please fix the highlighted fields.", 422, errors);

        var audit = new List<AuditEntry>();
        var deleteFiles = new List<string>();
        string SecKey(EmployeeField f) => secs.First(s => s.Id == f.SectionId).SectionKey;

        void CollectOldFile(EmployeeField f, object? oldV, object? newV)
        {
            if (f.DataType != "FILE") return;
            var oldId = FileParts(Convert.ToString(oldV)).Id;
            var newId = FileParts(Convert.ToString(newV)).Id;
            if (oldId != null && oldId != newId) deleteFiles.Add(oldId);
        }

        using var tx = c.BeginTransaction();

        // version bump first - doubles as the concurrency guard
        var bumped = await c.ExecuteAsync(
            "UPDATE employee_master SET row_version=row_version+1, updated_by=@userId, updated_at=NOW() WHERE employee_id=@id AND row_version=@ver",
            new { id, userId, ver }, tx);
        if (bumped == 0) throw new MasterException("This employee was changed by someone else while you were editing. Reload to see the latest data.", 409);

        var anyChange = false;

        // ---- main table ----
        var mainChanges = new List<(EmployeeField F, object? V)>();
        foreach (var f in all.Where(f => secs.Any(s => s.Id == f.SectionId && s.IsMain == 1)))
        {
            if (!mainValues.TryGetValue(f.ColumnName, out var nv)) continue;
            main.TryGetValue(f.ColumnName, out var ov);
            if (Norm(ov) == Norm(nv)) continue;

            mainChanges.Add((f, nv));
            audit.Add(new AuditEntry(SecKey(f), f.ColumnName, Norm(ov), Norm(nv), f));
            CollectOldFile(f, ov, nv);
        }
        if (mainChanges.Count > 0)
        {
            var p = new DynamicParameters();
            p.Add("id", id);
            for (var i = 0; i < mainChanges.Count; i++) p.Add($"p{i}", mainChanges[i].V);
            await c.ExecuteAsync($"UPDATE employee_master SET {string.Join(",", mainChanges.Select((x, i) => $"`{x.F.ColumnName}`=@p{i}"))} WHERE employee_id=@id", p, tx);
            anyChange = true;
        }

        // ---- document / extra sections ----
        foreach (var (s, vals, existing) in plans)
        {
            var sf = all.Where(f => f.SectionId == s.Id).ToList();

            if (vals == null) // optional section cleared -> remove its row
            {
                if (existing == null) continue;
                foreach (var f in sf)
                {
                    existing.TryGetValue(f.ColumnName, out var ov);
                    if (ov == null) continue;
                    audit.Add(new AuditEntry(s.SectionKey, f.ColumnName, Norm(ov), null, f));
                    CollectOldFile(f, ov, null);
                }
                await c.ExecuteAsync($"DELETE FROM `{s.TableName}` WHERE employee_id=@id", new { id }, tx);
                anyChange = true;
                continue;
            }

            if (existing == null)
            {
                var present = vals.Where(kv => kv.Value != null).ToList();
                if (present.Count == 0) continue;

                var sp = new DynamicParameters();
                sp.Add("eid", id);
                sp.Add("by", userId);
                for (var i = 0; i < present.Count; i++) sp.Add($"p{i}", present[i].Value);
                await c.ExecuteAsync(
                    $"INSERT INTO `{s.TableName}` (employee_id, created_by, updated_by{string.Concat(present.Select(kv => $", `{kv.Key}`"))}) " +
                    $"VALUES (@eid, @by, @by{string.Concat(present.Select((_, i) => $", @p{i}"))})", sp, tx);

                foreach (var kv in present)
                {
                    var f = sf.First(x => x.ColumnName == kv.Key);
                    audit.Add(new AuditEntry(s.SectionKey, f.ColumnName, null, Norm(kv.Value), f));
                }
                anyChange = true;
                continue;
            }

            var changes = new List<(EmployeeField F, object? V)>();
            foreach (var f in sf)
            {
                if (!vals.TryGetValue(f.ColumnName, out var nv)) continue;
                existing.TryGetValue(f.ColumnName, out var ov);
                if (Norm(ov) == Norm(nv)) continue;

                changes.Add((f, nv));
                audit.Add(new AuditEntry(s.SectionKey, f.ColumnName, Norm(ov), Norm(nv), f));
                CollectOldFile(f, ov, nv);
            }
            if (changes.Count == 0) continue;

            var up = new DynamicParameters();
            up.Add("id", id);
            up.Add("by", userId);
            for (var i = 0; i < changes.Count; i++) up.Add($"p{i}", changes[i].V);
            await c.ExecuteAsync($"UPDATE `{s.TableName}` SET {string.Join(",", changes.Select((x, i) => $"`{x.F.ColumnName}`=@p{i}"))}, updated_by=@by WHERE employee_id=@id", up, tx);
            anyChange = true;
        }

        // ---- organization transfer (history is kept) ----
        if (transfer)
        {
            var oldPath = cur == null ? null : await NodePathAsync(c, cur.NodeId);
            var newPath = await NodePathAsync(c, node!.NodeId);

            if (cur != null)
                await c.ExecuteAsync("UPDATE master_employee_organization_mapping SET is_current=0, effective_to=@to WHERE mapping_id=@mid",
                    new { to = effFrom.AddDays(-1), mid = cur.MappingId }, tx);

            await InsertMappingAsync(c, tx, id, node, effFrom, userId);
            audit.Add(new AuditEntry("organization", "__org", oldPath, newPath, null));
            anyChange = true;
        }

        if (!anyChange) return; // disposing the transaction rolls the version bump back

        var rows = new List<object>();
        foreach (var a in audit)
        {
            rows.Add(new
            {
                id,
                sec = a.SectionKey,
                col = a.Column,
                oldV = a.F != null ? await AuditTextAsync(c, a.F, a.Old) : a.Old,
                newV = a.F != null ? await AuditTextAsync(c, a.F, a.New) : a.New,
                by = userId
            });
        }
        await c.ExecuteAsync(
            "INSERT INTO employee_audit_log (employee_id, action, section_key, column_name, old_value, new_value, changed_by) VALUES (@id,'UPDATE',@sec,@col,@oldV,@newV,@by)",
            rows, tx);

        tx.Commit();

        // replaced / removed documents are no longer referenced - delete them from the file server (best effort)
        foreach (var fid in deleteFiles.Distinct())
        {
            try { await _files.DeleteAsync("secure", fid); }
            catch (Exception ex) { Console.Error.WriteLine($"Could not delete file {fid}: {ex.Message}"); }
        }
    }

    private static async Task<object?> GetEmployeeAsync(IDbConnection c, long id)
    {
        var main = await c.QueryFirstOrDefaultAsync("SELECT * FROM employee_master WHERE employee_id=@id", new { id }) as IDictionary<string, object>;
        if (main == null) return null;

        var secs = (await GetSectionsAsync(c)).Where(s => s.IsFinalized == 1).ToList();
        var all = await GetFieldsAsync(c);

        var sections = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var s in secs)
        {
            var row = s.IsMain == 1
                ? main
                : await c.QueryFirstOrDefaultAsync($"SELECT * FROM `{s.TableName}` WHERE employee_id=@id", new { id }) as IDictionary<string, object>;

            var vals = new Dictionary<string, object?>();
            foreach (var f in all.Where(f => f.SectionId == s.Id))
            {
                object? v = null;
                row?.TryGetValue(f.ColumnName, out v);
                vals[f.ColumnName] = OutValue(f, v);
            }
            sections[s.SectionKey] = vals;
        }

        var map = await CurrentMappingAsync(c, id);

        return new
        {
            employeeId = id,
            version = Convert.ToInt32(main["row_version"]).ToString(),
            organization = map == null ? null : new { nodeId = map.NodeId, organizationId = map.OrganizationId, effectiveFrom = map.EffectiveFrom.ToString("yyyy-MM-dd") },
            sections
        };
    }

    // =========================================================
    // LIST / EXPORT
    // =========================================================

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string SelectExpr(EmployeeField f, string alias) =>
        f.DataType == "LOOKUP" && f.LookupSource != null && Lookup.TryGetValue(f.LookupSource, out var m)
            ? $"(SELECT l.{m.NameCol} FROM {m.Table} l WHERE l.{m.IdCol} = {alias}.`{f.ColumnName}`) AS f{f.Id}"
            : $"{alias}.`{f.ColumnName}` AS f{f.Id}";

    private static async Task<(string FromSql, DynamicParameters P)> BuildFromAsync(
        IDbConnection c, List<Section> secs, List<EmployeeField> fields, ListQuery q, IEnumerable<int> joinSectionIds, DataScopeInfo scope)
    {
        var secById = secs.ToDictionary(s => s.Id);
        string AliasOf(EmployeeField f) => secById[f.SectionId].IsMain == 1 ? "e" : $"s{f.SectionId}";

        var searchFields = fields.Where(f => f.Searchable && TextTypes.Contains(f.DataType) && !FixedColumns.Contains(f.ColumnName)).ToList();

        var from = new StringBuilder(
            "FROM employee_master e " +
            "LEFT JOIN master_employee_organization_mapping m ON m.employee_id = e.employee_id AND m.is_current = 1 " +
            "LEFT JOIN master_organization_nodes n ON n.node_id = m.node_id ");

        var joined = new HashSet<int>();
        var needed = string.IsNullOrWhiteSpace(q.Search) ? joinSectionIds : joinSectionIds.Concat(searchFields.Select(f => f.SectionId));
        foreach (var sid in needed.Distinct())
            if (secById[sid].IsMain != 1 && joined.Add(sid))
                from.Append($"LEFT JOIN `{secById[sid].TableName}` s{sid} ON s{sid}.employee_id = e.employee_id ");

        // data-flow visibility always applies first
        var clauses = new List<string> { DataScope.Clause(scope, "m.node_id") };
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            p.Add("like", $"%{EscapeLike(q.Search.Trim())}%");
            var parts = new List<string>
            {
                "e.employee_code LIKE @like", "e.first_name LIKE @like", "e.last_name LIKE @like",
                "CONCAT(e.first_name,' ',COALESCE(e.last_name,'')) LIKE @like"
            };
            foreach (var f in searchFields) parts.Add($"{AliasOf(f)}.`{f.ColumnName}` LIKE @like");
            clauses.Add("(" + string.Join(" OR ", parts) + ")");
        }

        if (q.NodeId.HasValue)
        {
            var ids = await DataScope.SubtreeAsync(c, q.NodeId.Value, scope);
            clauses.Add($"m.node_id IN ({(ids.Count == 0 ? "-1" : string.Join(",", ids))})"); // ints only
        }

        if (q.DepartmentId.HasValue) { clauses.Add("e.department_id=@dept"); p.Add("dept", q.DepartmentId.Value); }
        if (q.DesignationId.HasValue) { clauses.Add("e.designation_id=@desig"); p.Add("desig", q.DesignationId.Value); }
        if (q.OrgId.HasValue) { clauses.Add("m.organization_id=@org"); p.Add("org", q.OrgId.Value); }
        if (q.Status is "ACTIVE" or "INACTIVE") { clauses.Add("e.status=@status"); p.Add("status", q.Status); }

        from.Append("WHERE ").Append(string.Join(" AND ", clauses));
        return (from.ToString(), p);
    }

    private static async Task<object> ListEmployeesAsync(IDbConnection c, ListQuery q, DataScopeInfo scope)
    {
        var secs = (await GetSectionsAsync(c)).Where(s => s.IsFinalized == 1).ToList();
        var secById = secs.ToDictionary(s => s.Id);
        var fields = (await GetFieldsAsync(c)).Where(f => secById.ContainsKey(f.SectionId)).ToList();
        string AliasOf(EmployeeField f) => secById[f.SectionId].IsMain == 1 ? "e" : $"s{f.SectionId}";

        var listFields = fields.Where(f => f.InList && !FixedColumns.Contains(f.ColumnName)).ToList();

        var (fromSql, p) = await BuildFromAsync(c, secs, fields, q, listFields.Select(f => f.SectionId), scope);
        var total = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) {fromSql}", p);

        var pageSize = Math.Clamp(q.PageSize, 1, 100);
        var page = Math.Max(q.Page, 1);
        p.Add("take", pageSize);
        p.Add("skip", (page - 1) * pageSize);

        // sortable whitelist (never user text in ORDER BY)
        var sortable = new Dictionary<string, string[]>
        {
            ["employee_code"] = new[] { "e.employee_code" },
            ["name"] = new[] { "e.first_name", "e.last_name" },
            ["status"] = new[] { "e.status" },
            ["created_at"] = new[] { "e.created_at" },
            ["org"] = new[] { "n.display_order", "n.node_name", "e.employee_code" }
        };
        foreach (var f in listFields.Where(f => f.DataType is not ("LOOKUP" or "FILE")))
            sortable[$"f{f.Id}"] = new[] { $"{AliasOf(f)}.`{f.ColumnName}`" };

        var sortKey = q.Sort != null && sortable.ContainsKey(q.Sort) ? q.Sort : "employee_code";
        var dir = string.Equals(q.Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        var orderBy = string.Join(", ", sortable[sortKey].Select(x => $"{x} {dir}")) + ", e.employee_id ASC";

        var select = "e.employee_id AS employee_id, e.employee_code AS employee_code, e.first_name AS first_name, " +
                     "e.last_name AS last_name, e.status AS status, m.node_id AS org_node_id" +
                     (listFields.Count > 0 ? ", " + string.Join(", ", listFields.Select(f => SelectExpr(f, AliasOf(f)))) : "");

        var rows = await c.QueryAsync($"SELECT {select} {fromSql} ORDER BY {orderBy} LIMIT @take OFFSET @skip", p);

        var items = new List<object>();
        foreach (var r in rows)
        {
            var row = (IDictionary<string, object>)r;
            var vals = new Dictionary<string, string?>();
            foreach (var f in listFields)
            {
                row.TryGetValue($"f{f.Id}", out var v);
                vals[f.Id.ToString()] = DisplayValue(f, v);
            }

            items.Add(new
            {
                employeeId = Convert.ToInt64(row["employee_id"]),
                employeeCode = Convert.ToString(row["employee_code"]),
                firstName = Convert.ToString(row["first_name"]),
                lastName = row["last_name"] is DBNull or null ? null : Convert.ToString(row["last_name"]),
                status = Convert.ToString(row["status"]),
                orgNodeId = row["org_node_id"] is DBNull or null ? (int?)null : Convert.ToInt32(row["org_node_id"]),
                values = vals
            });
        }

        return new { total, page, pageSize, items };
    }

    private static string Csv(string? s)
    {
        if (s == null) return "";
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s; // spreadsheet formula injection guard
        return s.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    private static async Task<byte[]> BuildCsvAsync(IDbConnection c, ListQuery q, DataScopeInfo scope)
    {
        var secs = (await GetSectionsAsync(c)).Where(s => s.IsFinalized == 1).ToList();
        var secById = secs.ToDictionary(s => s.Id);
        var fields = (await GetFieldsAsync(c)).Where(f => secById.ContainsKey(f.SectionId)).ToList();
        string AliasOf(EmployeeField f) => secById[f.SectionId].IsMain == 1 ? "e" : $"s{f.SectionId}";

        var (fromSql, p) = await BuildFromAsync(c, secs, fields, q, fields.Select(f => f.SectionId), scope);
        var select = string.Join(", ", fields.Select(f => SelectExpr(f, AliasOf(f)))) + ", n.node_name AS org_name";
        var rows = await c.QueryAsync($"SELECT {select} {fromSql} ORDER BY e.employee_id ASC LIMIT {ExportLimit}", p);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", fields.Select(f => Csv(f.Label)).Append("Organization")));
        foreach (var r in rows)
        {
            var row = (IDictionary<string, object>)r;
            var cells = fields.Select(f =>
            {
                row.TryGetValue($"f{f.Id}", out var v);
                return Csv(DisplayValue(f, v));
            }).ToList();
            cells.Add(Csv(row.TryGetValue("org_name", out var o) ? Convert.ToString(o) : null));
            sb.AppendLine(string.Join(",", cells));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(); // BOM so Excel reads UTF-8
    }
}