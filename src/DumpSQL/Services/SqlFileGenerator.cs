using System.Text;
using Serilog;

namespace DumpSQL.Services;

public class SqlFileGenerator
{
    private readonly string _outputDirectory;
    private readonly string _databaseType;
    private const int MySqlBatchSize = 500;

    public SqlFileGenerator(string outputDirectory, string databaseType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseType);
        _outputDirectory = outputDirectory;
        _databaseType = databaseType.ToLowerInvariant();
    }

    public async Task GenerateAsync(
        string tableName,
        IReadOnlyList<string> keyColumns,
        IAsyncEnumerable<Dictionary<string, object?>> rows,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_outputDirectory);

        var safeFileName = tableName.Replace('.', '_').Replace('/', '_').Replace('\\', '_');
        var date = DateTime.Now.ToString("yyyyMMdd");
        var finalPath = Path.Combine(_outputDirectory, $"{safeFileName}_{date}.sql");
        var tmpPath = finalPath + ".tmp";

        int count = 0;

        await using (var writer = new StreamWriter(tmpPath, append: false, Encoding.UTF8))
        {
            await writer.WriteLineAsync(
                $"-- Dump generato il {DateTime.Now:yyyy-MM-dd HH:mm:ss} -- Tabella: {tableName}");
            await writer.WriteLineAsync();

            if (_databaseType == "mysql")
            {
                var batch = new List<Dictionary<string, object?>>(MySqlBatchSize);
                await foreach (var row in rows.WithCancellation(cancellationToken))
                {
                    batch.Add(row);
                    count++;
                    if (batch.Count >= MySqlBatchSize)
                    {
                        await writer.WriteLineAsync(BuildMysqlBatchUpsert(tableName, batch, keyColumns));
                        batch.Clear();
                    }
                }
                if (batch.Count > 0)
                    await writer.WriteLineAsync(BuildMysqlBatchUpsert(tableName, batch, keyColumns));
            }
            else
            {
                await foreach (var row in rows.WithCancellation(cancellationToken))
                {
                    await writer.WriteLineAsync(BuildMssqlMerge(tableName, row, keyColumns));
                    count++;
                }
            }
        }

        if (count == 0)
        {
            File.Delete(tmpPath);
            Log.Warning("Nessun dato per {Table}: file non generato", tableName);
            return;
        }

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(tmpPath, finalPath);

        Log.Information("File generato: {File} ({Count} righe)", finalPath, count);
    }

    private static string BuildMssqlMerge(
        string tableName, Dictionary<string, object?> row, IReadOnlyList<string> keyColumns)
    {
        var columns = row.Keys.ToList();
        var effectiveKeys = keyColumns.Count > 0 ? (IReadOnlyList<string>)keyColumns : [columns[0]];

        var valueList = string.Join(", ", columns.Select(c => FormatValue(row[c])));
        var columnList = string.Join(", ", columns.Select(c => $"[{c}]"));
        var onClause = string.Join(" AND ", effectiveKeys.Select(k => $"target.[{k}] = source.[{k}]"));
        var nonKeyColumns = columns.Where(c => !effectiveKeys.Contains(c)).ToList();

        var quotedTable = SqlQuoting.QuoteMssql(tableName);

        if (nonKeyColumns.Count == 0)
        {
            // Nessuna colonna da aggiornare — solo INSERT se non esiste
            return $"""
                MERGE INTO {quotedTable} AS target
                USING (VALUES ({valueList})) AS source ({columnList})
                ON {onClause}
                WHEN NOT MATCHED THEN
                    INSERT ({columnList}) VALUES ({valueList});
                """;
        }

        var updateList = string.Join(", ", nonKeyColumns.Select(c => $"target.[{c}] = source.[{c}]"));

        return $"""
            MERGE INTO {quotedTable} AS target
            USING (VALUES ({valueList})) AS source ({columnList})
            ON {onClause}
            WHEN MATCHED THEN
                UPDATE SET {updateList}
            WHEN NOT MATCHED THEN
                INSERT ({columnList}) VALUES ({valueList});
            """;
    }

    private static string BuildMysqlBatchUpsert(
        string tableName, List<Dictionary<string, object?>> batch, IReadOnlyList<string> keyColumns)
    {
        if (batch.Count == 0) return string.Empty;

        var columns = batch[0].Keys.ToList();
        var effectiveKeys = keyColumns.Count > 0 ? (IReadOnlyList<string>)keyColumns : [columns[0]];

        var quotedTable = SqlQuoting.QuoteMysql(tableName);
        var columnList = string.Join(", ", columns.Select(c => $"`{c}`"));
        var valueRows = batch.Select(row =>
            $"  ({string.Join(", ", columns.Select(c => FormatValue(row[c])))})");
        var allValues = string.Join($",{Environment.NewLine}", valueRows);

        var nonKeyColumns = columns.Where(c => !effectiveKeys.Contains(c)).ToList();

        // Se non ci sono colonne non-chiave, aggiorna la prima chiave su se stessa (no-op sintattico)
        var updateList = nonKeyColumns.Count > 0
            ? string.Join(", ", nonKeyColumns.Select(c => $"`{c}` = VALUES(`{c}`)"))
            : $"`{effectiveKeys[0]}` = VALUES(`{effectiveKeys[0]}`)";

        // Nota: VALUES(col) è deprecato in MySQL 8.0.20+ ma mantiene ampia compatibilità.
        // Per MySQL 8.0.19+: usare "AS _new ON DUPLICATE KEY UPDATE col = _new.col"
        return $"INSERT INTO {quotedTable} ({columnList}){Environment.NewLine}VALUES{Environment.NewLine}{allValues}{Environment.NewLine}ON DUPLICATE KEY UPDATE {updateList};";
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "NULL";

        return value switch
        {
            bool b => b ? "1" : "0",
            byte or short or int or long or float or double or decimal
                => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL",
            DateTime dt => $"'{dt:yyyy-MM-ddTHH:mm:ss}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-ddTHH:mm:ss}'",
            _ => $"'{value.ToString()!.Replace("'", "''")}'"
        };
    }
}
