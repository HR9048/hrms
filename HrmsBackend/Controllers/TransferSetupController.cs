using System.Data;
using System.Text.Json;
using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Filters;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static HrmsBackend.Services.TransferCore;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize]
public class TransferSetupController : TransferBase
{
    private const string AdminRole = "SUPERADMIN";
    private const string WindowsRoute = "/transfers/windows";
    private const string VacancyRoute = "/transfers/vacancy";

    public TransferSetupController(DbConnectionFactory db) : base(db) { }

    public record TypeInput(string TypeKey, string Name, string ProcMode, string InitiatedBy, bool NeedsConsent, bool NeedsCategory, string VacancyCheck, string DeptChange, string DesigChange, string SeniorityEffect, string ApplyOn, bool IsEnabled);
    public record CategoryInput(string CategoryKey, string Name, int PriorityRank, string? DocLabel, bool WaivesVacancy, bool IsEnabled);
    public record RuleInput(string RuleKey, string Name, string RuleKind, JsonElement? Params, List<string>? AppliesTypes, List<string>? ExemptCategories, string Strength, string? RelaxRoleCode, bool IsEnabled);
    public record StepInput(string TypeKey, string Name, string Action, string Authority, List<string>? RoleCodes, List<long>? EmployeeIds, string Anchor);
    public record RotationInput(string RuleKey, string Name, List<int>? DesignationIds, decimal MaxYears, int WarnMonths, bool IsEnabled);
    public record FieldMapInput(string InputKey, string TableName, string ColumnName);
    public record PolicyCreate(string Name, string? Preset);
    public record PolicyMeta(string? Notes, string? EffectiveFrom);
    public record QuotaInput(string CategoryKey, int MaxSeats);
    public record WindowInput(string Name, string TypeKey, int? ScopeNodeId, string ApplyFrom, string ApplyTo, string? ObjectionFrom, string? ObjectionTo, string? OrdersFrom, string? EffectiveOn, int? TotalSeats, string? GeneralCategory, bool OverflowToGeneral, List<QuotaInput>? Quotas);
    public record ResolveBody(string Resolution);
    public record VacancyInput(int NodeId, int DesignationId, int Sanctioned);

    private static readonly System.Text.RegularExpressions.Regex KeyRx = new("^[A-Z][A-Z0-9_]{1,29}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly HashSet<string> Modes = new() { "INDIVIDUAL", "MUTUAL", "WINDOW" };
    private static readonly HashSet<string> Initiators = new() { "MANAGEMENT", "EMPLOYEE", "BOTH" };
    private static readonly HashSet<string> Vacancy = new() { "NONE", "WARN", "RELAX", "BLOCK" };
    private static readonly HashSet<string> Changes = new() { "NONE", "OPTIONAL", "REQUIRED" };
    private static readonly HashSet<string> Seniority = new() { "RETAIN", "BOTTOM", "LOCAL" };
    private static readonly HashSet<string> ApplyOn = new() { "RELIEVING", "JOINING" };
    private static readonly HashSet<string> Strengths = new() { "BLOCK", "RELAX", "WARN" };
    private static readonly HashSet<string> Actions = new() { "RECOMMEND", "NOC", "APPROVE" };
    private static readonly HashSet<string> Anchors = new() { "FROM", "TO", "EITHER" };
    private static readonly HashSet<string> InputKeys = new() { "GROUP", "JOIN_DATE", "DOB" };

    private static readonly Dictionary<string, (string Name, double Min, double Max)[]> ParamSpec = new()
    {
        ["MIN_TENURE"] = new[] { ("default", 0d, 50d) },
        ["MIN_SERVICE"] = new[] { ("years", 0d, 60d) },
        ["PROBATION"] = new[] { ("months", 0d, 120d) },
        ["NEAR_RETIREMENT"] = new[] { ("retirementAge", 40d, 80d), ("months", 0d, 120d) },
        ["FREQUENCY"] = new[] { ("maxTransfers", 1d, 50d), ("years", 0.5d, 50d) },
        ["MUTUAL_MATCH"] = new[] { ("sameGroup", 0d, 1d), ("sameDesignation", 0d, 1d) }
    };

    private static async Task<PolicyRow> GetPolicyAsync(IDbConnection c, int id) =>
        await c.QueryFirstOrDefaultAsync<PolicyRow>($"SELECT {PolicyCols} FROM tr_policy WHERE id=@id", new { id }) ?? throw new TrException("Policy not found.", 404);

    private static async Task<PolicyRow> AssertDraftAsync(IDbConnection c, int id)
    {
        var p = await GetPolicyAsync(c, id);
        if (p.Status != "DRAFT") throw new TrException("A finalized policy cannot be changed. Create a new version instead.", 409);
        return p;
    }

    private static async Task ReplaceAsync(IDbConnection c, string table, int id, string insertSql, List<object> rows, Func<IDbTransaction, Task>? after = null)
    {
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync($"DELETE FROM {table} WHERE policy_id=@id", new { id }, tx);
        foreach (var r in rows) await c.ExecuteAsync(insertSql, r, tx);
        if (after != null) await after(tx);
        tx.Commit();
    }

    private static string Key(string? raw, string what)
    {
        var k = (raw ?? "").Trim().ToUpperInvariant();
        if (!KeyRx.IsMatch(k)) throw new TrException($"{what} key \"{raw}\" is invalid (2 to 30 capital letters, digits or underscores, starting with a letter).");
        return k;
    }

    // =========================================================
    // POLICIES (super admin)
    // =========================================================

    [HttpGet("policies")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> Policies() => Run(async c =>
        (await c.QueryAsync(@"SELECT p.id AS id, p.name AS name, p.version AS version, p.status AS status, p.effective_from AS effectiveFrom,
            (SELECT COUNT(*) FROM tr_request r WHERE r.policy_id = p.id) AS requests FROM tr_policy p ORDER BY p.name, p.version DESC")).ToDictList());

    [HttpGet("policies/{id:int}")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> GetPolicy(int id) => Run(async c =>
    {
        var p = await GetPolicyAsync(c, id);
        var steps = (await c.QueryAsync<StepRow>($"SELECT {PStepCols} FROM tr_step WHERE policy_id=@id ORDER BY type_key, step_order", new { id })).ToList();
        var empIds = steps.SelectMany(s => Csv(s.EmployeeIds)).Select(x => long.TryParse(x, out var v) ? v : 0).Where(x => x > 0).Distinct().ToList();

        return new
        {
            policy = new { p.Id, p.Name, p.Version, p.Status, EffectiveFrom = p.EffectiveFrom?.ToString("yyyy-MM-dd"), p.Notes },
            types = (await c.QueryAsync<TypeRow>($"SELECT {TypeCols} FROM tr_type WHERE policy_id=@id ORDER BY sort_order, id", new { id }))
                .Select(t => new { t.TypeKey, t.Name, t.ProcMode, t.InitiatedBy, NeedsConsent = t.NeedsConsent == 1, NeedsCategory = t.NeedsCategory == 1, t.VacancyCheck, t.DeptChange, t.DesigChange, t.SeniorityEffect, t.ApplyOn, IsEnabled = t.IsEnabled == 1 }).ToList(),
            categories = (await c.QueryAsync<CatRow>($"SELECT {CatCols} FROM tr_category WHERE policy_id=@id ORDER BY priority_rank, id", new { id }))
                .Select(t => new { t.CategoryKey, t.Name, t.PriorityRank, t.DocLabel, WaivesVacancy = t.WaivesVacancy == 1, IsEnabled = t.IsEnabled == 1 }).ToList(),
            rules = (await c.QueryAsync<RuleRow>($"SELECT {RuleCols} FROM tr_rule WHERE policy_id=@id ORDER BY sort_order, id", new { id }))
                .Select(r => new { r.RuleKey, r.Name, r.RuleKind, Params = ParseParams(r.ParamsJson), AppliesTypes = Csv(r.AppliesTypes), ExemptCategories = Csv(r.ExemptCategories), r.Strength, r.RelaxRoleCode, IsEnabled = r.IsEnabled == 1 }).ToList(),
            steps = steps.Select(s => new { s.TypeKey, s.Name, s.Action, s.Authority, RoleCodes = Csv(s.RoleCodes), EmployeeIds = Csv(s.EmployeeIds).Select(x => long.TryParse(x, out var v) ? v : 0).Where(x => x > 0).ToList(), s.Anchor }).ToList(),
            rotation = (await c.QueryAsync(@"SELECT rule_key AS ruleKey, name AS name, designation_ids AS designationIds, max_years AS maxYears, warn_months AS warnMonths, is_enabled AS isEnabled FROM tr_rotation WHERE policy_id=@id ORDER BY id", new { id }))
                .Select(x => { var d = (IDictionary<string, object>)x; return new { ruleKey = d["ruleKey"], name = d["name"], designationIds = Csv(Convert.ToString(d["designationIds"])).Select(int.Parse).ToList(), maxYears = d["maxYears"], warnMonths = d["warnMonths"], isEnabled = Convert.ToInt32(d["isEnabled"]) == 1 }; }).ToList(),
            fieldMap = (await c.QueryAsync("SELECT input_key AS inputKey, table_name AS tableName, column_name AS columnName FROM tr_field_map WHERE policy_id=@id", new { id })).ToDictList(),
            roles = (await c.QueryAsync("SELECT role_code AS code, role_name AS name FROM roles WHERE active=1 ORDER BY sequence, role_name")).ToDictList(),
            designations = (await c.QueryAsync("SELECT id AS id, name AS name FROM designations WHERE active=1 ORDER BY sequence, name")).ToDictList(),
            people = empIds.Count == 0 ? new List<Dictionary<string, object?>>() : (await c.QueryAsync("SELECT employee_id AS id, CONCAT(employee_code,' · ',TRIM(CONCAT(first_name,' ',COALESCE(last_name,'')))) AS label FROM employee_master WHERE employee_id IN @ids", new { ids = empIds })).ToDictList(),
            fieldOptions = (await c.QueryAsync(@"SELECT s.table_name AS tableName, d.column_name AS columnName, d.label AS label, d.data_type AS dataType, s.title AS section
                FROM employee_field_definitions d JOIN employee_sections s ON s.id = d.section_id WHERE s.is_finalized = 1 AND d.data_type IN ('DATE','DROPDOWN','TEXT','NUMBER') ORDER BY s.sort_order, d.sort_order")).ToDictList()
        };
    });

    [HttpPost("policies")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> CreatePolicy([FromBody] PolicyCreate b)
    {
        var by = MyId();
        return Run(async c =>
        {
            var name = (b.Name ?? "").Trim();
            if (name.Length is < 2 or > 100) throw new TrException("Enter a policy name (2 to 100 characters).");
            using var tx = c.BeginTransaction();
            var version = await c.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(version),0)+1 FROM tr_policy WHERE name=@name", new { name }, tx);
            await c.ExecuteAsync("INSERT INTO tr_policy (name, version, status, created_by) VALUES (@name,@version,'DRAFT',@by)", new { name, version, by }, tx);
            var id = await c.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);
            if (string.Equals(b.Preset, "starter", StringComparison.OrdinalIgnoreCase)) await SeedStarterAsync(c, tx, id);
            tx.Commit();
            return new { id };
        });
    }

    [HttpPut("policies/{id:int}")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> UpdatePolicy(int id, [FromBody] PolicyMeta b) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        await c.ExecuteAsync("UPDATE tr_policy SET notes=@notes, effective_from=@eff WHERE id=@id", new { id, notes = string.IsNullOrWhiteSpace(b.Notes) ? null : b.Notes.Trim(), eff = OptDate(b.EffectiveFrom, "Effective date") });
        return new { message = "Saved." };
    });

    [HttpDelete("policies/{id:int}")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> DeletePolicy(int id) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        using var tx = c.BeginTransaction();
        foreach (var t in new[] { "tr_type", "tr_category", "tr_rule", "tr_step", "tr_rotation", "tr_field_map" }) await c.ExecuteAsync($"DELETE FROM {t} WHERE policy_id=@id", new { id }, tx);
        await c.ExecuteAsync("DELETE FROM tr_policy WHERE id=@id", new { id }, tx);
        tx.Commit();
        return new { message = "Deleted." };
    });

    [HttpPost("policies/{id:int}/clone")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> ClonePolicy(int id)
    {
        var by = MyId();
        return Run(async c =>
        {
            var src = await GetPolicyAsync(c, id);
            using var tx = c.BeginTransaction();
            var version = await c.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(version),0)+1 FROM tr_policy WHERE name=@n", new { n = src.Name }, tx);
            await c.ExecuteAsync("INSERT INTO tr_policy (name, version, status, notes, created_by) VALUES (@n,@v,'DRAFT',@notes,@by)", new { n = src.Name, v = version, notes = src.Notes, by }, tx);
            var nid = await c.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);
            var p = new { n = nid, o = id };
            await c.ExecuteAsync("INSERT INTO tr_type (policy_id,type_key,name,proc_mode,initiated_by,needs_consent,needs_category,vacancy_check,dept_change,desig_change,seniority_effect,apply_on,is_enabled,sort_order) SELECT @n,type_key,name,proc_mode,initiated_by,needs_consent,needs_category,vacancy_check,dept_change,desig_change,seniority_effect,apply_on,is_enabled,sort_order FROM tr_type WHERE policy_id=@o", p, tx);
            await c.ExecuteAsync("INSERT INTO tr_category (policy_id,category_key,name,priority_rank,doc_label,waives_vacancy,is_enabled) SELECT @n,category_key,name,priority_rank,doc_label,waives_vacancy,is_enabled FROM tr_category WHERE policy_id=@o", p, tx);
            await c.ExecuteAsync("INSERT INTO tr_rule (policy_id,rule_key,name,rule_kind,params_json,applies_types,exempt_categories,strength,relax_role_code,is_enabled,sort_order) SELECT @n,rule_key,name,rule_kind,params_json,applies_types,exempt_categories,strength,relax_role_code,is_enabled,sort_order FROM tr_rule WHERE policy_id=@o", p, tx);
            await c.ExecuteAsync("INSERT INTO tr_step (policy_id,type_key,step_order,name,action,authority,role_codes,employee_ids,anchor) SELECT @n,type_key,step_order,name,action,authority,role_codes,employee_ids,anchor FROM tr_step WHERE policy_id=@o", p, tx);
            await c.ExecuteAsync("INSERT INTO tr_rotation (policy_id,rule_key,name,designation_ids,max_years,warn_months,is_enabled) SELECT @n,rule_key,name,designation_ids,max_years,warn_months,is_enabled FROM tr_rotation WHERE policy_id=@o", p, tx);
            await c.ExecuteAsync("INSERT INTO tr_field_map (policy_id,input_key,table_name,column_name) SELECT @n,input_key,table_name,column_name FROM tr_field_map WHERE policy_id=@o", p, tx);
            tx.Commit();
            return new { id = nid };
        });
    }

    [HttpPost("policies/{id:int}/finalize")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> FinalizePolicy(int id)
    {
        var by = MyId();
        return Run(async c =>
        {
            await AssertDraftAsync(c, id);
            var enabled = (await c.QueryAsync<TypeRow>($"SELECT {TypeCols} FROM tr_type WHERE policy_id=@id AND is_enabled=1", new { id })).ToList();
            if (enabled.Count == 0) throw new TrException("Enable at least one transfer type before finalizing.");

            var roleCodes = (await c.QueryAsync<string>("SELECT role_code FROM roles WHERE active=1")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var enabledCats = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tr_category WHERE policy_id=@id AND is_enabled=1", new { id });

            foreach (var t in enabled)
            {
                var steps = (await c.QueryAsync<StepRow>($"SELECT {PStepCols} FROM tr_step WHERE policy_id=@id AND type_key=@k ORDER BY step_order", new { id, k = t.TypeKey })).ToList();
                if (steps.Count == 0) throw new TrException($"\"{t.Name}\" has no workflow steps.");
                if (steps.Last().Action != "APPROVE") throw new TrException($"The last step of \"{t.Name}\" must be an Approve step.");
                foreach (var s in steps)
                {
                    if (s.Authority == "ROLE")
                    {
                        if (Csv(s.RoleCodes).Length == 0) throw new TrException($"Step \"{s.Name}\" of \"{t.Name}\" has no role.");
                        foreach (var rc in Csv(s.RoleCodes)) if (!roleCodes.Contains(rc)) throw new TrException($"Step \"{s.Name}\" of \"{t.Name}\" uses the role \"{rc}\", which does not exist.");
                    }
                    else if (Csv(s.EmployeeIds).Length == 0) throw new TrException($"Step \"{s.Name}\" of \"{t.Name}\" has no employee.");
                }
                if (t.NeedsCategory == 1 && enabledCats == 0) throw new TrException($"\"{t.Name}\" needs a reason category, but no category is enabled.");
            }

            using var tx = c.BeginTransaction();
            await c.ExecuteAsync("UPDATE tr_policy SET status='RETIRED' WHERE status='ACTIVE'", transaction: tx);
            await c.ExecuteAsync("UPDATE tr_policy SET status='ACTIVE', effective_from=COALESCE(effective_from, CURDATE()), finalized_by=@by, finalized_at=NOW() WHERE id=@id", new { id, by }, tx);
            tx.Commit();
            return new { message = "Policy finalized." };
        });
    }

    [HttpPut("policies/{id:int}/types")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> SaveTypes(int id, [FromBody] List<TypeInput> body) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        var seen = new HashSet<string>();
        var rows = new List<object>();
        var order = 0;
        foreach (var t in body ?? new())
        {
            var key = Key(t.TypeKey, "Type");
            if (!seen.Add(key)) throw new TrException($"Type key {key} is repeated.");
            var name = (t.Name ?? "").Trim();
            if (name.Length is 0 or > 100) throw new TrException("Every type needs a name.");
            if (!Modes.Contains(t.ProcMode) || !Initiators.Contains(t.InitiatedBy) || !Vacancy.Contains(t.VacancyCheck) || !Changes.Contains(t.DeptChange) || !Changes.Contains(t.DesigChange) || !Seniority.Contains(t.SeniorityEffect) || !ApplyOn.Contains(t.ApplyOn))
                throw new TrException($"Check the settings of \"{name}\".");
            rows.Add(new { pid = id, key, name, mode = t.ProcMode, by = t.InitiatedBy, consent = t.NeedsConsent ? 1 : 0, cat = t.NeedsCategory ? 1 : 0, vac = t.VacancyCheck, dept = t.DeptChange, desig = t.DesigChange, sen = t.SeniorityEffect, app = t.ApplyOn, en = t.IsEnabled ? 1 : 0, ord = ++order * 10 });
        }
        await ReplaceAsync(c, "tr_type", id,
            "INSERT INTO tr_type (policy_id,type_key,name,proc_mode,initiated_by,needs_consent,needs_category,vacancy_check,dept_change,desig_change,seniority_effect,apply_on,is_enabled,sort_order) VALUES (@pid,@key,@name,@mode,@by,@consent,@cat,@vac,@dept,@desig,@sen,@app,@en,@ord)",
            rows, tx => c.ExecuteAsync("DELETE FROM tr_step WHERE policy_id=@id AND type_key NOT IN @keys", new { id, keys = seen.ToList() }, tx));
        return new { message = "Saved." };
    });

    [HttpPut("policies/{id:int}/categories")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> SaveCategories(int id, [FromBody] List<CategoryInput> body) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        var seen = new HashSet<string>();
        var rows = new List<object>();
        foreach (var t in body ?? new())
        {
            var key = Key(t.CategoryKey, "Category");
            if (!seen.Add(key)) throw new TrException($"Category key {key} is repeated.");
            var name = (t.Name ?? "").Trim();
            if (name.Length is 0 or > 100) throw new TrException("Every category needs a name.");
            var doc = string.IsNullOrWhiteSpace(t.DocLabel) ? null : t.DocLabel.Trim();
            if (doc is { Length: > 100 }) throw new TrException("Document name is too long.");
            rows.Add(new { pid = id, key, name, rank = Math.Clamp(t.PriorityRank, 1, 999), doc, waive = t.WaivesVacancy ? 1 : 0, en = t.IsEnabled ? 1 : 0 });
        }
        await ReplaceAsync(c, "tr_category", id, "INSERT INTO tr_category (policy_id,category_key,name,priority_rank,doc_label,waives_vacancy,is_enabled) VALUES (@pid,@key,@name,@rank,@doc,@waive,@en)", rows);
        return new { message = "Saved." };
    });

    private static string ValidateParams(string kind, JsonElement? p)
    {
        if (p == null || p.Value.ValueKind != JsonValueKind.Object) throw new TrException("Fill in the rule settings.");
        foreach (var (name, min, max) in ParamSpec[kind])
            if (!p.Value.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number || v.GetDouble() < min || v.GetDouble() > max)
                throw new TrException($"Setting \"{name}\" must be a number between {min:0.#} and {max:0.#}.");
        if (kind == "MIN_TENURE" && p.Value.TryGetProperty("byGroup", out var bg))
        {
            if (bg.ValueKind != JsonValueKind.Object) throw new TrException("Group tenures must be a list of group and years.");
            foreach (var kv in bg.EnumerateObject())
                if (string.IsNullOrWhiteSpace(kv.Name) || kv.Value.ValueKind != JsonValueKind.Number || kv.Value.GetDouble() < 0 || kv.Value.GetDouble() > 50)
                    throw new TrException("Each group tenure needs a group value and a number of years between 0 and 50.");
        }
        return p.Value.GetRawText();
    }

    [HttpPut("policies/{id:int}/rules")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> SaveRules(int id, [FromBody] List<RuleInput> body) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        var typeKeys = (await c.QueryAsync<string>("SELECT type_key FROM tr_type WHERE policy_id=@id", new { id })).ToHashSet();
        var catKeys = (await c.QueryAsync<string>("SELECT category_key FROM tr_category WHERE policy_id=@id", new { id })).ToHashSet();
        var roles = (await c.QueryAsync<string>("SELECT role_code FROM roles WHERE active=1")).ToList();

        var seen = new HashSet<string>();
        var rows = new List<object>();
        var order = 0;
        foreach (var r in body ?? new())
        {
            var key = Key(r.RuleKey, "Rule");
            if (!seen.Add(key)) throw new TrException($"Rule key {key} is repeated.");
            var name = (r.Name ?? "").Trim();
            if (name.Length is 0 or > 120) throw new TrException("Every rule needs a name.");
            var kind = (r.RuleKind ?? "").ToUpperInvariant();
            if (!ParamSpec.ContainsKey(kind)) throw new TrException($"Unknown rule type for \"{name}\".");
            var strength = (r.Strength ?? "").ToUpperInvariant();
            if (!Strengths.Contains(strength)) throw new TrException($"Choose the strength of \"{name}\".");

            var applies = (r.AppliesTypes ?? new()).Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
            if (applies.Count == 0 || applies.Contains("*")) applies = new List<string> { "*" };
            else foreach (var a in applies) if (!typeKeys.Contains(a)) throw new TrException($"Rule \"{name}\": unknown transfer type {a}. Save the types first.");

            var exempt = (r.ExemptCategories ?? new()).Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
            foreach (var e in exempt) if (!catKeys.Contains(e)) throw new TrException($"Rule \"{name}\": unknown category {e}. Save the categories first.");

            string? relax = null;
            if (strength == "RELAX" && !string.IsNullOrWhiteSpace(r.RelaxRoleCode))
                relax = roles.FirstOrDefault(x => x.Equals(r.RelaxRoleCode.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new TrException($"Rule \"{name}\": the role that may relax it does not exist.");

            rows.Add(new { pid = id, key, name, kind, json = ValidateParams(kind, r.Params), applies = string.Join(",", applies), exempt = exempt.Count == 0 ? null : string.Join(",", exempt), strength, relax, en = r.IsEnabled ? 1 : 0, ord = ++order * 10 });
        }
        await ReplaceAsync(c, "tr_rule", id, "INSERT INTO tr_rule (policy_id,rule_key,name,rule_kind,params_json,applies_types,exempt_categories,strength,relax_role_code,is_enabled,sort_order) VALUES (@pid,@key,@name,@kind,@json,@applies,@exempt,@strength,@relax,@en,@ord)", rows);
        return new { message = "Saved." };
    });

    [HttpPut("policies/{id:int}/steps")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> SaveSteps(int id, [FromBody] List<StepInput> body) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        var typeKeys = (await c.QueryAsync<string>("SELECT type_key FROM tr_type WHERE policy_id=@id", new { id })).ToHashSet();
        var roles = (await c.QueryAsync<string>("SELECT role_code FROM roles WHERE active=1")).ToList();

        var counters = new Dictionary<string, int>();
        var rows = new List<object>();
        foreach (var s in body ?? new())
        {
            var tk = (s.TypeKey ?? "").Trim().ToUpperInvariant();
            if (!typeKeys.Contains(tk)) throw new TrException($"Unknown transfer type {tk}. Save the types first.");
            var name = (s.Name ?? "").Trim();
            if (name.Length is 0 or > 100) throw new TrException("Every step needs a name.");
            var action = (s.Action ?? "").ToUpperInvariant();
            var anchor = (s.Anchor ?? "").ToUpperInvariant();
            var authority = (s.Authority ?? "").ToUpperInvariant();
            if (!Actions.Contains(action) || !Anchors.Contains(anchor) || (authority != "ROLE" && authority != "EMPLOYEE")) throw new TrException($"Check the settings of step \"{name}\".");

            string? roleCsv = null, empCsv = null;
            if (authority == "ROLE")
            {
                var codes = new List<string>();
                foreach (var rc in (s.RoleCodes ?? new()).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                    codes.Add(roles.FirstOrDefault(x => x.Equals(rc, StringComparison.OrdinalIgnoreCase)) ?? throw new TrException($"Step \"{name}\": the role \"{rc}\" does not exist."));
                if (codes.Count == 0) throw new TrException($"Step \"{name}\" needs at least one role.");
                roleCsv = string.Join(",", codes);
            }
            else
            {
                var ids = (s.EmployeeIds ?? new()).Distinct().ToList();
                if (ids.Count == 0) throw new TrException($"Step \"{name}\" needs at least one employee.");
                if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM employee_master WHERE employee_id IN @ids", new { ids }) != ids.Count) throw new TrException($"Step \"{name}\": one of the employees does not exist.");
                empCsv = string.Join(",", ids);
            }

            counters[tk] = counters.GetValueOrDefault(tk) + 1;
            rows.Add(new { pid = id, tk, ord = counters[tk], name, action, authority, roles = roleCsv, emps = empCsv, anchor });
        }
        await ReplaceAsync(c, "tr_step", id, "INSERT INTO tr_step (policy_id,type_key,step_order,name,action,authority,role_codes,employee_ids,anchor) VALUES (@pid,@tk,@ord,@name,@action,@authority,@roles,@emps,@anchor)", rows);
        return new { message = "Saved." };
    });

    [HttpPut("policies/{id:int}/rotation")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> SaveRotation(int id, [FromBody] List<RotationInput> body) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        var desig = (await c.QueryAsync<int>("SELECT id FROM designations")).ToHashSet();
        var seen = new HashSet<string>();
        var rows = new List<object>();
        foreach (var r in body ?? new())
        {
            var key = Key(r.RuleKey, "Rotation rule");
            if (!seen.Add(key)) throw new TrException($"Rotation key {key} is repeated.");
            var name = (r.Name ?? "").Trim();
            if (name.Length is 0 or > 120) throw new TrException("Every rotation rule needs a name.");
            if (r.MaxYears <= 0 || r.MaxYears > 50 || r.WarnMonths < 0 || r.WarnMonths > 60) throw new TrException($"Check the years and months of \"{name}\".");
            var ids = (r.DesignationIds ?? new()).Distinct().ToList();
            if (ids.Any(x => !desig.Contains(x))) throw new TrException($"\"{name}\": unknown designation.");
            rows.Add(new { pid = id, key, name, ids = ids.Count == 0 ? null : string.Join(",", ids), years = r.MaxYears, months = r.WarnMonths, en = r.IsEnabled ? 1 : 0 });
        }
        await ReplaceAsync(c, "tr_rotation", id, "INSERT INTO tr_rotation (policy_id,rule_key,name,designation_ids,max_years,warn_months,is_enabled) VALUES (@pid,@key,@name,@ids,@years,@months,@en)", rows);
        return new { message = "Saved." };
    });

    [HttpPut("policies/{id:int}/fieldmap")]
    [RequireRole(AdminRole)]
    public Task<IActionResult> SaveFieldMap(int id, [FromBody] List<FieldMapInput> body) => Run(async c =>
    {
        await AssertDraftAsync(c, id);
        var seen = new HashSet<string>();
        var rows = new List<object>();
        foreach (var m in body ?? new())
        {
            var key = (m.InputKey ?? "").ToUpperInvariant();
            if (!InputKeys.Contains(key) || !seen.Add(key)) throw new TrException("Invalid or repeated input.");
            if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM employee_field_definitions d JOIN employee_sections s ON s.id=d.section_id WHERE s.is_finalized=1 AND s.table_name=@t AND d.column_name=@col", new { t = m.TableName, col = m.ColumnName }) == 0)
                throw new TrException($"The field chosen for {key} does not exist in a finalized section.");
            rows.Add(new { id, k = key, t = m.TableName, col = m.ColumnName });
        }
        await ReplaceAsync(c, "tr_field_map", id, "INSERT INTO tr_field_map (policy_id,input_key,table_name,column_name) VALUES (@id,@k,@t,@col)", rows);
        return new { message = "Saved." };
    });

    // a starting point modelled on common patterns; every rule starts as a warning until the corporation confirms its real rules
    private static async Task SeedStarterAsync(IDbConnection c, IDbTransaction tx, int pid)
    {
        foreach (var (key, name, mode, by, consent, cat, vac, dept, desig, sen, ord) in new (string, string, string, string, int, int, string, string, string, string, int)[]
        {
            ("ADMIN", "Administrative transfer", "INDIVIDUAL", "MANAGEMENT", 0, 0, "NONE", "OPTIONAL", "OPTIONAL", "RETAIN", 10),
            ("REQUEST", "Transfer on request", "INDIVIDUAL", "EMPLOYEE", 0, 1, "WARN", "NONE", "NONE", "BOTTOM", 20),
            ("MUTUAL", "Mutual transfer", "MUTUAL", "EMPLOYEE", 1, 0, "NONE", "NONE", "NONE", "BOTTOM", 30),
            ("GENERAL", "General transfer (window)", "WINDOW", "EMPLOYEE", 0, 1, "WARN", "OPTIONAL", "OPTIONAL", "BOTTOM", 40),
            ("ROTATIONAL", "Rotational transfer", "INDIVIDUAL", "MANAGEMENT", 0, 0, "NONE", "NONE", "NONE", "RETAIN", 50),
            ("DISCIPLINARY", "Disciplinary / public-interest transfer", "INDIVIDUAL", "MANAGEMENT", 0, 0, "NONE", "NONE", "NONE", "RETAIN", 60)
        })
            await c.ExecuteAsync("INSERT INTO tr_type (policy_id,type_key,name,proc_mode,initiated_by,needs_consent,needs_category,vacancy_check,dept_change,desig_change,seniority_effect,apply_on,is_enabled,sort_order) VALUES (@pid,@key,@name,@mode,@by,@consent,@cat,@vac,@dept,@desig,@sen,'JOINING',1,@ord)",
                new { pid, key, name, mode, by, consent, cat, vac, dept, desig, sen, ord }, tx);

        foreach (var (key, name, rank, doc, waive) in new (string, string, int, string?, int)[]
        {
            ("MEDICAL", "Serious illness (self, spouse or children)", 1, "Medical certificate", 1), ("SPOUSE", "Spouse in service", 2, "Spouse employment proof", 0),
            ("WIDOW", "Widow", 3, null, 1), ("DISABILITY", "Disability", 4, "Disability certificate", 1), ("GENERAL", "General", 9, null, 0)
        })
            await c.ExecuteAsync("INSERT INTO tr_category (policy_id,category_key,name,priority_rank,doc_label,waives_vacancy,is_enabled) VALUES (@pid,@key,@name,@rank,@doc,@waive,1)", new { pid, key, name, rank, doc, waive }, tx);

        foreach (var (key, name, kind, json, applies, exempt, ord) in new (string, string, string, string, string, string?, int)[]
        {
            ("MIN_STAY", "Minimum stay at the current place", "MIN_TENURE", "{\"default\":3,\"byGroup\":{}}", "REQUEST,GENERAL,MUTUAL", null, 10),
            ("MIN_SERVICE", "Minimum service before a request", "MIN_SERVICE", "{\"years\":5}", "REQUEST,GENERAL,MUTUAL", "WIDOW", 20),
            ("PROBATION", "Not within the probation period", "PROBATION", "{\"months\":12}", "REQUEST,GENERAL,MUTUAL", null, 30),
            ("RETIREMENT", "Not close to retirement", "NEAR_RETIREMENT", "{\"retirementAge\":60,\"months\":12}", "REQUEST,GENERAL,MUTUAL,ADMIN", null, 40),
            ("FREQUENCY", "Limit on repeated transfers", "FREQUENCY", "{\"maxTransfers\":1,\"years\":3}", "REQUEST,GENERAL,MUTUAL", null, 50),
            ("SAME_GRADE", "Mutual transfer: same grade and designation", "MUTUAL_MATCH", "{\"sameGroup\":1,\"sameDesignation\":1}", "MUTUAL", null, 60)
        })
            await c.ExecuteAsync("INSERT INTO tr_rule (policy_id,rule_key,name,rule_kind,params_json,applies_types,exempt_categories,strength,is_enabled,sort_order) VALUES (@pid,@key,@name,@kind,@json,@applies,@exempt,'WARN',1,@ord)", new { pid, key, name, kind, json, applies, exempt, ord }, tx);

        foreach (var (tk, ord, name, action, anchor) in new[]
        {
            ("REQUEST", 1, "Recommendation by the current office", "RECOMMEND", "FROM"), ("REQUEST", 2, "No-objection by the receiving office", "NOC", "TO"), ("REQUEST", 3, "Approval by the competent authority", "APPROVE", "EITHER"),
            ("MUTUAL", 1, "Recommendation by the current office", "RECOMMEND", "FROM"), ("MUTUAL", 2, "No-objection by the other office", "NOC", "TO"), ("MUTUAL", 3, "Approval by the competent authority", "APPROVE", "EITHER"),
            ("GENERAL", 1, "Verification by the current office", "RECOMMEND", "FROM"), ("GENERAL", 2, "Verification of eligibility", "APPROVE", "EITHER"),
            ("ADMIN", 1, "Approval by the competent authority", "APPROVE", "EITHER"), ("ROTATIONAL", 1, "Approval by the competent authority", "APPROVE", "EITHER"), ("DISCIPLINARY", 1, "Approval by the competent authority", "APPROVE", "EITHER")
        })
            await c.ExecuteAsync("INSERT INTO tr_step (policy_id,type_key,step_order,name,action,authority,role_codes,anchor) VALUES (@pid,@tk,@ord,@name,@action,'ROLE','HR',@anchor)", new { pid, tk, ord, name, action, anchor }, tx);

        await c.ExecuteAsync("INSERT INTO tr_rotation (policy_id,rule_key,name,max_years,warn_months,is_enabled) VALUES (@pid,'MAX_STAY','Maximum stay at one place',5,6,1)", new { pid }, tx);
    }

    // =========================================================
    // WINDOWS (needs Edit on the Transfer Windows menu page)
    // =========================================================

    private async Task<Actor> WindowAdminAsync(IDbConnection c)
    {
        var me = await ActorAsync(c);
        await RequireMenuAsync(c, me, WindowsRoute, "can_edit", "manage transfer windows");
        return me;
    }

    private static async Task<WindowRow> LoadWindowAsync(IDbConnection c, int id) =>
        await c.QueryFirstOrDefaultAsync<WindowRow>($"SELECT {WindowCols} FROM tr_window WHERE id=@id", new { id }) ?? throw new TrException("Window not found.", 404);

    private static object WindowDto(WindowRow w, int requests) => new
    {
        w.Id, w.Name, w.TypeKey, w.ScopeNodeId, w.Status, ApplyFrom = Dt(w.ApplyFrom), ApplyTo = Dt(w.ApplyTo), ObjectionFrom = Dt(w.ObjectionFrom), ObjectionTo = Dt(w.ObjectionTo),
        OrdersFrom = Dt(w.OrdersFrom), EffectiveOn = w.EffectiveOn?.ToString("yyyy-MM-dd"), w.TotalSeats, w.GeneralCategory, OverflowToGeneral = w.OverflowToGeneral == 1, Requests = requests
    };

    [HttpGet("windows/options")]
    public Task<IActionResult> WindowOptions() => Run(async c =>
    {
        await WindowAdminAsync(c);
        var policy = await c.QueryFirstOrDefaultAsync<PolicyRow>($"SELECT {PolicyCols} FROM tr_policy WHERE status='ACTIVE' ORDER BY id DESC LIMIT 1");
        if (policy == null) return new { policy = (object?)null };
        return new
        {
            policy = new { policy.Id, policy.Name, policy.Version },
            types = (await c.QueryAsync("SELECT type_key AS typeKey, name AS name FROM tr_type WHERE policy_id=@id AND proc_mode='WINDOW' AND is_enabled=1", new { id = policy.Id })).ToDictList(),
            categories = (await c.QueryAsync("SELECT category_key AS categoryKey, name AS name FROM tr_category WHERE policy_id=@id AND is_enabled=1 ORDER BY priority_rank", new { id = policy.Id })).ToDictList()
        };
    });

    [HttpGet("windows")]
    public Task<IActionResult> Windows() => Run(async c =>
    {
        await WindowAdminAsync(c);
        var list = (await c.QueryAsync<WindowRow>($"SELECT {WindowCols} FROM tr_window ORDER BY id DESC")).ToList();
        var counts = (await c.QueryAsync("SELECT window_id AS w, COUNT(*) AS n FROM tr_request WHERE window_id IS NOT NULL GROUP BY window_id")).ToDictionary(x => Convert.ToInt32(((IDictionary<string, object>)x)["w"]), x => Convert.ToInt32(((IDictionary<string, object>)x)["n"]));
        return list.Select(w => WindowDto(w, counts.GetValueOrDefault(w.Id))).ToList();
    });

    private static async Task<WindowRow> ParseWindowAsync(IDbConnection c, WindowInput b, WindowRow? existing)
    {
        var name = (b.Name ?? "").Trim();
        if (name.Length is 0 or > 120) throw new TrException("Enter the window name.");
        var typeKey = (b.TypeKey ?? "").Trim().ToUpperInvariant();
        var policy = await c.QueryFirstOrDefaultAsync<PolicyRow>($"SELECT {PolicyCols} FROM tr_policy WHERE status='ACTIVE' ORDER BY id DESC LIMIT 1") ?? throw new TrException("No transfer policy is active.", 409);
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tr_type WHERE policy_id=@p AND type_key=@k AND proc_mode='WINDOW' AND is_enabled=1", new { p = policy.Id, k = typeKey }) == 0) throw new TrException("Choose a window-type transfer.");

        var af = OptDateTime(b.ApplyFrom, "Application start") ?? throw new TrException("Enter when applications open.");
        var at = OptDateTime(b.ApplyTo, "Application end") ?? throw new TrException("Enter when applications close.");
        var of = OptDateTime(b.ObjectionFrom, "Objection start");
        var ot = OptDateTime(b.ObjectionTo, "Objection end");
        var ord = OptDateTime(b.OrdersFrom, "Orders from");
        if (at <= af) throw new TrException("Applications must close after they open.");
        if ((of == null) != (ot == null)) throw new TrException("Enter both the start and the end of the objection period, or neither.");
        if (of != null && (of < at || ot <= of)) throw new TrException("The objection period must start after applications close and end after it starts.");
        if (ord != null && ord < (ot ?? at)) throw new TrException("Orders can only start after the objection period (or applications) end.");
        if (b.ScopeNodeId != null && await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM master_organization_nodes WHERE node_id=@n AND deleted_at IS NULL", new { n = b.ScopeNodeId }) == 0) throw new TrException("Choose a valid node.");
        if (b.TotalSeats is < 0) throw new TrException("Seats cannot be negative.");

        var general = string.IsNullOrWhiteSpace(b.GeneralCategory) ? null : b.GeneralCategory.Trim().ToUpperInvariant();
        var catKeys = (await c.QueryAsync<string>("SELECT category_key FROM tr_category WHERE policy_id=@p", new { p = policy.Id })).ToHashSet();
        if (general != null && !catKeys.Contains(general)) throw new TrException("Unknown general category.");
        foreach (var q in b.Quotas ?? new()) if (!catKeys.Contains((q.CategoryKey ?? "").ToUpperInvariant()) || q.MaxSeats < 0) throw new TrException("Check the category quotas.");

        return new WindowRow
        {
            Id = existing?.Id ?? 0, PolicyId = policy.Id, TypeKey = typeKey, Name = name, ScopeNodeId = b.ScopeNodeId, ApplyFrom = af, ApplyTo = at, ObjectionFrom = of, ObjectionTo = ot, OrdersFrom = ord,
            EffectiveOn = OptDate(b.EffectiveOn, "Effective date"), TotalSeats = b.TotalSeats, GeneralCategory = general, OverflowToGeneral = b.OverflowToGeneral ? 1 : 0, Status = existing?.Status ?? "DRAFT"
        };
    }

    private static async Task SaveQuotasAsync(IDbConnection c, IDbTransaction tx, int id, List<QuotaInput>? quotas)
    {
        await c.ExecuteAsync("DELETE FROM tr_window_quota WHERE window_id=@id", new { id }, tx);
        foreach (var q in quotas ?? new())
            await c.ExecuteAsync("INSERT INTO tr_window_quota (window_id, category_key, max_seats) VALUES (@id,@k,@m)", new { id, k = q.CategoryKey.ToUpperInvariant(), m = q.MaxSeats }, tx);
    }

    [HttpPost("windows")]
    public Task<IActionResult> CreateWindow([FromBody] WindowInput b) => Run(async c =>
    {
        var me = await WindowAdminAsync(c);
        var w = await ParseWindowAsync(c, b, null);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(@"INSERT INTO tr_window (policy_id,type_key,name,scope_node_id,apply_from,apply_to,objection_from,objection_to,orders_from,effective_on,total_seats,general_category,overflow_to_general,status,created_by)
            VALUES (@PolicyId,@TypeKey,@Name,@ScopeNodeId,@ApplyFrom,@ApplyTo,@ObjectionFrom,@ObjectionTo,@OrdersFrom,@EffectiveOn,@TotalSeats,@GeneralCategory,@OverflowToGeneral,'DRAFT',@by)", new { w.PolicyId, w.TypeKey, w.Name, w.ScopeNodeId, w.ApplyFrom, w.ApplyTo, w.ObjectionFrom, w.ObjectionTo, w.OrdersFrom, w.EffectiveOn, w.TotalSeats, w.GeneralCategory, w.OverflowToGeneral, by = me.Id }, tx);
        var id = await c.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction: tx);
        await SaveQuotasAsync(c, tx, id, b.Quotas);
        tx.Commit();
        return new { id };
    });

    [HttpPut("windows/{id:int}")]
    public Task<IActionResult> UpdateWindow(int id, [FromBody] WindowInput b) => Run(async c =>
    {
        await WindowAdminAsync(c);
        var cur = await LoadWindowAsync(c, id);
        if (cur.Status is not ("DRAFT" or "PUBLISHED")) throw new TrException("A window cannot be edited after ranking has started.", 409);
        var w = await ParseWindowAsync(c, b, cur);
        if (cur.Status == "PUBLISHED" && w.TypeKey != cur.TypeKey) throw new TrException("The transfer type cannot change after the window is published.", 409);

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(@"UPDATE tr_window SET type_key=@TypeKey, name=@Name, scope_node_id=@ScopeNodeId, apply_from=@ApplyFrom, apply_to=@ApplyTo, objection_from=@ObjectionFrom, objection_to=@ObjectionTo,
            orders_from=@OrdersFrom, effective_on=@EffectiveOn, total_seats=@TotalSeats, general_category=@GeneralCategory, overflow_to_general=@OverflowToGeneral WHERE id=@id",
            new { w.TypeKey, w.Name, w.ScopeNodeId, w.ApplyFrom, w.ApplyTo, w.ObjectionFrom, w.ObjectionTo, w.OrdersFrom, w.EffectiveOn, w.TotalSeats, w.GeneralCategory, w.OverflowToGeneral, id }, tx);
        await SaveQuotasAsync(c, tx, id, b.Quotas);
        tx.Commit();
        return new { message = "Saved." };
    });

    [HttpDelete("windows/{id:int}")]
    public Task<IActionResult> DeleteWindow(int id) => Run(async c =>
    {
        await WindowAdminAsync(c);
        var w = await LoadWindowAsync(c, id);
        if (w.Status != "DRAFT") throw new TrException("Only a draft window can be deleted.", 409);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("DELETE FROM tr_window_quota WHERE window_id=@id", new { id }, tx);
        await c.ExecuteAsync("DELETE FROM tr_window WHERE id=@id", new { id }, tx);
        tx.Commit();
        return new { message = "Deleted." };
    });

    [HttpGet("windows/{id:int}")]
    public Task<IActionResult> GetWindow(int id) => Run(async c =>
    {
        var me = await WindowAdminAsync(c);
        var w = await LoadWindowAsync(c, id);
        var stats = (await c.QueryAsync($"SELECT status AS status, sel_status AS sel, COUNT(*) AS n FROM tr_request r WHERE window_id=@id AND {VisibleClause(me)} GROUP BY status, sel_status", new { id })).ToDictList();
        var items = (await c.QueryAsync($@"SELECT r.id AS id, r.request_no AS requestNo, r.status AS status, r.rank_no AS rankNo, r.sel_status AS sel, r.selection_note AS note, r.category_key AS categoryKey,
                e.employee_code AS employeeCode, TRIM(CONCAT(e.first_name,' ',COALESCE(e.last_name,''))) AS employeeName, fn.node_name AS fromName, tn.node_name AS toName
            FROM tr_request r JOIN employee_master e ON e.employee_id = r.employee_id
            LEFT JOIN master_organization_nodes fn ON fn.node_id = r.from_node_id LEFT JOIN master_organization_nodes tn ON tn.node_id = r.to_node_id
            WHERE r.window_id=@id AND {VisibleClause(me)} ORDER BY COALESCE(r.rank_no, 999999), r.id LIMIT 2000", new { id })).ToDictList();
        var objections = (await c.QueryAsync($@"SELECT o.id AS id, o.request_id AS requestId, r.request_no AS requestNo, o.text AS text, o.status AS status, o.resolution AS resolution, o.created_at AS createdAt,
                TRIM(CONCAT(e.first_name,' ',COALESCE(e.last_name,''))) AS byName
            FROM tr_objection o JOIN tr_request r ON r.id = o.request_id JOIN employee_master e ON e.employee_id = o.by_user
            WHERE o.window_id=@id AND {VisibleClause(me)} ORDER BY o.id DESC", new { id })).ToDictList();
        var quotas = (await c.QueryAsync("SELECT category_key AS categoryKey, max_seats AS maxSeats FROM tr_window_quota WHERE window_id=@id", new { id })).ToDictList();

        return new { window = WindowDto(w, items.Count), quotas, stats, items, objections };
    });

    [HttpPost("windows/{id:int}/publish")]
    public Task<IActionResult> PublishWindow(int id) => Run(async c =>
    {
        await WindowAdminAsync(c);
        var w = await LoadWindowAsync(c, id);
        if (w.Status != "DRAFT") throw new TrException("Only a draft window can be published.", 409);
        if (w.ApplyTo <= DateTime.Now) throw new TrException("The application period has already ended. Edit the dates first.");
        await c.ExecuteAsync("UPDATE tr_window SET status='PUBLISHED' WHERE id=@id", new { id });
        return new { message = "Published. Applications open at the set time." };
    });

    [HttpPost("windows/{id:int}/rank")]
    public Task<IActionResult> RankWindow(int id) => Run(async c =>
    {
        await WindowAdminAsync(c);
        var w = await LoadWindowAsync(c, id);
        if (w.Status is not ("PUBLISHED" or "RANKED" or "LIST_PUBLISHED")) throw new TrException("This window cannot be ranked now.", 409);
        if (DateTime.Now <= w.ApplyTo) throw new TrException("Ranking can run only after the application period ends.", 409);

        var type = await c.QueryFirstAsync<TypeRow>($"SELECT {TypeCols} FROM tr_type WHERE policy_id=(SELECT policy_id FROM tr_window WHERE id=@id) AND type_key=@k", new { id, k = w.TypeKey });
        var cats = (await c.QueryAsync<CatRow>($"SELECT {CatCols} FROM tr_category WHERE policy_id=(SELECT policy_id FROM tr_window WHERE id=@id)", new { id })).ToDictionary(x => x.CategoryKey);
        var quota = (await c.QueryAsync("SELECT category_key AS k, max_seats AS m FROM tr_window_quota WHERE window_id=@id", new { id })).ToDictionary(x => (string)((IDictionary<string, object>)x)["k"], x => (int?)Convert.ToInt32(((IDictionary<string, object>)x)["m"]));

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("UPDATE tr_request SET rank_no=NULL, sel_status=NULL, selection_note=NULL WHERE window_id=@id AND status='VERIFIED'", new { id }, tx);
        var reqs = (await c.QueryAsync<Req>($"SELECT {ReqCols} FROM tr_request WHERE window_id=@id AND status='VERIFIED'", new { id }, tx)).ToList();

        // seniority = date of joining (earlier first; unknown last), then the order of application
        var join = new Dictionary<long, DateTime?>();
        foreach (var r in reqs) join[r.Id] = TryDate(await ReadInputAsync(c, r.PolicyId, "JOIN_DATE", r.EmployeeId), out var d) ? d : null;
        int Rank(Req r) => r.CategoryKey != null && cats.TryGetValue(r.CategoryKey, out var ct) ? ct.PriorityRank : 999;
        var ordered = reqs.OrderBy(Rank).ThenBy(r => join[r.Id] ?? DateTime.MaxValue).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();

        var seats = new Dictionary<string, int?>();
        foreach (var k in cats.Keys) seats[k] = quota.TryGetValue(k, out var q) ? q : null;
        int? totalLeft = w.TotalSeats;
        var unused = 0;
        var taken = new Dictionary<(int, int), int>();
        int n = 0, selected = 0, waitlisted = 0;

        foreach (var grp in ordered.GroupBy(r => r.CategoryKey ?? ""))
        {
            var key = grp.Key;
            if (w.OverflowToGeneral == 1 && key == w.GeneralCategory && seats.TryGetValue(key, out var gs) && gs != null) seats[key] = gs + unused;

            foreach (var r in grp)
            {
                string sel = "SELECTED";
                string? note = null;
                var hasQuota = key != "" && seats.TryGetValue(key, out var sq) && sq != null;
                if (hasQuota && seats[key]!.Value <= 0) { sel = "WAITLIST"; note = "Category quota is full."; }
                else if (totalLeft is { } tl && tl <= 0) { sel = "WAITLIST"; note = "All seats are taken."; }
                else if (type.VacancyCheck != "NONE" && !(r.CategoryKey != null && cats.TryGetValue(r.CategoryKey, out var cc) && cc.WaivesVacancy == 1))
                {
                    var desig = r.ToDesignationId ?? r.FromDesignationId;
                    if (desig != null)
                    {
                        var (sanc, work, inc) = await VacancyAsync(c, r.ToNodeId, desig.Value, null);
                        if (sanc != null && sanc.Value - work - inc - taken.GetValueOrDefault((r.ToNodeId, desig.Value)) <= 0) { sel = "WAITLIST"; note = "No vacancy at the destination."; }
                    }
                }

                if (sel == "SELECTED")
                {
                    selected++;
                    if (hasQuota) seats[key] = seats[key]!.Value - 1;
                    if (totalLeft != null) totalLeft--;
                    var d2 = r.ToDesignationId ?? r.FromDesignationId;
                    if (d2 != null) taken[(r.ToNodeId, d2.Value)] = taken.GetValueOrDefault((r.ToNodeId, d2.Value)) + 1;
                }
                else waitlisted++;
                await c.ExecuteAsync("UPDATE tr_request SET rank_no=@n, sel_status=@sel, selection_note=@note WHERE id=@id", new { n = ++n, sel, note, id = r.Id }, tx);
            }
            if (key != w.GeneralCategory && key != "" && seats.TryGetValue(key, out var left) && left != null) unused += Math.Max(left.Value, 0);
        }

        await c.ExecuteAsync("UPDATE tr_window SET status=IF(status='LIST_PUBLISHED','LIST_PUBLISHED','RANKED') WHERE id=@id", new { id }, tx);
        tx.Commit();
        return new { message = $"Ranking done: {selected} selected, {waitlisted} on the waiting list." };
    });

    [HttpPost("windows/{id:int}/publish-list")]
    public Task<IActionResult> PublishList(int id) => Run(async c =>
    {
        var me = await WindowAdminAsync(c);
        var w = await LoadWindowAsync(c, id);
        if (w.Status is not ("RANKED" or "LIST_PUBLISHED")) throw new TrException("Run the ranking first.", 409);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("UPDATE tr_window SET status='LIST_PUBLISHED' WHERE id=@id", new { id }, tx);
        var people = (await c.QueryAsync<long>("SELECT DISTINCT employee_id FROM tr_request WHERE window_id=@id AND sel_status IS NOT NULL", new { id }, tx)).ToList();
        await NotifyAsync(c, tx, people, $"List published: {w.Name}", w.ObjectionFrom != null ? $"Objections can be raised until {w.ObjectionTo:dd MMM yyyy HH:mm}." : "Check your result.", "/transfers");
        tx.Commit();
        return new { message = "The list is published." };
    });

    [HttpPost("windows/objections/{oid:long}/resolve")]
    public Task<IActionResult> ResolveObjection(long oid, [FromBody] ResolveBody b) => Run(async c =>
    {
        var me = await WindowAdminAsync(c);
        var text = (b.Resolution ?? "").Trim();
        if (text.Length < 3) throw new TrException("Write the decision on this objection.");
        var o = await c.QueryFirstOrDefaultAsync("SELECT by_user, window_id FROM tr_objection WHERE id=@oid AND status='OPEN'", new { oid }) as IDictionary<string, object>
            ?? throw new TrException("Objection not found or already resolved.", 404);
        await c.ExecuteAsync("UPDATE tr_objection SET status='RESOLVED', resolution=@text, resolved_by=@by, resolved_at=NOW() WHERE id=@oid", new { oid, text, by = me.Id });
        await NotifyAsync(c, null, new[] { Convert.ToInt64(o["by_user"]) }, "Your objection was decided", text, "/transfers");
        return new { message = "Resolved. Run the ranking again if the list must change." };
    });

    [HttpPost("windows/{id:int}/finalize")]
    public Task<IActionResult> FinalizeWindow(int id) => Run(async c =>
    {
        var me = await WindowAdminAsync(c);
        var w = await LoadWindowAsync(c, id);
        if (w.Status != "LIST_PUBLISHED") throw new TrException("Publish the list first.", 409);
        if (w.ObjectionTo != null && DateTime.Now <= w.ObjectionTo) throw new TrException($"The objection period runs until {w.ObjectionTo:dd MMM yyyy HH:mm}.", 409);
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tr_objection WHERE window_id=@id AND status='OPEN'", new { id }) > 0) throw new TrException("Resolve every open objection first.", 409);

        using var tx = c.BeginTransaction();
        var selected = (await c.QueryAsync<Req>($"SELECT {ReqCols} FROM tr_request WHERE window_id=@id AND status='VERIFIED' AND sel_status='SELECTED'", new { id }, tx)).ToList();
        var others = (await c.QueryAsync<Req>($"SELECT {ReqCols} FROM tr_request WHERE window_id=@id AND status IN ('VERIFIED','PENDING','RETURNED') AND (sel_status IS NULL OR sel_status<>'SELECTED')", new { id }, tx)).ToList();

        foreach (var r in selected)
        {
            await c.ExecuteAsync("UPDATE tr_request SET status='APPROVED' WHERE id=@id", new { id = r.Id }, tx);
            await LogAsync(c, tx, r.Id, "WINDOW_SELECTED", me.Id, w.Name);
            await NotifyAsync(c, tx, new[] { (long)r.EmployeeId, r.CreatedBy }, "Selected in the transfer list", $"{r.RequestNo}: the transfer order will follow.", $"/transfers/{r.Id}");
        }
        foreach (var r in others)
        {
            await c.ExecuteAsync("UPDATE tr_request SET status='REJECTED' WHERE id=@id", new { id = r.Id }, tx);
            await LogAsync(c, tx, r.Id, "WINDOW_NOT_SELECTED", me.Id, r.SelectionNote ?? "Not selected in this window.");
            await NotifyAsync(c, tx, new[] { (long)r.EmployeeId, r.CreatedBy }, "Not selected in the transfer list", $"{r.RequestNo}: {r.SelectionNote ?? "not selected"}.", $"/transfers/{r.Id}");
        }
        await c.ExecuteAsync("UPDATE tr_window SET status='CLOSED' WHERE id=@id", new { id }, tx);
        tx.Commit();
        return new { message = $"Finalized: {selected.Count} approved for orders, {others.Count} closed." };
    });

    // =========================================================
    // VACANCY / SANCTIONED STRENGTH
    // =========================================================

    // GET vacancy?nodeId= - sanctioned, working, incoming and vacancy per designation at one node (node must be in the caller's scope)
    [HttpGet("vacancy")]
    public Task<IActionResult> VacancyList([FromQuery] int nodeId) => Run(async c =>
    {
        var me = await ActorAsync(c);
        if (!me.Scope.Contains(nodeId)) throw new TrException("You do not have access to this node.", 403);

        var desigs = (await c.QueryAsync<int>(@"SELECT designation_id FROM tr_sanctioned WHERE node_id=@n
            UNION SELECT e.designation_id FROM employee_master e JOIN master_employee_organization_mapping m ON m.employee_id=e.employee_id AND m.is_current=1 WHERE m.node_id=@n AND e.designation_id IS NOT NULL
            UNION SELECT COALESCE(r.to_designation_id, e.designation_id) FROM tr_request r JOIN employee_master e ON e.employee_id=r.employee_id WHERE r.to_node_id=@n AND r.status IN (" + OpenSql + ")", new { n = nodeId })).Where(x => x > 0).Distinct().ToList();

        var names = (await c.QueryAsync("SELECT id, name FROM designations")).ToDictionary(x => Convert.ToInt32(((IDictionary<string, object>)x)["id"]), x => Convert.ToString(((IDictionary<string, object>)x)["name"]));
        var rows = new List<object>();
        foreach (var d in desigs)
        {
            var (s, w, i) = await VacancyAsync(c, nodeId, d, null);
            rows.Add(new { designationId = d, designation = names.GetValueOrDefault(d), sanctioned = s, working = w, incoming = i, vacancy = s == null ? (int?)null : s.Value - w - i });
        }
        return new { nodeId, rows = rows.OrderBy(x => ((dynamic)x).designation).ToList(), designations = (await c.QueryAsync("SELECT id AS id, name AS name FROM designations WHERE active=1 ORDER BY sequence, name")).ToDictList() };
    });

    [HttpPut("vacancy")]
    public Task<IActionResult> SaveVacancy([FromBody] VacancyInput b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        await RequireMenuAsync(c, me, VacancyRoute, "can_edit", "change sanctioned strength");
        if (!me.Scope.Contains(b.NodeId)) throw new TrException("You do not have access to this node.", 403);
        if (b.Sanctioned < 0 || b.Sanctioned > 100000) throw new TrException("Enter a valid number.");
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM designations WHERE id=@d", new { d = b.DesignationId }) == 0) throw new TrException("Choose a valid designation.");
        await c.ExecuteAsync(@"INSERT INTO tr_sanctioned (node_id, designation_id, sanctioned, updated_by) VALUES (@n,@d,@s,@by)
            ON DUPLICATE KEY UPDATE sanctioned=@s, updated_by=@by", new { n = b.NodeId, d = b.DesignationId, s = b.Sanctioned, by = me.Id });
        return new { message = "Saved." };
    });

    [HttpDelete("vacancy")]
    public Task<IActionResult> DeleteVacancy([FromQuery] int nodeId, [FromQuery] int designationId) => Run(async c =>
    {
        var me = await ActorAsync(c);
        await RequireMenuAsync(c, me, VacancyRoute, "can_edit", "change sanctioned strength");
        if (!me.Scope.Contains(nodeId)) throw new TrException("You do not have access to this node.", 403);
        await c.ExecuteAsync("DELETE FROM tr_sanctioned WHERE node_id=@nodeId AND designation_id=@designationId", new { nodeId, designationId });
        return new { message = "Removed." };
    });
}