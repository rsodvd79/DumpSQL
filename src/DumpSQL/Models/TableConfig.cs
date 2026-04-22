namespace DumpSQL.Models;

public record TableConfig
{
    public string TableName { get; init; } = string.Empty;
    public string ReferenceField { get; init; } = string.Empty;
    public string ReferenceValue { get; init; } = string.Empty;
    /// <summary>
    /// Colonne chiave per l'upsert (MERGE ON / ON DUPLICATE KEY).
    /// Se vuoto, viene usata la prima colonna del resultset.
    /// </summary>
    public List<string> KeyColumns { get; init; } = [];
}
