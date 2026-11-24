using System.Data;
using Dapper;
using HrmsBackend.Common;
using HrmsBackend.Data;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static HrmsBackend.Services.TransferCore;

namespace HrmsBackend.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize]
public class TransferController : TransferBase
{
    private const string NewPageRoute = "/transfers/new";           // Add here = may raise requests for other employees
    private const string RotationRoute = "/transfers/rotation";     // View = see the due list, Add = raise transfers from it

    private readonly FileStorageClient _files;

    public TransferController(DbConnectionFactory db, FileStorageClient files) : base(db) { _files = files; }

    public record DecideBody(string Decision, string? Remarks, string? RelaxationReason);
    public record RemarksBody(string? Remarks);
    public record ConsentBody(bool Accept, string? Remarks);
    public record ResubmitBody(string? Reason, List<DocInput>? Documents);
    public record OrderBody(string OrderNo, string OrderDate, string? FileId, string? FileName);
    public record BulkOrderBody(List<long>? Ids, string OrderNo, string OrderDate, string? FileId, string? FileName);
    public record DateBody(string Date);
    public record ObjectionBody(string Text);
    public record ReadBody(List<long>? Ids);
    public record RotationItem(long EmployeeId, int ToNodeId);
    public record RotationBody(string TypeKey, string? Reason, List<RotationItem>? Items, int? ToDepartmentId, int? ToDesignationId);

    // =========================================================
    // HELPERS
    // =========================================================

    private static string Like(string s) => "%" + s.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private static Task<TypeRow?> TypeOfAsync(IDbConnection c, Req r) =>
        c.QueryFirstOrDefaultAsync<TypeRow>($"SELECT {TypeCols} FROM tr_type WHERE policy_id=@p AND type_key=@k", new { p = r.PolicyId, k = r.TypeKey });

    private static Task<WindowRow?> WindowOfAsync(IDbConnection c, int? id) =>
        id == null ? Task.FromResult<WindowRow?>(null) : c.QueryFirstOrDefaultAsync<WindowRow>($"SELECT {WindowCols} FROM tr_window WHERE id=@id", new { id });

    private static async Task<string?> LookupNameAsync(IDbConnection c, string table, string col, int? id) =>
        id == null ? null : await c.ExecuteScalarAsync<string?>($"SELECT {col} FROM {table} WHERE id=@id", new { id });

    // Which transfer types this login may raise.
    // - employee (no Add on /transfers/new): types started by EMPLOYEE or BOTH
    // - management (Add on /transfers/new):   types started by MANAGEMENT or BOTH, limited to the roles ticked on the type
    //   (no roles ticked = any management login; SUPERADMIN skips the role list)
    private static (string[] Inits, string RoleSql) RaiseFilter(Actor me, bool canOthers) => (
        canOthers ? new[] { "MANAGEMENT", "BOTH" } : new[] { "EMPLOYEE", "BOTH" },
        canOthers && !me.Super ? " AND (raise_roles IS NULL OR raise_roles='' OR FIND_IN_SET(@role, raise_roles) > 0)" : "");

    private static async Task AssertMayRaiseAsync(IDbConnection c, Actor me, string? typeKey, long employeeId, bool canOthers)
    {
        var policyId = await c.ExecuteScalarAsync<int?>("SELECT id FROM tr_policy WHERE status='ACTIVE' ORDER BY id DESC LIMIT 1");
        var key = (typeKey ?? "").Trim().ToUpperInvariant();
        if (policyId == null || await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tr_type WHERE policy_id=@p AND type_key=@k", new { p = policyId, k = key }) == 0)
            return; // PrepareAsync reports a missing policy or an unknown type

        if (!canOthers && employeeId != me.Id) throw new TrException("You can raise a transfer only for yourself.", 403);

        var (inits, roleSql) = RaiseFilter(me, canOthers);
        var ok = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM tr_type WHERE policy_id=@p AND type_key=@k AND initiated_by IN @inits{roleSql}",
            new { p = policyId, k = key, inits, role = me.Role });
        if (ok == 0) throw new TrException("You are not allowed to raise this type of transfer.", 403);
    }

    // the people a request concerns: the employee(s) and whoever raised it
    private static IEnumerable<long> Parties(Req r) => new long[] { r.EmployeeId, r.Employee2Id ?? 0, r.CreatedBy }.Where(x => x > 0).Distinct();

    // employees whose consent is needed: the exchange partner always, the first employee when somebody else raised the request
    private static List<long> RequiredConsenters(Req r, TypeRow? type)
    {
        var list = new List<long>();
        if (r.Employee2Id != null) { list.Add(r.Employee2Id.Value); if (r.CreatedBy != r.EmployeeId) list.Add(r.EmployeeId); }
        else if (type?.NeedsConsent == 1 && r.CreatedBy != r.EmployeeId) list.Add(r.EmployeeId);
        return list.Distinct().ToList();
    }

    private static async Task<List<long>> GivenConsentsAsync(IDbConnection c, long id, IDbTransaction? tx = null) =>
        (await c.QueryAsync<long>("SELECT DISTINCT by_user FROM tr_request_log WHERE request_id=@id AND action='CONSENT_GIVEN'", new { id }, tx)).ToList();

    private static List<DocInput> CleanDocs(List<DocInput>? docs)
    {
        var list = (docs ?? new()).Where(d => d != null).ToList();
        foreach (var d in list)
            if (!FileIdRx.IsMatch(d.FileId ?? "") || string.IsNullOrWhiteSpace(d.FileName) || string.IsNullOrWhiteSpace(d.Label) || d.Label.Length > 100 || d.FileName.Length > 255)
                throw new TrException("A document was not uploaded properly. Please attach it again.");
        return list;
    }

    private static (string Reason, DateTime? Requested, List<DocInput> Docs) ValidateSubmit(Prep prep, SubmitBody b)
    {
        var reason = (b.Reason ?? "").Trim();
        if (reason.Length < 5 || reason.Length > 4000) throw new TrException("Enter the reason for the transfer (at least 5 characters).");
        var requested = OptDate(b.RequestedDate, "The preferred date");
        var docs = CleanDocs(b.Documents);
        if (prep.Category?.DocLabel is { Length: > 0 } need && !docs.Any(d => string.Equals(d.Label, need, StringComparison.OrdinalIgnoreCase)))
            throw new TrException($"Attach the required document: {need}.");
        if (prep.Blocked) throw new TrException("This transfer cannot be requested. See the rules that failed.", 422, prep.Checks);
        if (prep.Steps.Count == 0) throw new TrException("No approval workflow is configured for this transfer type.", 409);
        return (reason, requested, docs);
    }

    // creates the request with its steps, rule results, documents and first notifications
    private async Task<long> InsertRequestAsync(IDbConnection c, Actor me, Prep prep, SubmitBody b, string reason, DateTime? requested, List<DocInput> docs, string logAction)
    {
        var temp = new Req { EmployeeId = (int)b.EmployeeId, Employee2Id = prep.PartnerId == null ? null : (int?)prep.PartnerId.Value, CreatedBy = me.Id };
        var consenters = RequiredConsenters(temp, prep.Type);
        var consent = consenters.Count > 0 ? "PENDING" : "NA";

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(@"INSERT INTO tr_request (policy_id, type_key, category_key, employee_id, employee2_id, from_node_id, from_org_id, to_node_id, to_org_id,
                from_department_id, from_designation_id, to_department_id, to_designation_id, requested_date, reason, status, current_step, consent_status, needs_relaxation, window_id, created_by)
            VALUES (@pid,@tk,@cat,@emp,@emp2,@fromNode,@fromOrg,@toNode,@toOrg,@fd,@fdg,@td,@tdg,@req,@reason,'PENDING',1,@consent,@relax,@win,@by)",
            new
            {
                pid = prep.Policy.Id, tk = prep.Type.TypeKey, cat = prep.Category?.CategoryKey, emp = b.EmployeeId, emp2 = prep.PartnerId,
                fromNode = prep.From.NodeId, fromOrg = prep.From.OrganizationId, toNode = prep.ToNodeId, toOrg = prep.ToOrgId,
                fd = prep.FromDepartmentId, fdg = prep.FromDesignationId, td = prep.ToDepartmentId, tdg = prep.ToDesignationId,
                req = requested, reason, consent, relax = prep.NeedsRelaxation ? 1 : 0, win = prep.Window?.Id, by = me.Id
            }, tx);
        var id = await c.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()", transaction: tx);
        var no = $"TR-{DateTime.Now:yyyy}-{id:D6}";
        await c.ExecuteAsync("UPDATE tr_request SET request_no=@no WHERE id=@id", new { id, no }, tx);

        foreach (var s in prep.Steps)
            await c.ExecuteAsync("INSERT INTO tr_request_step (request_id, step_order, name, action, authority, role_codes, employee_ids, anchor) VALUES (@id,@o,@n,@a,@au,@r,@e,@an)",
                new { id, o = s.StepOrder, n = s.Name, a = s.Action, au = s.Authority, r = s.RoleCodes, e = s.EmployeeIds, an = s.Anchor }, tx);
        foreach (var ch in prep.Checks)
            await c.ExecuteAsync("INSERT INTO tr_request_check (request_id, subject, rule_key, name, strength, state, message) VALUES (@id,@sub,@k,@n,@s,@st,@m)",
                new { id, sub = ch.Subject, k = ch.RuleKey, n = ch.Name, s = ch.Strength, st = ch.State, m = ch.Message }, tx);
        foreach (var d in docs)
            await c.ExecuteAsync("INSERT INTO tr_request_doc (request_id, label, file_id, file_name, uploaded_by) VALUES (@id,@l,@f,@n,@by)", new { id, l = d.Label.Trim(), f = d.FileId, n = d.FileName.Trim(), by = me.Id }, tx);

        await LogAsync(c, tx, id, logAction, me.Id, null);

        var rq = new Req { Id = id, RequestNo = no, FromNodeId = prep.From.NodeId, ToNodeId = prep.ToNodeId };
        if (consenters.Count > 0)
            await NotifyAsync(c, tx, consenters, "Your consent is needed for a transfer", $"{no}: please open the request and give or decline your consent.", $"/transfers/{id}");
        else
            await NotifyStepAsync(c, tx, rq, prep.Steps.OrderBy(s => s.StepOrder).First(), "Transfer request waiting for you");

        tx.Commit();
        return id;
    }

    // =========================================================
    // FORM, CHECK, SUBMIT
    // =========================================================

    // GET form - everything the New Transfer page needs
    [HttpGet("form")]
    public Task<IActionResult> Form() => Run(async c =>
    {
        var me = await ActorAsync(c);
        var policy = await c.QueryFirstOrDefaultAsync<PolicyRow>($"SELECT {PolicyCols} FROM tr_policy WHERE status='ACTIVE' ORDER BY id DESC LIMIT 1");
        if (policy == null) return new { policy = (object?)null };

        var map = await CurrentMappingAsync(c, me.Id);
        var dest = await DataScope.OrgLookupsAsync(c, new DataScopeInfo { Unrestricted = true });
        var canOthers = await MenuPermAsync(c, me, NewPageRoute, "can_add");
        var (inits, roleSql) = RaiseFilter(me, canOthers);
        var now = DateTime.Now;
        var windows = (await c.QueryAsync<WindowRow>($"SELECT {WindowCols} FROM tr_window WHERE policy_id=@id AND status='PUBLISHED' AND apply_to >= NOW() ORDER BY apply_from", new { id = policy.Id }))
            .Select(w => new { w.Id, w.Name, w.TypeKey, w.ScopeNodeId, ApplyFrom = Dt(w.ApplyFrom), ApplyTo = Dt(w.ApplyTo), OpenNow = now >= w.ApplyFrom && now <= w.ApplyTo }).ToList();

        return new
        {
            policy = new { policy.Id, policy.Name, policy.Version },
            types = (await c.QueryAsync<TypeRow>($"SELECT {TypeCols} FROM tr_type WHERE policy_id=@id AND is_enabled=1 AND initiated_by IN @inits{roleSql} ORDER BY sort_order, id", new { id = policy.Id, inits, role = me.Role }))
                .Select(t => new { t.TypeKey, t.Name, t.ProcMode, t.InitiatedBy, NeedsConsent = t.NeedsConsent == 1, NeedsCategory = t.NeedsCategory == 1, t.VacancyCheck, t.DeptChange, t.DesigChange }).ToList(),
            categories = (await c.QueryAsync<CatRow>($"SELECT {CatCols} FROM tr_category WHERE policy_id=@id AND is_enabled=1 ORDER BY priority_rank, id", new { id = policy.Id }))
                .Select(t => new { t.CategoryKey, t.Name, t.DocLabel }).ToList(),
            windows,
            departments = (await c.QueryAsync("SELECT id AS id, dep_name AS name FROM departments WHERE active=1 ORDER BY sequence, dep_name")).ToDictList(),
            designations = (await c.QueryAsync("SELECT id AS id, name AS name FROM designations WHERE active=1 ORDER BY sequence, name")).ToDictList(),
            canRaiseForOthers = canOthers,
            me = new { employeeId = me.Id, person = await PersonAsync(c, me.Id), path = map == null ? null : await NodePathAsync(c, map.NodeId) },
            destinations = new { organizations = dest.Organizations, nodeTypes = dest.NodeTypes, nodes = dest.Nodes }
        };
    });

    // GET people?search= - find an exchange partner (code, name, designation, place only)
    [HttpGet("people")]
    public Task<IActionResult> People([FromQuery] string? search) => Run(async c =>
    {
        var s = (search ?? "").Trim();
        if (s.Length < 2) return new List<object>();
        var like = Like(s);
        return (await c.QueryAsync(@"SELECT e.employee_id AS id, e.employee_code AS code, TRIM(CONCAT(e.first_name,' ',COALESCE(e.last_name,''))) AS name, d.name AS designation, n.node_name AS place
            FROM employee_master e
            JOIN master_employee_organization_mapping m ON m.employee_id = e.employee_id AND m.is_current = 1
            JOIN master_organization_nodes n ON n.node_id = m.node_id
            LEFT JOIN designations d ON d.id = e.designation_id
            WHERE e.status='ACTIVE' AND (e.employee_code LIKE @like OR CONCAT(e.first_name,' ',COALESCE(e.last_name,'')) LIKE @like)
            ORDER BY e.employee_code LIMIT 8", new { like })).ToDictList();
    });

    // POST check - the rule results for a request that has not been submitted yet
    [HttpPost("check")]
    public Task<IActionResult> Check([FromBody] SubmitBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var canOthers = await MenuPermAsync(c, me, NewPageRoute, "can_add");
        await AssertMayRaiseAsync(c, me, b.TypeKey, b.EmployeeId, canOthers);
        var prep = await PrepareAsync(c, me, b, canOthers);
        var temp = new Req { EmployeeId = (int)b.EmployeeId, Employee2Id = prep.PartnerId == null ? null : (int?)prep.PartnerId.Value, CreatedBy = me.Id };

        object? partner = null;
        if (prep.PartnerId != null)
        {
            var pm = await CurrentMappingAsync(c, prep.PartnerId.Value);
            partner = new { person = await PersonAsync(c, prep.PartnerId.Value), path = pm == null ? null : await NodePathAsync(c, pm.NodeId) };
        }

        return new
        {
            typeName = prep.Type.Name,
            categoryName = prep.Category?.Name,
            docLabel = prep.Category?.DocLabel,
            seniorityEffect = prep.Type.SeniorityEffect,
            consentRequired = RequiredConsenters(temp, prep.Type).Count > 0,
            checks = prep.Checks,
            blocked = prep.Blocked,
            needsRelaxation = prep.NeedsRelaxation,
            steps = prep.Steps.Select(s => new { s.Name, s.Action, s.Authority, RoleCodes = Csv(s.RoleCodes), s.Anchor }).ToList(),
            fromPath = await NodePathAsync(c, prep.From.NodeId),
            toPath = await NodePathAsync(c, prep.ToNodeId),
            partner,
            window = prep.Window == null ? null : new { prep.Window.Name, ApplyTo = Dt(prep.Window.ApplyTo) }
        };
    });

    // POST - raise a request
    [HttpPost]
    public Task<IActionResult> Submit([FromBody] SubmitBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var canOthers = await MenuPermAsync(c, me, NewPageRoute, "can_add");
        await AssertMayRaiseAsync(c, me, b.TypeKey, b.EmployeeId, canOthers);
        var prep = await PrepareAsync(c, me, b, canOthers);
        var (reason, requested, docs) = ValidateSubmit(prep, b);
        return new { id = await InsertRequestAsync(c, me, prep, b, reason, requested, docs, "SUBMITTED") };
    });

    // =========================================================
    // LIST AND DETAIL
    // =========================================================

    // GET - view = inbox | mine | all (all = everything visible through the data flow)
    [HttpGet]
    public Task<IActionResult> ListRequests([FromQuery] string view = "mine", [FromQuery] string? status = null, [FromQuery] string? typeKey = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) => Run(async c =>
    {
        var me = await ActorAsync(c);
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);
        var inbox = await InboxIdsAsync(c, me);

        var where = new List<string>();
        var p = new DynamicParameters();
        p.Add("me", me.Id);

        if (view == "inbox") where.Add(inbox.Count == 0 ? "1=0" : $"r.id IN ({string.Join(",", inbox)})");
        else if (view == "all") where.Add(VisibleClause(me));
        else where.Add("(r.created_by=@me OR r.employee_id=@me OR r.employee2_id=@me)");

        if (!string.IsNullOrWhiteSpace(status)) { where.Add("r.status=@status"); p.Add("status", status.ToUpperInvariant()); }
        if (!string.IsNullOrWhiteSpace(typeKey)) { where.Add("r.type_key=@typeKey"); p.Add("typeKey", typeKey.ToUpperInvariant()); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            p.Add("like", Like(search));
            where.Add("(r.request_no LIKE @like OR e.employee_code LIKE @like OR e.first_name LIKE @like OR e.last_name LIKE @like)");
        }

        var from = @"FROM tr_request r JOIN employee_master e ON e.employee_id = r.employee_id
            LEFT JOIN employee_master e2 ON e2.employee_id = r.employee2_id
            LEFT JOIN tr_type ty ON ty.policy_id = r.policy_id AND ty.type_key = r.type_key
            LEFT JOIN master_organization_nodes fn ON fn.node_id = r.from_node_id
            LEFT JOIN master_organization_nodes tn ON tn.node_id = r.to_node_id
            WHERE " + string.Join(" AND ", where);

        p.Add("take", pageSize);
        p.Add("skip", (page - 1) * pageSize);
        var total = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) {from}", p);
        var items = (await c.QueryAsync($@"SELECT r.id AS id, r.request_no AS requestNo, r.status AS status, r.type_key AS typeKey, ty.name AS typeName, r.window_id AS windowId,
                r.employee_id AS employeeId, e.employee_code AS employeeCode, TRIM(CONCAT(e.first_name,' ',COALESCE(e.last_name,''))) AS employeeName,
                TRIM(CONCAT(COALESCE(e2.first_name,''),' ',COALESCE(e2.last_name,''))) AS partnerName, fn.node_name AS fromName, tn.node_name AS toName,
                r.requested_date AS requestedDate, r.created_at AS createdAt, r.sel_status AS selStatus, r.rank_no AS rankNo
            {from} ORDER BY r.id DESC LIMIT @take OFFSET @skip", p)).ToDictList();

        return new { total, page, pageSize, inboxCount = inbox.Count, items };
    });

    // GET {id} - one request with everything and the actions the caller may take
    [HttpGet("{id:long}")]
    public Task<IActionResult> GetRequest(long id) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        var steps = await LoadStepsAsync(c, id);
        if (!CanView(me, r, steps)) throw new TrException("You do not have access to this request.", 403);

        var type = await TypeOfAsync(c, r);
        var cat = r.CategoryKey == null ? null : await c.QueryFirstOrDefaultAsync<CatRow>($"SELECT {CatCols} FROM tr_category WHERE policy_id=@p AND category_key=@k", new { p = r.PolicyId, k = r.CategoryKey });
        var win = await WindowOfAsync(c, r.WindowId);

        var consenters = RequiredConsenters(r, type);
        var given = await GivenConsentsAsync(c, id);
        var consentList = new List<object>();
        foreach (var x in consenters) consentList.Add(new { id = x, person = await PersonAsync(c, x), given = given.Contains(x) });
        var waiting = r.ConsentStatus == "PENDING" ? consenters.Where(x => !given.Contains(x)).ToList() : new List<long>();

        var now = DateTime.Now;
        var cur = steps.FirstOrDefault(s => s.StepOrder == r.CurrentStep);
        var actions = new List<string>();
        if (cur != null && CanDecide(me, r, cur, steps)) actions.Add("decide");
        if (waiting.Contains(me.Id)) actions.Add("consent");
        var involved = r.CreatedBy == me.Id || r.EmployeeId == me.Id || r.Employee2Id == me.Id || me.Super;
        if ((r.Status is "PENDING" or "RETURNED" or "VERIFIED" or "APPROVED") && involved) actions.Add("withdraw");
        if (r.Status == "RETURNED" && (r.CreatedBy == me.Id || me.Super)) actions.Add("resubmit");
        if (r.Status == "APPROVED" && CanPost(me, r, steps, "order")) actions.Add("order");
        if (r.Status == "ORDERED" && CanPost(me, r, steps, "relieve")) actions.Add("relieve");
        if (r.Status == "RELIEVED" && CanPost(me, r, steps, "join")) actions.Add("join");
        if (win != null && win.Status == "LIST_PUBLISHED" && win.ObjectionFrom != null && now >= win.ObjectionFrom && now <= win.ObjectionTo && r.SelStatus != null && (r.EmployeeId == me.Id || r.CreatedBy == me.Id))
            actions.Add("objection");

        var lastOrder = steps.Count == 0 ? 0 : steps.Max(s => s.StepOrder);

        return new
        {
            request = new
            {
                r.Id, r.RequestNo, r.Status, r.TypeKey, typeName = type?.Name, procMode = type?.ProcMode, categoryName = cat?.Name, seniorityEffect = type?.SeniorityEffect, applyOn = type?.ApplyOn,
                employee = await PersonAsync(c, r.EmployeeId),
                partner = r.Employee2Id == null ? null : await PersonAsync(c, r.Employee2Id.Value),
                createdBy = await PersonAsync(c, r.CreatedBy),
                fromPath = await NodePathAsync(c, r.FromNodeId),
                toPath = await NodePathAsync(c, r.ToNodeId),
                department = r.ToDepartmentId == null ? null : new { from = await LookupNameAsync(c, "departments", "dep_name", r.FromDepartmentId), to = await LookupNameAsync(c, "departments", "dep_name", r.ToDepartmentId) },
                designation = r.ToDesignationId == null ? null : new { from = await LookupNameAsync(c, "designations", "name", r.FromDesignationId), to = await LookupNameAsync(c, "designations", "name", r.ToDesignationId) },
                requestedDate = r.RequestedDate?.ToString("yyyy-MM-dd"), r.Reason, r.ConsentStatus, needsRelaxation = r.NeedsRelaxation == 1, r.RelaxationReason, r.CurrentStep,
                windowName = win?.Name, r.RankNo, r.SelStatus, r.SelectionNote,
                order = r.OrderNo == null ? null : new { no = r.OrderNo, date = r.OrderDate?.ToString("yyyy-MM-dd"), fileId = r.OrderFileId, fileName = r.OrderFileName },
                relieveDate = r.RelieveDate?.ToString("yyyy-MM-dd"), joinDate = r.JoinDate?.ToString("yyyy-MM-dd"),
                applyAt = r.ApplyAt?.ToString("yyyy-MM-dd"), postingApplied = r.PostingApplied == 1, r.PostingError, createdAt = r.CreatedAt
            },
            consent = consentList,
            checks = (await c.QueryAsync<CheckResult>("SELECT subject AS Subject, rule_key AS RuleKey, name AS Name, strength AS Strength, state AS State, message AS Message FROM tr_request_check WHERE request_id=@id ORDER BY id", new { id })).ToList(),
            steps = (await c.QueryAsync(@"SELECT s.step_order AS stepOrder, s.name AS name, s.action AS action, s.authority AS authority, s.role_codes AS roleCodes, s.employee_ids AS employeeIds, s.anchor AS anchor,
                    s.decision AS decision, s.acted_at AS actedAt, s.remarks AS remarks, TRIM(CONCAT(COALESCE(e.first_name,''),' ',COALESCE(e.last_name,''))) AS actedByName
                FROM tr_request_step s LEFT JOIN employee_master e ON e.employee_id = s.acted_by WHERE s.request_id=@id ORDER BY s.step_order", new { id })).ToDictList(),
            docs = (await c.QueryAsync("SELECT label AS label, file_id AS fileId, file_name AS fileName FROM tr_request_doc WHERE request_id=@id ORDER BY id", new { id })).ToDictList(),
            objections = (await c.QueryAsync("SELECT id AS id, text AS text, status AS status, resolution AS resolution, created_at AS createdAt FROM tr_objection WHERE request_id=@id ORDER BY id DESC", new { id })).ToDictList(),
            log = (await c.QueryAsync(@"SELECT l.action AS action, l.logged_at AS at, l.remarks AS remarks, TRIM(CONCAT(COALESCE(e.first_name,''),' ',COALESCE(e.last_name,''))) AS byName
                FROM tr_request_log l LEFT JOIN employee_master e ON e.employee_id = l.by_user WHERE l.request_id=@id ORDER BY l.id", new { id })).ToDictList(),
            actions,
            needsRelaxationReason = r.NeedsRelaxation == 1 && cur != null && cur.StepOrder == lastOrder && actions.Contains("decide")
        };
    });

    // =========================================================
    // ACTIONS
    // =========================================================

    // POST {id}/decide - approve / reject / return at the current step
    [HttpPost("{id:long}/decide")]
    public Task<IActionResult> Decide(long id, [FromBody] DecideBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        var steps = await LoadStepsAsync(c, id);
        var step = steps.FirstOrDefault(s => s.StepOrder == r.CurrentStep) ?? throw new TrException("This request has no current step.", 409);
        if (!CanDecide(me, r, step, steps)) throw new TrException("You cannot act on this step of the request.", 403);

        var decision = (b.Decision ?? "").ToUpperInvariant();
        var remarks = string.IsNullOrWhiteSpace(b.Remarks) ? null : b.Remarks.Trim();
        if (decision is not ("APPROVE" or "REJECT" or "RETURN")) throw new TrException("Choose approve, reject or return.");
        if (decision != "APPROVE" && (remarks == null || remarks.Length < 3)) throw new TrException("A written reason is required to reject or return a request.");

        var lastStep = steps.OrderBy(s => s.StepOrder).Last();
        var isLast = r.CurrentStep >= lastStep.StepOrder;
        string? relaxReason = null;
        if (decision == "APPROVE" && isLast && r.NeedsRelaxation == 1)
        {
            relaxReason = string.IsNullOrWhiteSpace(b.RelaxationReason) ? null : b.RelaxationReason.Trim();
            if (relaxReason == null || relaxReason.Length < 5) throw new TrException("This request fails a rule that needs relaxation. Record the reason for relaxing it.");

            var relaxRoles = await c.QueryAsync<string?>(@"SELECT ru.relax_role_code FROM tr_request_check ch JOIN tr_rule ru ON ru.policy_id=@pid AND ru.rule_key=ch.rule_key
                WHERE ch.request_id=@id AND ch.strength='RELAX' AND ch.state='FAIL'", new { id, pid = r.PolicyId });
            foreach (var rr in relaxRoles)
                if (!string.IsNullOrEmpty(rr) && !me.Super && !string.Equals(rr, me.Role, StringComparison.OrdinalIgnoreCase))
                    throw new TrException($"Only the {rr} role can relax this rule.", 403);
        }

        var status = "PENDING";
        var next = r.CurrentStep;
        if (decision == "APPROVE") { if (isLast) status = r.WindowId != null ? "VERIFIED" : "APPROVED"; else next++; }
        else status = decision == "REJECT" ? "REJECTED" : "RETURNED";

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("UPDATE tr_request_step SET decision=@d, acted_by=@by, acted_at=NOW(), remarks=@rem WHERE id=@sid",
            new { d = decision == "APPROVE" ? "APPROVED" : decision == "REJECT" ? "REJECTED" : "RETURNED", by = me.Id, rem = remarks, sid = step.Id }, tx);
        var n = await c.ExecuteAsync("UPDATE tr_request SET status=@status, current_step=@next, relaxation_reason=COALESCE(@rr, relaxation_reason) WHERE id=@id AND status='PENDING' AND current_step=@cur",
            new { id, status, next, rr = relaxReason, cur = r.CurrentStep }, tx);
        if (n == 0) throw new TrException("Someone else has just acted on this request. Reload and try again.", 409);
        await LogAsync(c, tx, id, $"{decision}:{step.Name}", me.Id, remarks ?? relaxReason);

        var link = $"/transfers/{id}";
        if (decision == "APPROVE" && !isLast)
            await NotifyStepAsync(c, tx, r, steps.First(s => s.StepOrder == next), "Transfer request waiting for you");
        else if (status == "APPROVED")
        {
            await NotifyAsync(c, tx, await ApproversAsync(c, lastStep, r.FromNodeId, r.ToNodeId, tx), "Transfer approved: the order can be issued", r.RequestNo, link);
            await NotifyAsync(c, tx, Parties(r), "Transfer request approved", r.RequestNo, link);
        }
        else if (status == "VERIFIED")
            await NotifyAsync(c, tx, Parties(r), "Application verified for the transfer window", r.RequestNo, link);
        else if (decision != "APPROVE")
            await NotifyAsync(c, tx, Parties(r), decision == "REJECT" ? "Transfer request rejected" : "Transfer request returned for correction", $"{r.RequestNo}: {remarks}", link);

        tx.Commit();
        return new { message = "Done.", status };
    });

    // POST {id}/consent - each required employee accepts or declines
    [HttpPost("{id:long}/consent")]
    public Task<IActionResult> Consent(long id, [FromBody] ConsentBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        var type = await TypeOfAsync(c, r);
        var need = RequiredConsenters(r, type);
        var given = await GivenConsentsAsync(c, id);
        if (r.ConsentStatus != "PENDING" || !need.Contains(me.Id) || given.Contains(me.Id))
            throw new TrException("There is no consent waiting from you on this request.", 403);

        using var tx = c.BeginTransaction();
        if (!b.Accept)
        {
            await c.ExecuteAsync("UPDATE tr_request SET consent_status='DECLINED', status='REJECTED' WHERE id=@id", new { id }, tx);
            await LogAsync(c, tx, id, "CONSENT_DECLINED", me.Id, b.Remarks?.Trim());
            await NotifyAsync(c, tx, Parties(r).Where(x => x != me.Id), "Transfer consent declined", $"{r.RequestNo}: a required consent was declined.", $"/transfers/{id}");
        }
        else
        {
            await LogAsync(c, tx, id, "CONSENT_GIVEN", me.Id, b.Remarks?.Trim());
            if (need.All(x => x == me.Id || given.Contains(x)))
            {
                await c.ExecuteAsync("UPDATE tr_request SET consent_status='GIVEN' WHERE id=@id", new { id }, tx);
                var steps = await LoadStepsAsync(c, id, tx);
                var first = steps.FirstOrDefault(s => s.StepOrder == r.CurrentStep);
                if (first != null) await NotifyStepAsync(c, tx, r, first, "Transfer request waiting for you");
            }
        }
        tx.Commit();
        return new { message = "Recorded." };
    });

    // POST {id}/withdraw
    [HttpPost("{id:long}/withdraw")]
    public Task<IActionResult> Withdraw(long id, [FromBody] RemarksBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        var involved = r.CreatedBy == me.Id || r.EmployeeId == me.Id || r.Employee2Id == me.Id || me.Super;
        if (!(r.Status is "PENDING" or "RETURNED" or "VERIFIED" or "APPROVED") || !involved) throw new TrException("This request cannot be withdrawn.", 403);

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("UPDATE tr_request SET status='WITHDRAWN' WHERE id=@id", new { id }, tx);
        await LogAsync(c, tx, id, "WITHDRAWN", me.Id, b?.Remarks?.Trim());
        await NotifyAsync(c, tx, Parties(r).Where(x => x != me.Id), "Transfer request withdrawn", r.RequestNo, $"/transfers/{id}");
        tx.Commit();
        return new { message = "Withdrawn." };
    });

    // POST {id}/resubmit - after the request was returned for correction
    [HttpPost("{id:long}/resubmit")]
    public Task<IActionResult> Resubmit(long id, [FromBody] ResubmitBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        if (r.Status != "RETURNED" || !(r.CreatedBy == me.Id || me.Super)) throw new TrException("This request cannot be resubmitted.", 403);

        var type = await TypeOfAsync(c, r);
        var reason = string.IsNullOrWhiteSpace(b?.Reason) ? r.Reason : b!.Reason!.Trim();
        if (reason.Length < 5 || reason.Length > 4000) throw new TrException("Enter the reason for the transfer (at least 5 characters).");
        var docs = CleanDocs(b?.Documents);

        var cat = r.CategoryKey == null ? null : await c.QueryFirstOrDefaultAsync<CatRow>($"SELECT {CatCols} FROM tr_category WHERE policy_id=@p AND category_key=@k", new { p = r.PolicyId, k = r.CategoryKey });
        if (cat?.DocLabel is { Length: > 0 } need)
        {
            var have = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tr_request_doc WHERE request_id=@id AND LOWER(label)=LOWER(@need)", new { id, need });
            if (have == 0 && !docs.Any(d => string.Equals(d.Label, need, StringComparison.OrdinalIgnoreCase))) throw new TrException($"Attach the required document: {need}.");
        }

        if (r.WindowId != null)
        {
            var w = await WindowOfAsync(c, r.WindowId);
            if (w == null || w.Status != "PUBLISHED" || DateTime.Now > w.ApplyTo) throw new TrException("The application period of this window has ended.", 409);
        }

        var map = await CurrentMappingAsync(c, r.EmployeeId) ?? throw new TrException("The employee has no current organization assignment.", 409);
        var checks = await EvaluateAsync(c, r.PolicyId, r.EmployeeId, r.TypeKey, r.CategoryKey, map.EffectiveFrom, r.Employee2Id, 1);
        if (r.Employee2Id != null)
        {
            var pm = await CurrentMappingAsync(c, r.Employee2Id.Value);
            if (pm != null) checks.AddRange(await EvaluateAsync(c, r.PolicyId, r.Employee2Id.Value, r.TypeKey, r.CategoryKey, pm.EffectiveFrom, null, 2));
        }
        if (type != null && type.VacancyCheck != "NONE" && type.ProcMode == "INDIVIDUAL")
            checks.Add(await VacancyCheckAsync(c, type.VacancyCheck, cat, r.ToNodeId, r.ToDesignationId ?? r.FromDesignationId, id));
        if (checks.Any(x => x.Strength == "BLOCK" && x.State == "FAIL")) throw new TrException("This transfer cannot continue. See the rules that failed.", 422, checks);

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("DELETE FROM tr_request_check WHERE request_id=@id", new { id }, tx);
        foreach (var ch in checks)
            await c.ExecuteAsync("INSERT INTO tr_request_check (request_id, subject, rule_key, name, strength, state, message) VALUES (@id,@sub,@k,@n,@s,@st,@m)",
                new { id, sub = ch.Subject, k = ch.RuleKey, n = ch.Name, s = ch.Strength, st = ch.State, m = ch.Message }, tx);
        foreach (var d in docs)
            await c.ExecuteAsync("INSERT INTO tr_request_doc (request_id, label, file_id, file_name, uploaded_by) VALUES (@id,@l,@f,@n,@by)", new { id, l = d.Label.Trim(), f = d.FileId, n = d.FileName.Trim(), by = me.Id }, tx);
        await c.ExecuteAsync("UPDATE tr_request_step SET decision=NULL, acted_by=NULL, acted_at=NULL, remarks=NULL WHERE request_id=@id", new { id }, tx);
        await c.ExecuteAsync("UPDATE tr_request SET reason=@reason, status='PENDING', current_step=1, needs_relaxation=@relax, relaxation_reason=NULL WHERE id=@id",
            new { id, reason, relax = checks.Any(x => x.Strength == "RELAX" && x.State == "FAIL") ? 1 : 0 }, tx);
        await LogAsync(c, tx, id, "RESUBMITTED", me.Id, null);

        if (r.ConsentStatus != "PENDING")
        {
            var steps = await LoadStepsAsync(c, id, tx);
            if (steps.Count > 0) await NotifyStepAsync(c, tx, r, steps.OrderBy(s => s.StepOrder).First(), "Transfer request waiting for you");
        }
        tx.Commit();
        return new { message = "Resubmitted." };
    });

    // ---- orders ----

    private static async Task AssertOrderTimeAsync(IDbConnection c, Req r)
    {
        if (r.WindowId == null) return;
        var w = await WindowOfAsync(c, r.WindowId);
        if (w?.OrdersFrom != null && DateTime.Now < w.OrdersFrom) throw new TrException($"Orders for this window can be issued from {w.OrdersFrom:dd MMM yyyy HH:mm}.", 409);
    }

    private static async Task AssertEffectiveAsync(IDbConnection c, Req r, DateTime date)
    {
        if (r.WindowId == null) return;
        var w = await WindowOfAsync(c, r.WindowId);
        if (w?.EffectiveOn != null && date.Date < w.EffectiveOn.Value.Date) throw new TrException($"The date cannot be before the window's effective date ({w.EffectiveOn:dd MMM yyyy}).");
    }

    private static (string No, DateTime Date, string? FileId, string? FileName) ReadOrder(string? orderNo, string? orderDate, string? fileId, string? fileName)
    {
        var no = (orderNo ?? "").Trim();
        if (no.Length is 0 or > 60) throw new TrException("Enter the order number.");
        var date = ReqDate(orderDate, "The order date");
        if (!string.IsNullOrEmpty(fileId) && !FileIdRx.IsMatch(fileId)) throw new TrException("The order document was not uploaded properly.");
        return (no, date, string.IsNullOrEmpty(fileId) ? null : fileId, string.IsNullOrEmpty(fileId) ? null : fileName?.Trim());
    }

    private static async Task RecordOrderAsync(IDbConnection c, IDbTransaction tx, Req r, string no, DateTime date, string? fileId, string? fileName, long by)
    {
        var n = await c.ExecuteAsync("UPDATE tr_request SET status='ORDERED', order_no=@no, order_date=@date, order_file_id=@fid, order_file_name=@fn WHERE id=@id AND status='APPROVED'",
            new { id = r.Id, no, date, fid = fileId, fn = fileName }, tx);
        if (n == 0) throw new TrException($"{r.RequestNo} is no longer waiting for an order.", 409);
        await LogAsync(c, tx, r.Id, "ORDER_ISSUED", by, no);
        await NotifyAsync(c, tx, Parties(r), "Transfer order issued", $"{r.RequestNo}: order {no} dated {date:dd MMM yyyy}.", $"/transfers/{r.Id}");
    }

    // POST {id}/order
    [HttpPost("{id:long}/order")]
    public Task<IActionResult> IssueOrder(long id, [FromBody] OrderBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        var steps = await LoadStepsAsync(c, id);
        if (r.Status != "APPROVED" || !CanPost(me, r, steps, "order")) throw new TrException("You cannot issue the order for this request.", 403);
        await AssertOrderTimeAsync(c, r);
        var (no, date, fid, fn) = ReadOrder(b.OrderNo, b.OrderDate, b.FileId, b.FileName);

        using var tx = c.BeginTransaction();
        await RecordOrderAsync(c, tx, r, no, date, fid, fn, me.Id);
        tx.Commit();
        return new { message = "Order recorded." };
    });

    // POST bulk-order - one common order for many approved requests (all or none)
    [HttpPost("bulk-order")]
    public Task<IActionResult> BulkOrder([FromBody] BulkOrderBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var ids = (b.Ids ?? new()).Distinct().ToList();
        if (ids.Count is 0 or > 500) throw new TrException("Select between 1 and 500 requests.");
        var (no, date, fid, fn) = ReadOrder(b.OrderNo, b.OrderDate, b.FileId, b.FileName);

        var ok = new List<Req>();
        var problems = new List<string>();
        foreach (var id in ids)
        {
            var r = await c.QueryFirstOrDefaultAsync<Req>($"SELECT {ReqCols} FROM tr_request WHERE id=@id", new { id });
            if (r == null) { problems.Add($"#{id}: not found."); continue; }
            var steps = await LoadStepsAsync(c, id);
            if (r.Status != "APPROVED") problems.Add($"{r.RequestNo}: not waiting for an order.");
            else if (!CanPost(me, r, steps, "order")) problems.Add($"{r.RequestNo}: you are not the authority for this order.");
            else
            {
                try { await AssertOrderTimeAsync(c, r); ok.Add(r); }
                catch (TrException ex) { problems.Add($"{r.RequestNo}: {ex.Message}"); }
            }
        }
        if (problems.Count > 0)
            throw new TrException("Some selected requests cannot be ordered. " + string.Join(" ", problems.Take(5)) + (problems.Count > 5 ? $" … and {problems.Count - 5} more." : ""), 422);

        using var tx = c.BeginTransaction();
        foreach (var r in ok) await RecordOrderAsync(c, tx, r, no, date, fid, fn, me.Id);
        tx.Commit();
        return new { message = $"Order {no} recorded for {ok.Count} request(s)." };
    });

    // POST {id}/relieve
    [HttpPost("{id:long}/relieve")]
    public Task<IActionResult> Relieve(long id, [FromBody] DateBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        var steps = await LoadStepsAsync(c, id);
        if (r.Status != "ORDERED" || !CanPost(me, r, steps, "relieve")) throw new TrException("You cannot relieve the employee on this request.", 403);

        var date = ReqDate(b.Date, "The relieving date");
        await AssertEffectiveAsync(c, r, date);
        var type = await TypeOfAsync(c, r);

        using var tx = c.BeginTransaction();
        var n = await c.ExecuteAsync("UPDATE tr_request SET status='RELIEVED', relieve_date=@date WHERE id=@id AND status='ORDERED'", new { id, date }, tx);
        if (n == 0) throw new TrException("This request is no longer waiting to be relieved.", 409);
        if (type?.ApplyOn == "RELIEVING") await ApplyOrScheduleAsync(c, tx, r, date, me.Id);
        await LogAsync(c, tx, id, "RELIEVED", me.Id, date.ToString("yyyy-MM-dd"));

        var toSide = steps.Where(s => s.Authority == "ROLE" && s.Anchor != "FROM").OrderBy(s => s.StepOrder).FirstOrDefault() ?? steps.First();
        await NotifyAsync(c, tx, await ApproversAsync(c, toSide, r.FromNodeId, r.ToNodeId, tx), "Employee relieved: joining to be recorded", r.RequestNo, $"/transfers/{id}");
        await NotifyAsync(c, tx, Parties(r), "Relieved from the current posting", $"{r.RequestNo}: relieved on {date:dd MMM yyyy}.", $"/transfers/{id}");
        tx.Commit();
        return new { message = "Relieved." };
    });

    // POST {id}/join - the employee reports at the new place; the request is completed
    [HttpPost("{id:long}/join")]
    public Task<IActionResult> Join(long id, [FromBody] DateBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        var steps = await LoadStepsAsync(c, id);
        if (r.Status != "RELIEVED" || !CanPost(me, r, steps, "join")) throw new TrException("You cannot record the joining on this request.", 403);

        var date = ReqDate(b.Date, "The joining date");
        if (r.RelieveDate != null && date < r.RelieveDate.Value.Date) throw new TrException("The joining date cannot be before the relieving date.");
        await AssertEffectiveAsync(c, r, date);
        var type = await TypeOfAsync(c, r);

        using var tx = c.BeginTransaction();
        var n = await c.ExecuteAsync("UPDATE tr_request SET status='COMPLETED', join_date=@date WHERE id=@id AND status='RELIEVED'", new { id, date }, tx);
        if (n == 0) throw new TrException("This request is no longer waiting for the joining.", 409);
        if (type?.ApplyOn != "RELIEVING") await ApplyOrScheduleAsync(c, tx, r, date, me.Id);
        await LogAsync(c, tx, id, "JOINED", me.Id, date.ToString("yyyy-MM-dd"));
        await NotifyAsync(c, tx, Parties(r), "Transfer completed", $"{r.RequestNo}: joined on {date:dd MMM yyyy}.", $"/transfers/{id}");
        tx.Commit();
        return new { message = "Completed." };
    });

    // POST {id}/objection - an applicant objects to the published list during the objection period
    [HttpPost("{id:long}/objection")]
    public Task<IActionResult> Objection(long id, [FromBody] ObjectionBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        if (r.WindowId == null || r.SelStatus == null || !(r.EmployeeId == me.Id || r.CreatedBy == me.Id)) throw new TrException("You cannot raise an objection on this request.", 403);

        var w = await WindowOfAsync(c, r.WindowId);
        var now = DateTime.Now;
        if (w == null || w.Status != "LIST_PUBLISHED" || w.ObjectionFrom == null || now < w.ObjectionFrom || now > w.ObjectionTo)
            throw new TrException("Objections can only be raised during the objection period.", 409);

        var text = (b.Text ?? "").Trim();
        if (text.Length is < 5 or > 1000) throw new TrException("Write your objection (5 to 1000 characters).");

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("INSERT INTO tr_objection (window_id, request_id, by_user, text) VALUES (@w,@id,@by,@text)", new { w = w.Id, id, by = me.Id, text }, tx);
        await LogAsync(c, tx, id, "OBJECTION", me.Id, text);
        tx.Commit();
        return new { message = "Your objection was recorded." };
    });

    // GET {id}/files/{fileId}/link - signed link to a document of this request (or its order copy)
    [HttpGet("{id:long}/files/{fileId}/link")]
    public Task<IActionResult> FileLink(long id, string fileId, [FromQuery] bool download = false) => Run(async c =>
    {
        var me = await ActorAsync(c);
        var r = await LoadReqAsync(c, id);
        if (!CanView(me, r, await LoadStepsAsync(c, id))) throw new TrException("You do not have access to this request.", 403);

        var name = await c.QueryFirstOrDefaultAsync<string?>(@"SELECT file_name FROM tr_request_doc WHERE request_id=@id AND file_id=@fileId
            UNION SELECT order_file_name FROM tr_request WHERE id=@id AND order_file_id=@fileId", new { id, fileId }) ?? throw new TrException("Document not found.", 404);

        try
        {
            var signed = await _files.SignAsync(fileId, download, download ? name : null);
            return new { url = signed.url, expiresAt = signed.expiresAt };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { throw new TrException("The file is no longer available.", 404); }
        catch (HttpRequestException) { throw new TrException("File storage service is unreachable.", 502); }
    });

    // =========================================================
    // ROTATION DUE LIST + BULK TRANSFERS
    // =========================================================

    // GET rotation - employees who have stayed too long at one place (only those inside the caller's data scope)
    [HttpGet("rotation")]
    public Task<IActionResult> Rotation() => Run(async c =>
    {
        var me = await ActorAsync(c);
        await RequireMenuAsync(c, me, RotationRoute, "can_view", "see the rotation list");

        var policy = await c.QueryFirstOrDefaultAsync<PolicyRow>($"SELECT {PolicyCols} FROM tr_policy WHERE status='ACTIVE' ORDER BY id DESC LIMIT 1");
        if (policy == null) return new { policy = (object?)null };

        var rules = (await c.QueryAsync("SELECT rule_key AS ruleKey, name AS name, designation_ids AS designationIds, max_years AS maxYears, warn_months AS warnMonths FROM tr_rotation WHERE policy_id=@id AND is_enabled=1 ORDER BY id", new { id = policy.Id })).ToList();
        var result = new Dictionary<long, object>();

        foreach (var raw in rules)
        {
            var rule = (IDictionary<string, object>)raw;
            var maxYears = Convert.ToDouble(rule["maxYears"]);
            var maxMonths = (int)Math.Round(maxYears * 12);
            var cut = DateTime.Today.AddMonths(-Math.Max(maxMonths - Convert.ToInt32(rule["warnMonths"]), 0));
            var desigs = Csv(Convert.ToString(rule["designationIds"])).Select(int.Parse).ToList();

            var sql = $@"SELECT e.employee_id AS employeeId, e.employee_code AS code, TRIM(CONCAT(e.first_name,' ',COALESCE(e.last_name,''))) AS name, d.name AS designation,
                    m.node_id AS nodeId, n.node_name AS place, m.effective_from AS since, TIMESTAMPDIFF(MONTH, m.effective_from, CURDATE()) AS months
                FROM employee_master e
                JOIN master_employee_organization_mapping m ON m.employee_id = e.employee_id AND m.is_current = 1
                JOIN master_organization_nodes n ON n.node_id = m.node_id
                LEFT JOIN designations d ON d.id = e.designation_id
                WHERE e.status='ACTIVE' AND {DataScope.Clause(me.Scope, "m.node_id")} AND m.effective_from <= @cut
                  AND NOT EXISTS (SELECT 1 FROM tr_request r WHERE (r.employee_id=e.employee_id OR r.employee2_id=e.employee_id) AND r.status IN ({OpenSql}))
                  {(desigs.Count > 0 ? "AND e.designation_id IN @desigs" : "")}
                ORDER BY m.effective_from LIMIT 1000";

            foreach (var rowRaw in await c.QueryAsync(sql, new { cut, desigs }))
            {
                var d = (IDictionary<string, object>)rowRaw;
                var eid = Convert.ToInt64(d["employeeId"]);
                if (result.ContainsKey(eid)) continue;
                var months = Convert.ToInt32(d["months"]);
                result[eid] = new
                {
                    employeeId = eid, code = d["code"], name = d["name"], designation = d["designation"] is DBNull ? null : d["designation"],
                    nodeId = Convert.ToInt32(d["nodeId"]), place = d["place"], since = Convert.ToDateTime(d["since"]).ToString("yyyy-MM-dd"),
                    months, rule = rule["name"], maxYears, overdue = months >= maxMonths
                };
            }
        }

        var (rInits, rRoleSql) = RaiseFilter(me, true);

        return new
        {
            policy = new { policy.Id, policy.Name },
            canRaise = await MenuPermAsync(c, me, RotationRoute, "can_add"),
            rows = result.Values.OrderByDescending(x => ((dynamic)x).overdue).ThenByDescending(x => ((dynamic)x).months).ToList(),
            types = (await c.QueryAsync($"SELECT type_key AS typeKey, name AS name, dept_change AS deptChange, desig_change AS desigChange FROM tr_type WHERE policy_id=@id AND proc_mode='INDIVIDUAL' AND is_enabled=1 AND needs_category=0 AND initiated_by IN @inits{rRoleSql}", new { id = policy.Id, inits = rInits, role = me.Role })).ToDictList(),
            departments = (await c.QueryAsync("SELECT id AS id, dep_name AS name FROM departments WHERE active=1 ORDER BY sequence, dep_name")).ToDictList(),
            designations = (await c.QueryAsync("SELECT id AS id, name AS name FROM designations WHERE active=1 ORDER BY sequence, name")).ToDictList(),
            destinations = await DataScope.OrgLookupsAsync(c, new DataScopeInfo { Unrestricted = true })
        };
    });

    // POST rotation/transfer - raise one request per selected employee (each is checked on its own; failures do not stop the rest)
    [HttpPost("rotation/transfer")]
    public Task<IActionResult> RotationTransfer([FromBody] RotationBody b) => Run(async c =>
    {
        var me = await ActorAsync(c);
        await RequireMenuAsync(c, me, RotationRoute, "can_add", "raise transfers from the rotation list");

        var items = (b.Items ?? new()).DistinctBy(x => x.EmployeeId).ToList();
        if (items.Count is 0 or > 200) throw new TrException("Select between 1 and 200 employees.");
        var reason = (b.Reason ?? "").Trim();
        if (reason.Length < 5) throw new TrException("Enter the reason for the transfers.");

        var results = new List<object>();
        int created = 0, failed = 0;
        foreach (var it in items)
        {
            try
            {
                await AssertMayRaiseAsync(c, me, b.TypeKey, it.EmployeeId, true);
                var body = new SubmitBody { EmployeeId = it.EmployeeId, TypeKey = b.TypeKey, ToNodeId = it.ToNodeId, ToDepartmentId = b.ToDepartmentId, ToDesignationId = b.ToDesignationId, Reason = reason };
                var prep = await PrepareAsync(c, me, body, true);
                if (prep.Type.ProcMode != "INDIVIDUAL" || prep.Type.NeedsCategory == 1) throw new TrException("This transfer type cannot be used for bulk rotation.");
                var (rs, rq, docs) = ValidateSubmit(prep, body);
                var id = await InsertRequestAsync(c, me, prep, body, rs, rq, docs, "RAISED_FROM_ROTATION_LIST");
                results.Add(new { employeeId = it.EmployeeId, ok = true, id });
                created++;
            }
            catch (TrException ex) { results.Add(new { employeeId = it.EmployeeId, ok = false, error = ex.Message }); failed++; }
        }
        return new { created, failed, results };
    });

    // =========================================================
    // NOTIFICATIONS (the logged-in employee's own)
    // =========================================================

    [HttpGet("notifications")]
    public Task<IActionResult> Notifications([FromQuery] int limit = 30) => Run(async c =>
    {
        var id = MyId();
        limit = Math.Clamp(limit, 1, 100);
        return new
        {
            unread = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_notification WHERE employee_id=@id AND is_read=0", new { id }),
            items = (await c.QueryAsync("SELECT id AS id, title AS title, body AS body, link AS link, is_read AS isRead, created_at AS createdAt FROM app_notification WHERE employee_id=@id ORDER BY id DESC LIMIT @limit", new { id, limit })).ToDictList()
        };
    });

    [HttpGet("notifications/count")]
    public Task<IActionResult> NotificationCount() => Run(async c =>
        new { unread = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_notification WHERE employee_id=@id AND is_read=0", new { id = MyId() }) });

    // POST notifications/read - body { ids: [..] } marks those read; empty or missing marks everything read
    [HttpPost("notifications/read")]
    public Task<IActionResult> ReadNotifications([FromBody] ReadBody? b) => Run(async c =>
    {
        var id = MyId();
        if (b?.Ids is { Count: > 0 } ids) await c.ExecuteAsync("UPDATE app_notification SET is_read=1 WHERE employee_id=@id AND id IN @ids", new { id, ids });
        else await c.ExecuteAsync("UPDATE app_notification SET is_read=1 WHERE employee_id=@id AND is_read=0", new { id });
        return new { message = "Done." };
    });
}