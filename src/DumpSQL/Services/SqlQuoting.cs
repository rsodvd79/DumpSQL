namespace DumpSQL.Services;

internal static class SqlQuoting
{
    internal static string QuoteMssql(string identifier)
    {
        // "THIP.ORD_VEN_RIG" → "[THIP].[ORD_VEN_RIG]"
        var parts = identifier.Split('.');
        return string.Join(".", parts.Select(p => $"[{p.Replace("]", "]]")}]"));
    }

    internal static string QuoteMysql(string identifier)
    {
        // "schema.table" → "`schema`.`table`"
        var parts = identifier.Split('.');
        return string.Join(".", parts.Select(p => $"`{p.Replace("`", "``")}`"));
    }
}
