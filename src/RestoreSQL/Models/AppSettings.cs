namespace RestoreSQL.Models;

public record AppSettings
{
    public string DatabaseType { get; init; } = "mssql";
    public string ConnectionString { get; init; } = string.Empty;
    public string InputDirectory { get; init; } = "input";

    /// <summary>
    /// Ordine di elaborazione delle tabelle. I file il cui nome inizia con una voce di questo elenco
    /// vengono elaborati nell'ordine specificato (rispettando le dipendenze FK).
    /// I file non presenti nell'elenco vengono elaborati dopo, in ordine alfabetico.
    /// </summary>
    public List<string> TableOrder { get; init; } = [];
}
