using System.Data;
using System.Security.Claims;
using Dapper;

namespace HrmsBackend.Common;

public sealed class DataScopeInfo
{
    public bool Unrestricted { get; init; }
    public int? HomeNodeId { get; init; }
    public HashSet<int> NodeIds { get; init; } = new();

    public bool Contains(int nodeId) => Unrestricted || NodeIds.Contains(nodeId);
}

public class IdName
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class ScopedNode
{
    public int Id { get; set; }
    public int OrgId { get; set; }
    public int? TypeId { get; set; }
    public int? ParentId { get; set; }   // null when the parent is outside the user's scope
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public bool IsRoot { get; set; }     // true only for the real top node of an organization
}

public class OrgLookups
{
    public object Scope { get; set; } = new();
    public List<IdName> Organizations { get; set; } = new();
    public List<IdName> NodeTypes { get; set; } = new();
    public List<ScopedNode> Nodes { get; set; } = new();
}

/// <summary>
/// Data-flow visibility: a user sees their own organization node plus every node below it in
/// master_organization_data_flow. Siblings and unrelated nodes are never visible.
/// Usage in any controller:
///   var scope = await DataScope.ForUserAsync(conn, User, employeeId);
///   WHERE ... AND {DataScope.Clause(scope, "m.node_id")}      // or scope.Contains(nodeId)
/// </summary>
public static class DataScope
{
    // role codes that see everything
    public static readonly HashSet<string> UnrestrictedRoles = new(StringComparer.OrdinalIgnoreCase) { "SUPERADMIN" };

    private sealed class FlowRow
    {
        public long Id { get; set; }
        public long? ParentId { get; set; }
        public long NodeId { get; set; }
    }

    private sealed class NodeRow
    {
        public int Id { get; set; }
        public int OrgId { get; set; }
        public int? TypeId { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = "";
        public string? Code { get; set; }
    }

    public static async Task<DataScopeInfo> ForUserAsync(IDbConnection c, ClaimsPrincipal user, long employeeId)
    {
        var role = user.FindFirst("roleCode")?.Value;
        if (role != null && UnrestrictedRoles.Contains(role)) return new DataScopeInfo { Unrestricted = true };

        var home = await c.ExecuteScalarAsync<int?>(@"
            SELECT node_id FROM master_employee_organization_mapping
            WHERE employee_id=@employeeId AND is_current=1
            ORDER BY mapping_id DESC LIMIT 1", new { employeeId });

        var ids = new HashSet<int>();
        if (home != null)
        {
            ids.Add(home.Value);
            foreach (var n in await FlowDescendantsAsync(c, new[] { home.Value })) ids.Add(n);
        }
        return new DataScopeInfo { HomeNodeId = home, NodeIds = ids };
    }

    // SQL fragment for a node-id column. Ids are ints, so it is safe to inline.
    public static string Clause(DataScopeInfo scope, string column)
    {
        if (scope.Unrestricted) return "1=1";
        return scope.NodeIds.Count == 0 ? "1=0" : $"{column} IN ({string.Join(",", scope.NodeIds)})";
    }

    // node ids below the given nodes in the data flow tree (the given nodes themselves are included when they are in the tree)
    public static async Task<HashSet<int>> FlowDescendantsAsync(IDbConnection c, IEnumerable<int> rootNodeIds)
    {
        var rows = (await c.QueryAsync<FlowRow>(@"
            SELECT id AS Id, parent_data_flow_id AS ParentId, organization_node_id AS NodeId
            FROM master_organization_data_flow WHERE is_active=1")).ToList();

        var kids = rows.Where(r => r.ParentId != null).GroupBy(r => r.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var roots = rootNodeIds.ToHashSet();

        var result = new HashSet<int>();
        var seen = new HashSet<long>();
        var stack = new Stack<FlowRow>(rows.Where(r => roots.Contains((int)r.NodeId)));
        while (stack.Count > 0)
        {
            var r = stack.Pop();
            if (!seen.Add(r.Id)) continue;
            result.Add((int)r.NodeId);
            if (kids.TryGetValue(r.Id, out var ks)) foreach (var k in ks) stack.Push(k);
        }
        return result;
    }

    // "this node and everything below it", limited to what the user may see.
    // Unrestricted users also get the organization-hierarchy children, so picking any node in the hierarchy works for them.
    public static async Task<List<int>> SubtreeAsync(IDbConnection c, int rootId, DataScopeInfo scope)
    {
        var set = await FlowDescendantsAsync(c, new[] { rootId });
        set.Add(rootId);

        if (scope.Unrestricted)
        {
            var nodes = (await c.QueryAsync<NodeRow>(
                "SELECT node_id AS Id, parent_node_id AS ParentId FROM master_organization_nodes WHERE deleted_at IS NULL")).ToList();
            var children = nodes.Where(n => n.ParentId != null).GroupBy(n => n.ParentId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

            var stack = new Stack<int>();
            stack.Push(rootId);
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!seen.Add(id)) continue;
                set.Add(id);
                if (children.TryGetValue(id, out var kids)) foreach (var k in kids) stack.Push(k);
            }
        }
        else
        {
            set.IntersectWith(scope.NodeIds);
        }
        return set.ToList();
    }

    // ---- organization lookups for pickers and filters (already limited to the user's scope) ----

    private static (string Table, string IdCol, string NameCol)? _orgMeta;
    private static (string Table, string IdCol, string NameCol)? _typeMeta;

    private static async Task<(string Table, string IdCol, string NameCol)> ResolveAsync(
        IDbConnection c, string table, string[] idCandidates, string[] nameCandidates)
    {
        var cols = (await c.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table", new { table })).ToList();
        if (cols.Count == 0) throw new InvalidOperationException($"Table '{table}' was not found.");

        string? Pick(string[] cands) => cands.FirstOrDefault(x => cols.Contains(x, StringComparer.OrdinalIgnoreCase));
        var id = Pick(idCandidates) ?? throw new InvalidOperationException($"No id column found in '{table}' (looked for: {string.Join(", ", idCandidates)}).");
        var name = Pick(nameCandidates) ?? throw new InvalidOperationException($"No name column found in '{table}' (looked for: {string.Join(", ", nameCandidates)}).");
        return (table, id, name);
    }

    public static async Task<OrgLookups> OrgLookupsAsync(IDbConnection c, DataScopeInfo scope)
    {
        var org = _orgMeta ??= await ResolveAsync(c, "master_organizations",
            new[] { "organization_id", "id" }, new[] { "organization_name", "org_name", "name", "legal_name", "organization", "title" });
        var typ = _typeMeta ??= await ResolveAsync(c, "master_organization_node_types",
            new[] { "node_type_id", "id" }, new[] { "node_type_name", "type_name", "name", "node_type", "title" });

        var all = (await c.QueryAsync<NodeRow>(@"
            SELECT node_id AS Id, organization_id AS OrgId, node_type_id AS TypeId, parent_node_id AS ParentId,
                   node_name AS Name, node_code AS Code
            FROM master_organization_nodes WHERE deleted_at IS NULL ORDER BY display_order, node_name")).ToList();

        var visible = all.Where(n => scope.Contains(n.Id)).ToList();
        var visibleIds = visible.Select(n => n.Id).ToHashSet();

        var nodes = visible.Select(n => new ScopedNode
        {
            Id = n.Id,
            OrgId = n.OrgId,
            TypeId = n.TypeId,
            ParentId = n.ParentId != null && visibleIds.Contains(n.ParentId.Value) ? n.ParentId : null,
            Name = n.Name,
            Code = n.Code,
            IsRoot = n.ParentId == null
        }).ToList();

        var orgs = (await c.QueryAsync<IdName>($"SELECT {org.IdCol} AS Id, {org.NameCol} AS Name FROM {org.Table} ORDER BY {org.NameCol}")).ToList();
        if (!scope.Unrestricted)
        {
            var orgIds = nodes.Select(n => n.OrgId).ToHashSet();
            orgs = orgs.Where(o => orgIds.Contains(o.Id)).ToList();
        }

        return new OrgLookups
        {
            Scope = new { unrestricted = scope.Unrestricted, homeNodeId = scope.HomeNodeId },
            Organizations = orgs,
            NodeTypes = (await c.QueryAsync<IdName>($"SELECT {typ.IdCol} AS Id, {typ.NameCol} AS Name FROM {typ.Table} ORDER BY {typ.NameCol}")).ToList(),
            Nodes = nodes
        };
    }
}