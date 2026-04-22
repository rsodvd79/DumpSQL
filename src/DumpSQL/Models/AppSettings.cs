namespace DumpSQL.Models;

public record AppSettings
{
    public string DatabaseType { get; init; } = "mssql";
    public string ConnectionString { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = "output";
    public List<TableConfig> Tables { get; init; } = [];
}
