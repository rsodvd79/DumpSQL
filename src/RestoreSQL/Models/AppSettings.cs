namespace RestoreSQL.Models;

public record AppSettings
{
    public string DatabaseType { get; init; } = "mssql";
    public string ConnectionString { get; init; } = string.Empty;
    public string InputDirectory { get; init; } = "input";
}
