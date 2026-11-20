using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HrmsBackend.Services;

public class TrException : Exception
{
    public int Status { get; }
    public object? Checks { get; }
    public TrException(string message, int status = 400, object? checks = null) : base(message) { Status = status; Checks = checks; }
}

// ---------------------------------------------------------------- rows
public class PolicyRow { public int Id { get; set; } public string Name { get; set; } = ""; public int Version { get; set; } public string Status { get; set; } = "DRAFT"; public DateTime? EffectiveFrom { get; set; } public string? Notes { get; set; } }
public class TypeRow { public string TypeKey { get; set; } = ""; public string Name { get; set; } = ""; public string ProcMode { get; set; } = "INDIVIDUAL"; public string InitiatedBy { get; set; } = "MANAGEMENT"; public int NeedsConsent { get; set; } public int NeedsCategory { get; set; } public string VacancyCheck { get; set; } = "NONE"; public string DeptChange { get; set; } = "NONE"; public string DesigChange { get; set; } = "NONE"; public string SeniorityEffect { get; set; } = "RETAIN"; public string ApplyOn { get; set; } = "JOINING"; public int IsEnabled { get; set; } }
public class CatRow { public string CategoryKey { get; set; } = ""; public string Name { get; set; } = ""; public int PriorityRank { get; set; } public string? DocLabel { get; set; } public int WaivesVacancy { get; set; } public int IsEnabled { get; set; } }
public class RuleRow { public string RuleKey { get; set; } = ""; public string Name { get; set; } = ""; public string RuleKind { get; set; } = ""; public string? ParamsJson { get; set; } public string AppliesTypes { get; set; } = "*"; public string? ExemptCategories { get; set; } public string Strength { get; set; } = "WARN"; public string? RelaxRoleCode { get; set; } public int IsEnabled { get; set; } }
public class StepRow { public long Id { get; set; } public long RequestId { get; set; } public string TypeKey { get; set; } = ""; public int StepOrder { get; set; } public string Name { get; set; } = ""; public string Action { get; set; } = ""; public string Authority { get; set; } = "ROLE"; public string? RoleCodes { get; set; } public string? EmployeeIds { get; set; } public string Anchor { get; set; } = "EITHER"; public string? Decision { get; set; } public long? ActedBy { get; set; } public DateTime? ActedAt { get; set; } public string? Remarks { get; set; } }
public class CheckResult { public int Subject { get; set; } = 1; public string RuleKey { get; set; } = ""; public string Name { get; set; } = ""; public string Strength { get; set; } = "WARN"; public string State { get; set; } = "PASS"; public string? Message { get; set; } }
public class Mapping { public int MappingId { get; set; } public int OrganizationId { get; set; } public int NodeId { get; set; } public DateTime EffectiveFrom { get; set; } }
public class WindowRow
{
    public int Id { get; set; } public int PolicyId { get; set; } public string TypeKey { get; set; } = ""; public string Name { get; set; } = ""; public int? ScopeNodeId { get; set; }
    public DateTime ApplyFrom { get; set; } public DateTime ApplyTo { get; set; } public DateTime? ObjectionFrom { get; set; } public DateTime? ObjectionTo { get; set; } public DateTime? OrdersFrom { get; set; }
    public DateTime? EffectiveOn { get; set; } public int? TotalSeats { get; set; } public string? GeneralCategory { get; set; } public int OverflowToGeneral { get; set; } public string Status { get; set; } = "DRAFT";
}
public class Req
{
    public long Id { get; set; } public string? RequestNo { get; set; } public int PolicyId { get; set; } public string TypeKey { get; set; } = ""; public string? CategoryKey { get; set; }
    public int EmployeeId { get; set; } public int? Employee2Id { get; set; }
    public int FromNodeId { get; set; } public int FromOrgId { get; set; } public int ToNodeId { get; set; } public int ToOrgId { get; set; }
    public int? FromDepartmentId { get; set; } public int? FromDesignationId { get; set; } public int? ToDepartmentId { get; set; } public int? ToDesignationId { get; set; }
    public DateTime? RequestedDate { get; set; } public string Reason { get; set; } = ""; public string Status { get; set; } = ""; public int CurrentStep { get; set; }
    public string ConsentStatus { get; set; } = "NA"; public int NeedsRelaxation { get; set; } public string? RelaxationReason { get; set; }
    public int? WindowId { get; set; } public int? RankNo { get; set; } public string? SelStatus { get; set; } public string? SelectionNote { get; set; }
    public string? OrderNo { get; set; } public DateTime? OrderDate { get; set; } public string? OrderFileId { get; set; } public string? OrderFileName { get; set; }
    public DateTime? RelieveDate { get; set; } public DateTime? JoinDate { get; set; } public DateTime? ApplyAt { get; set; } public int PostingApplied { get; set; } public string? PostingError { get; set; }
    public long CreatedBy { get; set; } public DateTime CreatedAt { get; set; }
}

public class Actor
{
    public long Id { get; init; }
    public string? Role { get; init; }
    public bool Super { get; init; }
    public DataScopeInfo Scope { get; init; } = new();
}

public record DocInput(string Label, string FileId, string FileName);

public class SubmitBody
{
    public long EmployeeId { get; set; }
    public long? PartnerId { get; set; }          // mutual transfer: the employee to exchange with
    public string TypeKey { get; set; } = "";
    public string? CategoryKey { get; set; }
    public int ToNodeId { get; set; }
    public int? ToDepartmentId { get; set; }
    public int? ToDesignationId { get; set; }
    public int? WindowId { get; set; }
    public string? RequestedDate { get; set; }
    public string? Reason { get; set; }
    public List<DocInput>? Documents { get; set; }
}

public class Prep
{
    public PolicyRow Policy { get; set; } = new();
    public TypeRow Type { get; set; } = new();
    public CatRow? Category { get; set; }
    public Mapping From { get; set; } = new();
    public int ToNodeId { get; set; }
    public int ToOrgId { get; set; }
    public int? FromDepartmentId { get; set; }
    public int? FromDesignationId { get; set; }
    public int? ToDepartmentId { get; set; }
    public int? ToDesignationId { get; set; }
    public long? PartnerId { get; set; }
    public WindowRow? Window { get; set; }
    public List<CheckResult> Checks { get; set; } = new();
    public List<StepRow> Steps { get; set; } = new();
    public bool Blocked => Checks.Any(x => x.Strength == "BLOCK" && x.State == "FAIL");
    public bool NeedsRelaxation => Checks.Any(x => x.Strength == "RELAX" && x.State == "FAIL");
}

// ---------------------------------------------------------------- engine
public static class TransferCore
{
    public static readonly Regex FileIdRx = new("^[0-9a-f]{32}$", RegexOptions.Compiled);
    public static readonly Regex IdentRx = new("^[A-Za-z0-9_]{1,64}$", RegexOptions.Compiled);
    public const string OpenSql = "'PENDING','RETURNED','VERIFIED','APPROVED','ORDERED','RELIEVED'";

    public const string PolicyCols = "id AS Id, name AS Name, version AS Version, status AS Status, effective_from AS EffectiveFrom, notes AS Notes";
    public const string TypeCols = "type_key AS TypeKey, name AS Name, proc_mode AS ProcMode, initiated_by AS InitiatedBy, needs_consent AS NeedsConsent, needs_category AS NeedsCategory, vacancy_check AS VacancyCheck, dept_change AS DeptChange, desig_change AS DesigChange, seniority_effect AS SeniorityEffect, apply_on AS ApplyOn, is_enabled AS IsEnabled";
    public const string CatCols = "category_key AS CategoryKey, name AS Name, priority_rank AS PriorityRank, doc_label AS DocLabel, waives_vacancy AS WaivesVacancy, is_enabled AS IsEnabled";
    public const string RuleCols = "rule_key AS RuleKey, name AS Name, rule_kind AS RuleKind, params_json AS ParamsJson, applies_types AS AppliesTypes, exempt_categories AS ExemptCategories, strength AS Strength, relax_role_code AS RelaxRoleCode, is_enabled AS IsEnabled";
    public const string PStepCols = "id AS Id, type_key AS TypeKey, step_order AS StepOrder, name AS Name, action AS Action, authority AS Authority, role_codes AS RoleCodes, employee_ids AS EmployeeIds, anchor AS Anchor";
    public const string RStepCols = "id AS Id, request_id AS RequestId, step_order AS StepOrder, name AS Name, action AS Action, authority AS Authority, role_codes AS RoleCodes, employee_ids AS EmployeeIds, anchor AS Anchor, decision AS Decision, acted_by AS ActedBy, acted_at AS ActedAt, remarks AS Remarks";
    public const string WindowCols = "id AS Id, policy_id AS PolicyId, type_key AS TypeKey, name AS Name, scope_node_id AS ScopeNodeId, apply_from AS ApplyFrom, apply_to AS ApplyTo, objection_from AS ObjectionFrom, objection_to AS ObjectionTo, orders_from AS OrdersFrom, effective_on AS EffectiveOn, total_seats AS TotalSeats, general_category AS GeneralCategory, overflow_to_general AS OverflowToGeneral, status AS Status";
    public const string ReqCols = @"id AS Id, request_no AS RequestNo, policy_id AS PolicyId, type_key AS TypeKey, category_key AS CategoryKey, employee_id AS EmployeeId, employee2_id AS Employee2Id,
        from_node_id AS FromNodeId, from_org_id AS FromOrgId, to_node_id AS ToNodeId, to_org_id AS ToOrgId, from_department_id AS FromDepartmentId, from_designation_id AS FromDesignationId,
        to_department_id AS ToDepartmentId, to_designation_id AS ToDesignationId, requested_date AS RequestedDate, reason AS Reason, status AS Status, current_step AS CurrentStep,
        consent_status AS ConsentStatus, needs_relaxation AS NeedsRelaxation, relaxation_reason AS RelaxationReason, window_id AS WindowId, rank_no AS RankNo, sel_status AS SelStatus,
        selection_note AS SelectionNote, order_no AS OrderNo, order_date AS OrderDate, order_file_id AS OrderFileId, order_file_name AS OrderFileName, relieve_date AS RelieveDate,
        join_date AS JoinDate, apply_at AS ApplyAt, posting_applied AS PostingApplied, posting_error AS PostingError, created_by AS CreatedBy, created_at AS CreatedAt";

    // ---- small helpers ----
    public static string[] Csv(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static bool InCsv(string? csv, string value) => Csv(csv).Contains(value, StringComparer.OrdinalIgnoreCase);
    public static string? Dt(DateTime? d) => d?.ToString("yyyy-MM-dd'T'HH:mm");

    public static JsonElement ParseParams(string? json)
    {
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); return d.RootElement.Clone(); }
        catch { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }
    }

    public static double Num(JsonElement p, string name, double def) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;

    public static bool TryDate(string? s, out DateTime d) =>
        DateTime.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d.Year >= 1900 && d.Year <= 2100;

    public static DateTime? OptDate(string? s, string what)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!TryDate(s, out var d)) throw new TrException($"{what} is not a valid date.");
        return d.Date;
    }

    public static DateTime ReqDate(string? s, string what) => OptDate(s, what) ?? throw new TrException($"{what} is required.");

    public static DateTime? OptDateTime(string? s, string what)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d.Year < 1900 || d.Year > 2100) throw new TrException($"{what} is not a valid date and time.");
        return d;
    }

    // ---- lookups ----
    public static Task<Mapping?> CurrentMappingAsync(IDbConnection c, long employeeId, IDbTransaction? tx = null) =>
        c.QueryFirstOrDefaultAsync<Mapping>(@"SELECT mapping_id AS MappingId, organization_id AS OrganizationId, node_id AS NodeId, effective_from AS EffectiveFrom
            FROM master_employee_organization_mapping WHERE employee_id=@employeeId AND is_current=1 ORDER BY mapping_id DESC LIMIT 1", new { employeeId }, tx);

    public static async Task<string> NodePathAsync(IDbConnection c, int nodeId, IDbTransaction? tx = null)
    {
        var names = new List<string>();
        int? cur = nodeId;
        var guard = 0;
        while (cur != null && guard++ < 30)
        {
            var row = await c.QueryFirstOrDefaultAsync("SELECT node_name, parent_node_id FROM master_organization_nodes WHERE node_id=@id", new { id = cur }, tx) as IDictionary<string, object>;
            if (row == null) break;
            names.Insert(0, Convert.ToString(row["node_name"]) ?? "");
            cur = row["parent_node_id"] is null or DBNull ? null : Convert.ToInt32(row["parent_node_id"]);
        }
        return string.Join(" › ", names);
    }

    public static async Task<bool> IsUnderAsync(IDbConnection c, int nodeId, int ancestorId)
    {
        int? cur = nodeId;
        var guard = 0;
        while (cur != null && guard++ < 50)
        {
            if (cur == ancestorId) return true;
            cur = await c.ExecuteScalarAsync<int?>("SELECT parent_node_id FROM master_organization_nodes WHERE node_id=@id", new { id = cur });
        }
        return false;
    }

    public static async Task<object?> PersonAsync(IDbConnection c, long id)
    {
        var row = await c.QueryFirstOrDefaultAsync("SELECT employee_code, first_name, last_name FROM employee_master WHERE employee_id=@id", new { id }) as IDictionary<string, object>;
        if (row == null) return null;
        var name = string.Join(" ", new[] { Convert.ToString(row["first_name"]), row["last_name"] is DBNull or null ? null : Convert.ToString(row["last_name"]) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new { id, code = Convert.ToString(row["employee_code"]), name };
    }

    public static Task LogAsync(IDbConnection c, IDbTransaction? tx, long requestId, string action, long by, string? remarks) =>
        c.ExecuteAsync("INSERT INTO tr_request_log (request_id, action, by_user, remarks) VALUES (@requestId,@action,@by,@remarks)", new { requestId, action, by, remarks }, tx);

    public static async Task<Req> LoadReqAsync(IDbConnection c, long id, IDbTransaction? tx = null) =>
        await c.QueryFirstOrDefaultAsync<Req>($"SELECT {ReqCols} FROM tr_request WHERE id=@id", new { id }, tx) ?? throw new TrException("Transfer request not found.", 404);

    public static async Task<List<StepRow>> LoadStepsAsync(IDbConnection c, long id, IDbTransaction? tx = null) =>
        (await c.QueryAsync<StepRow>($"SELECT {RStepCols} FROM tr_request_step WHERE request_id=@id ORDER BY step_order", new { id }, tx)).ToList();

    // ---- who may do what (all anchored in the data-flow scope) ----
    public static bool HasRole(Actor a, string? csv) => a.Role != null && InCsv(csv, a.Role);
    public static bool IsNamed(Actor a, StepRow s) => s.Authority == "EMPLOYEE" && Csv(s.EmployeeIds).Contains(a.Id.ToString());

    public static bool StepAuthorizes(Actor a, StepRow s, int from, int to)
    {
        if (a.Super) return true;
        if (s.Authority == "EMPLOYEE") return IsNamed(a, s);
        if (!HasRole(a, s.RoleCodes)) return false;
        return s.Anchor switch { "FROM" => a.Scope.Contains(from), "TO" => a.Scope.Contains(to), _ => a.Scope.Contains(from) || a.Scope.Contains(to) };
    }

    public static bool CanDecide(Actor a, Req r, StepRow step, List<StepRow> steps)
    {
        if (r.Status != "PENDING" || r.ConsentStatus == "PENDING" || step.Decision != null) return false;
        if (!StepAuthorizes(a, step, r.FromNodeId, r.ToNodeId)) return false;
        if (a.Super) return true;
        // maker-checker: not your own transfer, and nobody acts on two steps of one request
        return r.EmployeeId != a.Id && r.Employee2Id != a.Id && r.CreatedBy != a.Id && !steps.Any(s => s.ActedBy == a.Id);
    }

    public static bool CanPost(Actor a, Req r, List<StepRow> steps, string action)
    {
        if (a.Super) return true;
        if (steps.Count == 0) return false;
        if (action == "order") return StepAuthorizes(a, steps.OrderBy(s => s.StepOrder).Last(), r.FromNodeId, r.ToNodeId);
        var node = action == "relieve" ? r.FromNodeId : r.ToNodeId;
        return steps.Any(s => s.Authority == "EMPLOYEE" ? IsNamed(a, s) : HasRole(a, s.RoleCodes) && a.Scope.Contains(node));
    }

    // strictly data-flow based: the requester, the employees involved, named authorities, and users whose scope covers the from or to node
    public static bool CanView(Actor a, Req r, List<StepRow> steps) =>
        a.Super || r.CreatedBy == a.Id || r.EmployeeId == a.Id || r.Employee2Id == a.Id
        || a.Scope.Contains(r.FromNodeId) || a.Scope.Contains(r.ToNodeId) || steps.Any(s => IsNamed(a, s));

    public static string VisibleClause(Actor a, string r = "r")
    {
        if (a.Super) return "1=1";
        return $"({r}.created_by={a.Id} OR {r}.employee_id={a.Id} OR {r}.employee2_id={a.Id} OR {DataScope.Clause(a.Scope, r + ".from_node_id")} OR {DataScope.Clause(a.Scope, r + ".to_node_id")}"
             + $" OR EXISTS(SELECT 1 FROM tr_request_step vs WHERE vs.request_id={r}.id AND vs.authority='EMPLOYEE' AND FIND_IN_SET({a.Id}, vs.employee_ids) > 0))";
    }

    // requests waiting for this user: a decision, an order, a relieving or a joining
    public static async Task<List<long>> InboxIdsAsync(IDbConnection c, Actor me)
    {
        var reqs = (await c.QueryAsync<Req>($"SELECT {ReqCols} FROM tr_request WHERE status IN ('PENDING','APPROVED','ORDERED','RELIEVED') AND {VisibleClause(me)}")).ToList();
        if (reqs.Count == 0) return new List<long>();

        var ids = reqs.Select(r => r.Id).ToList();
        var steps = (await c.QueryAsync<StepRow>($"SELECT {RStepCols} FROM tr_request_step WHERE request_id IN @ids", new { ids })).GroupBy(s => s.RequestId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<long>();
        foreach (var r in reqs)
        {
            var st = steps.TryGetValue(r.Id, out var list) ? list : new List<StepRow>();
            var ok = r.Status switch
            {
                "PENDING" => st.FirstOrDefault(s => s.StepOrder == r.CurrentStep) is { } cur && CanDecide(me, r, cur, st),
                "APPROVED" => (r.WindowId == null || true) && CanPost(me, r, st, "order"),
                "ORDERED" => CanPost(me, r, st, "relieve"),
                "RELIEVED" => CanPost(me, r, st, "join"),
                _ => false
            };
            if (ok) result.Add(r.Id);
        }
        return result;
    }

    // ---- notifications ----
    private class FlowRow { public long Id { get; set; } public long? ParentId { get; set; } public long NodeId { get; set; } }

    // a node and every node above it in the data-flow tree = the places whose officers can see it
    public static async Task<HashSet<int>> FlowAncestorsAsync(IDbConnection c, int nodeId, IDbTransaction? tx = null)
    {
        var rows = (await c.QueryAsync<FlowRow>("SELECT id AS Id, parent_data_flow_id AS ParentId, organization_node_id AS NodeId FROM master_organization_data_flow WHERE is_active=1", transaction: tx)).ToList();
        var byId = rows.ToDictionary(x => x.Id);
        var result = new HashSet<int> { nodeId };
        var guard = 0;
        foreach (var start in rows.Where(x => x.NodeId == nodeId))
        {
            var cur = start;
            while (cur.ParentId != null && byId.TryGetValue(cur.ParentId.Value, out var p) && guard++ < 2000) { result.Add((int)p.NodeId); cur = p; }
        }
        return result;
    }

    public static async Task<List<long>> ApproversAsync(IDbConnection c, StepRow s, int from, int to, IDbTransaction? tx = null)
    {
        if (s.Authority == "EMPLOYEE") return Csv(s.EmployeeIds).Select(x => long.TryParse(x, out var v) ? v : 0).Where(x => x > 0).ToList();

        var nodes = new HashSet<int>();
        if (s.Anchor != "TO") nodes.UnionWith(await FlowAncestorsAsync(c, from, tx));
        if (s.Anchor != "FROM") nodes.UnionWith(await FlowAncestorsAsync(c, to, tx));
        var roles = Csv(s.RoleCodes);
        if (nodes.Count == 0 || roles.Length == 0) return new List<long>();

        return (await c.QueryAsync<long>(@"SELECT DISTINCT ua.employee_id FROM user_accounts ua JOIN roles r ON r.id = ua.role_id
            JOIN master_employee_organization_mapping m ON m.employee_id = ua.employee_id AND m.is_current = 1
            WHERE ua.is_active = 1 AND r.role_code IN @roles AND m.node_id IN @nodes", new { roles, nodes = nodes.ToList() }, tx)).ToList();
    }

    public static async Task NotifyAsync(IDbConnection c, IDbTransaction? tx, IEnumerable<long> ids, string title, string? body, string? link)
    {
        foreach (var id in ids.Where(x => x > 0).Distinct())
            await c.ExecuteAsync("INSERT INTO app_notification (employee_id, title, body, link) VALUES (@id,@title,@body,@link)", new { id, title, body, link }, tx);
    }

    public static async Task NotifyStepAsync(IDbConnection c, IDbTransaction? tx, Req r, StepRow step, string title) =>
        await NotifyAsync(c, tx, await ApproversAsync(c, step, r.FromNodeId, r.ToNodeId, tx), title, $"{r.RequestNo} · {step.Name}", $"/transfers/{r.Id}");

    // ---- rule engine ----
    // a policy "input" (group, date of joining, date of birth) read from whichever employee-master field the admin mapped to it
    public static async Task<string?> ReadInputAsync(IDbConnection c, int policyId, string key, long employeeId)
    {
        var m = await c.QueryFirstOrDefaultAsync("SELECT table_name, column_name FROM tr_field_map WHERE policy_id=@policyId AND input_key=@key", new { policyId, key }) as IDictionary<string, object>;
        if (m == null) return null;
        var table = Convert.ToString(m["table_name"]) ?? "";
        var col = Convert.ToString(m["column_name"]) ?? "";
        if (!IdentRx.IsMatch(table) || !IdentRx.IsMatch(col)) return null;

        var row = await c.QueryFirstOrDefaultAsync($"SELECT `{col}` AS v FROM `{table}` WHERE employee_id=@employeeId", new { employeeId }) as IDictionary<string, object>;
        if (row == null || row["v"] is null or DBNull) return null;
        return row["v"] is DateTime d ? d.ToString("yyyy-MM-dd") : Convert.ToString(row["v"], CultureInfo.InvariantCulture);
    }

    public static async Task<(string State, string? Message)> RunRuleAsync(IDbConnection c, int policyId, long emp, long? partner, string kind, JsonElement p, DateTime since, DateTime today)
    {
        switch (kind)
        {
            case "MIN_TENURE":
            {
                var need = Num(p, "default", 0);
                var group = await ReadInputAsync(c, policyId, "GROUP", emp);
                if (group != null && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("byGroup", out var bg) && bg.ValueKind == JsonValueKind.Object
                    && bg.TryGetProperty(group, out var gv) && gv.ValueKind == JsonValueKind.Number) need = gv.GetDouble();
                var years = (today - since).TotalDays / 365.25;
                return years >= need ? ("PASS", $"Stayed {years:0.0} years at the current place (required {need:0.#}).") : ("FAIL", $"Stayed only {years:0.0} years at the current place; {need:0.#} years are required.");
            }
            case "MIN_SERVICE":
            {
                if (!TryDate(await ReadInputAsync(c, policyId, "JOIN_DATE", emp), out var join)) return ("SKIP", "Date of joining is not available, so this could not be checked.");
                var need = Num(p, "years", 0);
                var years = (today - join).TotalDays / 365.25;
                return years >= need ? ("PASS", $"{years:0.0} years of service (required {need:0.#}).") : ("FAIL", $"Only {years:0.0} years of service; {need:0.#} years are required.");
            }
            case "PROBATION":
            {
                if (!TryDate(await ReadInputAsync(c, policyId, "JOIN_DATE", emp), out var join)) return ("SKIP", "Date of joining is not available, so this could not be checked.");
                var need = Num(p, "months", 0);
                return (today - join).TotalDays / 30.4375 >= need ? ("PASS", "Probation period is over.") : ("FAIL", $"Still within the first {need:0.#} months of service.");
            }
            case "NEAR_RETIREMENT":
            {
                if (!TryDate(await ReadInputAsync(c, policyId, "DOB", emp), out var dob)) return ("SKIP", "Date of birth is not available, so this could not be checked.");
                var retires = dob.AddYears((int)Num(p, "retirementAge", 60));
                var months = Num(p, "months", 0);
                return (retires - today).TotalDays / 30.4375 > months ? ("PASS", $"Retires on {retires:yyyy-MM-dd}.") : ("FAIL", $"Retires on {retires:yyyy-MM-dd}, within {months:0.#} months.");
            }
            case "FREQUENCY":
            {
                var years = Num(p, "years", 1);
                var max = (int)Num(p, "maxTransfers", 1);
                var count = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tr_request WHERE (employee_id=@emp OR employee2_id=@emp) AND status='COMPLETED' AND COALESCE(join_date, relieve_date) >= @since",
                    new { emp, since = today.AddYears(-(int)Math.Ceiling(years)) });
                return count < max ? ("PASS", $"{count} transfer(s) in the last {years:0.#} years.") : ("FAIL", $"{count} transfer(s) already completed in the last {years:0.#} years (maximum {max}).");
            }
            case "MUTUAL_MATCH":
            {
                if (partner == null) return ("SKIP", "Applies to mutual transfers only.");
                var fails = new List<string>();
                if (Num(p, "sameGroup", 1) == 1)
                {
                    var g1 = await ReadInputAsync(c, policyId, "GROUP", emp);
                    var g2 = await ReadInputAsync(c, policyId, "GROUP", partner.Value);
                    if (g1 == null || g2 == null) return ("SKIP", "Employee group is not available for both employees.");
                    if (g1 != g2) fails.Add("different employee groups");
                }
                if (Num(p, "sameDesignation", 1) == 1)
                {
                    var d1 = await c.ExecuteScalarAsync<int?>("SELECT designation_id FROM employee_master WHERE employee_id=@e", new { e = emp });
                    var d2 = await c.ExecuteScalarAsync<int?>("SELECT designation_id FROM employee_master WHERE employee_id=@e", new { e = partner.Value });
                    if (d1 != d2) fails.Add("different designations");
                }
                return fails.Count == 0 ? ("PASS", "Both employees hold the same grade.") : ("FAIL", "The two employees have " + string.Join(" and ", fails) + ".");
            }
        }
        return ("SKIP", "Unknown rule type.");
    }

    public static async Task<List<CheckResult>> EvaluateAsync(IDbConnection c, int policyId, long emp, string typeKey, string? categoryKey, DateTime since, long? partner, int subject)
    {
        var rules = (await c.QueryAsync<RuleRow>($"SELECT {RuleCols} FROM tr_rule WHERE policy_id=@policyId AND is_enabled=1 ORDER BY sort_order, id", new { policyId })).ToList();
        var results = new List<CheckResult>();
        var today = DateTime.Today;

        foreach (var r in rules)
        {
            if (subject == 2 && r.RuleKind == "MUTUAL_MATCH") continue;
            var applies = Csv(r.AppliesTypes);
            if (!applies.Contains("*") && !applies.Contains(typeKey, StringComparer.OrdinalIgnoreCase)) continue;

            var name = subject == 2 ? r.Name + " (other employee)" : r.Name;
            if (categoryKey != null && InCsv(r.ExemptCategories, categoryKey))
            {
                results.Add(new CheckResult { Subject = subject, RuleKey = r.RuleKey, Name = name, Strength = r.Strength, State = "PASS", Message = "Not applicable for this reason category." });
                continue;
            }
            var (state, msg) = await RunRuleAsync(c, policyId, emp, partner, r.RuleKind, ParseParams(r.ParamsJson), since, today);
            results.Add(new CheckResult { Subject = subject, RuleKey = r.RuleKey, Name = name, Strength = r.Strength, State = state, Message = msg });
        }
        return results;
    }

    // ---- vacancy ----
    public static async Task<(int? Sanctioned, int Working, int Incoming)> VacancyAsync(IDbConnection c, int nodeId, int designationId, long? exclude)
    {
        var s = await c.ExecuteScalarAsync<int?>("SELECT sanctioned FROM tr_sanctioned WHERE node_id=@n AND designation_id=@d", new { n = nodeId, d = designationId });
        var w = await c.ExecuteScalarAsync<int>(@"SELECT COUNT(*) FROM employee_master e JOIN master_employee_organization_mapping m ON m.employee_id = e.employee_id AND m.is_current = 1
            WHERE m.node_id=@n AND e.designation_id=@d AND e.status='ACTIVE'", new { n = nodeId, d = designationId });
        var i = await c.ExecuteScalarAsync<int>($@"SELECT COUNT(*) FROM tr_request r JOIN employee_master e ON e.employee_id = r.employee_id
            WHERE r.to_node_id=@n AND r.employee2_id IS NULL AND r.from_node_id<>@n AND COALESCE(r.to_designation_id, e.designation_id)=@d
              AND ((r.status IN ({OpenSql}) AND (r.window_id IS NULL OR r.sel_status='SELECTED')) OR (r.status='COMPLETED' AND r.posting_applied=0))
              AND (@x IS NULL OR r.id<>@x)", new { n = nodeId, d = designationId, x = exclude });
        return (s, w, i);
    }

    public static async Task<CheckResult> VacancyCheckAsync(IDbConnection c, string strength, CatRow? cat, int nodeId, int? designationId, long? exclude)
    {
        var res = new CheckResult { RuleKey = "VACANCY", Name = "Vacancy at the destination", Strength = strength };
        if (cat?.WaivesVacancy == 1) { res.State = "PASS"; res.Message = "Vacancy is not required for this reason category."; return res; }
        if (designationId == null) { res.State = "SKIP"; res.Message = "The employee's designation is not set, so vacancy could not be checked."; return res; }

        var (sanc, work, inc) = await VacancyAsync(c, nodeId, designationId.Value, exclude);
        if (sanc == null) { res.State = "SKIP"; res.Message = "No sanctioned strength is recorded for this node and designation."; return res; }

        var vacancy = sanc.Value - work - inc;
        res.State = vacancy > 0 ? "PASS" : "FAIL";
        res.Message = $"Sanctioned {sanc}, working {work}, already incoming {inc}: {Math.Max(vacancy, 0)} vacancy.";
        return res;
    }

    // ---- preparing a request: every hard check plus the rule results ----
    private static async Task<int?> ChangeAsync(IDbConnection c, string setting, int? value, string table, string label)
    {
        if (setting == "NONE") return null;
        if (value == null) { if (setting == "REQUIRED") throw new TrException($"Choose the new {label}."); return null; }
        if (await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE id=@v AND active=1", new { v = value }) == 0) throw new TrException($"Choose a valid {label}.");
        return value;
    }

    public static async Task<Prep> PrepareAsync(IDbConnection c, Actor me, SubmitBody b, bool canRaiseOthers)
    {
        var policy = await c.QueryFirstOrDefaultAsync<PolicyRow>($"SELECT {PolicyCols} FROM tr_policy WHERE status='ACTIVE' ORDER BY id DESC LIMIT 1")
            ?? throw new TrException("No transfer policy is active. An administrator must finalize one first.", 409);
        var typeKey = (b.TypeKey ?? "").Trim().ToUpperInvariant();
        var type = await c.QueryFirstOrDefaultAsync<TypeRow>($"SELECT {TypeCols} FROM tr_type WHERE policy_id=@pid AND type_key=@k AND is_enabled=1", new { pid = policy.Id, k = typeKey })
            ?? throw new TrException("Choose a valid transfer type.");

        var forOther = b.EmployeeId != me.Id;
        if (forOther && !canRaiseOthers) throw new TrException("You can only raise a transfer request for yourself.", 403);
        if (!forOther && type.InitiatedBy == "MANAGEMENT") throw new TrException("This transfer type can only be started by management.", 403);

        var from = await CurrentMappingAsync(c, b.EmployeeId) ?? throw new TrException("The employee has no current organization assignment.", 409);
        if (forOther && !me.Scope.Contains(from.NodeId)) throw new TrException("You do not have access to this employee.", 403);

        CatRow? cat = null;
        if (type.NeedsCategory == 1)
            cat = await c.QueryFirstOrDefaultAsync<CatRow>($"SELECT {CatCols} FROM tr_category WHERE policy_id=@pid AND category_key=@k AND is_enabled=1", new { pid = policy.Id, k = (b.CategoryKey ?? "").Trim().ToUpperInvariant() })
                ?? throw new TrException("Choose the reason category.");

        // destination
        long? partner = null;
        int toNode;
        if (type.ProcMode == "MUTUAL")
        {
            partner = b.PartnerId ?? throw new TrException("Choose the employee to exchange with.");
            if (partner == b.EmployeeId) throw new TrException("Choose a different employee to exchange with.");
            toNode = (await CurrentMappingAsync(c, partner.Value) ?? throw new TrException("The other employee has no current organization assignment.", 409)).NodeId;
        }
        else toNode = b.ToNodeId > 0 ? b.ToNodeId : from.NodeId;

        var toOrg = await c.ExecuteScalarAsync<int?>("SELECT organization_id FROM master_organization_nodes WHERE node_id=@id AND deleted_at IS NULL", new { id = toNode })
            ?? throw new TrException("Choose a valid destination node.");

        // department / designation changes
        var mutual = type.ProcMode == "MUTUAL";
        var toDept = await ChangeAsync(c, mutual ? "NONE" : type.DeptChange, b.ToDepartmentId, "departments", "department");
        var toDesig = await ChangeAsync(c, mutual ? "NONE" : type.DesigChange, b.ToDesignationId, "designations", "designation");
        var cur = await c.QueryFirstOrDefaultAsync("SELECT department_id, designation_id FROM employee_master WHERE employee_id=@e", new { e = b.EmployeeId }) as IDictionary<string, object>;
        int? curDept = cur == null || cur["department_id"] is null or DBNull ? null : Convert.ToInt32(cur["department_id"]);
        int? curDesig = cur == null || cur["designation_id"] is null or DBNull ? null : Convert.ToInt32(cur["designation_id"]);
        if (toDept == curDept) toDept = null;
        if (toDesig == curDesig) toDesig = null;
        if (toNode == from.NodeId && toDept == null && toDesig == null) throw new TrException("Nothing would change: the employee is already at this node.");

        // window
        WindowRow? win = null;
        if (type.ProcMode == "WINDOW")
        {
            win = await c.QueryFirstOrDefaultAsync<WindowRow>($"SELECT {WindowCols} FROM tr_window WHERE id=@id", new { id = b.WindowId }) ?? throw new TrException("Choose the transfer window.");
            if (win.TypeKey != type.TypeKey) throw new TrException("This window is not for this transfer type.");
            var now = DateTime.Now;
            if (win.Status != "PUBLISHED" || now < win.ApplyFrom || now > win.ApplyTo)
                throw new TrException($"Applications for this window are open from {win.ApplyFrom:dd MMM yyyy HH:mm} to {win.ApplyTo:dd MMM yyyy HH:mm}.", 409);
            if (win.ScopeNodeId != null && !await IsUnderAsync(c, from.NodeId, win.ScopeNodeId.Value)) throw new TrException("This window is not open for the employee's current posting.", 403);
        }

        foreach (var e in new[] { (long?)b.EmployeeId, partner })
        {
            if (e == null) continue;
            var open = await c.ExecuteScalarAsync<long?>($"SELECT id FROM tr_request WHERE (employee_id=@e OR employee2_id=@e) AND status IN ({OpenSql}) LIMIT 1", new { e });
            if (open != null) throw new TrException($"{(e == b.EmployeeId ? "This employee" : "The other employee")} already has an open transfer request (#{open}).", 409);
        }

        var checks = await EvaluateAsync(c, policy.Id, b.EmployeeId, type.TypeKey, cat?.CategoryKey, from.EffectiveFrom, partner, 1);
        if (partner != null)
        {
            var pm = await CurrentMappingAsync(c, partner.Value);
            checks.AddRange(await EvaluateAsync(c, policy.Id, partner.Value, type.TypeKey, cat?.CategoryKey, pm!.EffectiveFrom, null, 2));
        }
        if (type.VacancyCheck != "NONE" && type.ProcMode == "INDIVIDUAL")
            checks.Add(await VacancyCheckAsync(c, type.VacancyCheck, cat, toNode, toDesig ?? curDesig, null));

        return new Prep
        {
            Policy = policy, Type = type, Category = cat, From = from, ToNodeId = toNode, ToOrgId = toOrg, FromDepartmentId = curDept, FromDesignationId = curDesig,
            ToDepartmentId = toDept, ToDesignationId = toDesig, PartnerId = partner, Window = win, Checks = checks,
            Steps = (await c.QueryAsync<StepRow>($"SELECT {PStepCols} FROM tr_step WHERE policy_id=@pid AND type_key=@k ORDER BY step_order", new { pid = policy.Id, k = type.TypeKey })).ToList()
        };
    }

    // ---- the one place where a posting really changes ----
    private static Task AuditAsync(IDbConnection c, IDbTransaction tx, long emp, string section, string col, string? oldV, string? newV, long by) =>
        c.ExecuteAsync("INSERT INTO employee_audit_log (employee_id, action, section_key, column_name, old_value, new_value, changed_by) VALUES (@emp,'UPDATE',@section,@col,@oldV,@newV,@by)",
            new { emp, section, col, oldV, newV, by }, tx);

    private static async Task MovePersonAsync(IDbConnection c, IDbTransaction tx, long emp, int fromNode, int toNode, int toOrg, DateTime eff, long by, int? dept, int? desig)
    {
        if (fromNode != toNode)
        {
            var cur = await CurrentMappingAsync(c, emp, tx);
            if (cur == null || cur.NodeId != fromNode) throw new TrException("The employee's current posting has changed since this request was raised. Withdraw it and raise a new one.", 409);
            if (eff.Date <= cur.EffectiveFrom.Date) throw new TrException($"The date must be after the current assignment date ({cur.EffectiveFrom:yyyy-MM-dd}).");

            var oldPath = await NodePathAsync(c, cur.NodeId, tx);
            var newPath = await NodePathAsync(c, toNode, tx);
            await c.ExecuteAsync("UPDATE master_employee_organization_mapping SET is_current=0, effective_to=@to WHERE mapping_id=@mid", new { to = eff.Date.AddDays(-1), mid = cur.MappingId }, tx);
            await c.ExecuteAsync("INSERT INTO master_employee_organization_mapping (employee_id, organization_id, node_id, effective_from, is_current, assigned_by) VALUES (@e,@o,@n,@f,1,@by)",
                new { e = emp, o = toOrg, n = toNode, f = eff.Date, by }, tx);
            await AuditAsync(c, tx, emp, "organization", "__org", oldPath, newPath, by);
        }

        if (dept != null)
        {
            var oldId = await c.ExecuteScalarAsync<int?>("SELECT department_id FROM employee_master WHERE employee_id=@e", new { e = emp }, tx);
            var oldName = oldId == null ? null : await c.ExecuteScalarAsync<string?>("SELECT dep_name FROM departments WHERE id=@i", new { i = oldId }, tx);
            var newName = await c.ExecuteScalarAsync<string?>("SELECT dep_name FROM departments WHERE id=@i", new { i = dept }, tx);
            await c.ExecuteAsync("UPDATE employee_master SET department_id=@d WHERE employee_id=@e", new { d = dept, e = emp }, tx);
            await AuditAsync(c, tx, emp, "work", "department_id", oldName, newName, by);
        }
        if (desig != null)
        {
            var oldId = await c.ExecuteScalarAsync<int?>("SELECT designation_id FROM employee_master WHERE employee_id=@e", new { e = emp }, tx);
            var oldName = oldId == null ? null : await c.ExecuteScalarAsync<string?>("SELECT name FROM designations WHERE id=@i", new { i = oldId }, tx);
            var newName = await c.ExecuteScalarAsync<string?>("SELECT name FROM designations WHERE id=@i", new { i = desig }, tx);
            await c.ExecuteAsync("UPDATE employee_master SET designation_id=@d WHERE employee_id=@e", new { d = desig, e = emp }, tx);
            await AuditAsync(c, tx, emp, "work", "designation_id", oldName, newName, by);
        }
        await c.ExecuteAsync("UPDATE employee_master SET row_version = row_version + 1 WHERE employee_id=@e", new { e = emp }, tx); // an open edit page now conflicts instead of overwriting
    }

    public static async Task ApplyPostingAsync(IDbConnection c, IDbTransaction tx, Req r, DateTime effective, long by)
    {
        await MovePersonAsync(c, tx, r.EmployeeId, r.FromNodeId, r.ToNodeId, r.ToOrgId, effective, by, r.ToDepartmentId, r.ToDesignationId);
        if (r.Employee2Id != null) await MovePersonAsync(c, tx, r.Employee2Id.Value, r.ToNodeId, r.FromNodeId, r.FromOrgId, effective, by, null, null);
        await c.ExecuteAsync("UPDATE tr_request SET posting_applied=1, apply_at=NULL, posting_error=NULL WHERE id=@id", new { id = r.Id }, tx);
    }

    // today or earlier: change the posting now; a future date: the scheduler applies it on that day
    public static async Task ApplyOrScheduleAsync(IDbConnection c, IDbTransaction tx, Req r, DateTime effective, long by)
    {
        if (effective.Date <= DateTime.Today) await ApplyPostingAsync(c, tx, r, effective, by);
        else await c.ExecuteAsync("UPDATE tr_request SET apply_at=@d, posting_applied=0 WHERE id=@id", new { d = effective.Date, id = r.Id }, tx);
    }
}

// ---------------------------------------------------------------- scheduler
// Every 30 minutes: applies posting changes whose date has arrived.
public class TransferScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TransferScheduler> _log;

    public TransferScheduler(IServiceScopeFactory scopes, ILogger<TransferScheduler> log) { _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnceAsync(); } catch (Exception ex) { _log.LogError(ex, "Transfer scheduler failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stop); } catch (TaskCanceledException) { }
        }
    }

    private async Task RunOnceAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbConnectionFactory>();
        await using var c = db.CreateConnection();
        await c.OpenAsync();

        var due = (await c.QueryAsync<Req>($"SELECT {TransferCore.ReqCols} FROM tr_request WHERE posting_applied=0 AND apply_at IS NOT NULL AND apply_at <= CURDATE() AND status IN ('RELIEVED','COMPLETED')")).ToList();
        foreach (var r in due)
        {
            using var tx = c.BeginTransaction();
            try
            {
                await TransferCore.ApplyPostingAsync(c, tx, r, r.ApplyAt!.Value, r.CreatedBy);
                await TransferCore.NotifyAsync(c, tx, new long[] { r.EmployeeId, r.CreatedBy }, "Transfer posting applied", $"{r.RequestNo}: the posting change is now in effect.", $"/transfers/{r.Id}");
                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _log.LogError(ex, "Could not apply posting for transfer {Id}", r.Id);
                await c.ExecuteAsync("UPDATE tr_request SET posting_error=@e WHERE id=@id", new { id = r.Id, e = ex.Message.Length > 290 ? ex.Message[..290] : ex.Message });
            }
        }
    }
}