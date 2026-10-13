namespace HrmsBackend.Common;

/// <summary>
/// A lot of the original Express routes just do `res.json(rows)` — forwarding
/// the raw mysql2 result set straight to the client, with whatever column/alias
/// names were used in the SQL (mostly snake_case: role_code, department_name...).
///
/// To keep the exact same JSON shape for the existing frontend, we convert Dapper's
/// dynamic rows into plain Dictionary&lt;string, object?&gt; before returning them.
/// System.Text.Json does NOT apply the camelCase naming policy to dictionary keys
/// (only to POCO property names), so the original column names are preserved as-is.
/// </summary>
public static class DapperRowExtensions
{
    public static List<Dictionary<string, object?>> ToDictList(this IEnumerable<dynamic> rows)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (IDictionary<string, object> row in rows)
        {
            list.Add(new Dictionary<string, object?>(row));
        }
        return list;
    }
}
