using System.Diagnostics;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Serilog;

namespace RestoreSQL.Services;

public class RestoreService(string connectionString, string databaseType)
{
    // ── Constraint management ─────────────────────────────────────────────────
    public async Task DisableConstraintsAsync(CancellationToken ct = default)
    {
        switch (databaseType.ToLowerInvariant())
        {
            case "mssql":
                await using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync(ct);
                    await using var cmd = new SqlCommand(
                        "EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'", conn)
                    { CommandTimeout = 120 };
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                break;
            // MySQL: gestito per sessione dentro ExecuteMySqlAsync
        }
    }

    public async Task EnableConstraintsAsync(CancellationToken ct = default)
    {
        switch (databaseType.ToLowerInvariant())
        {
            case "mssql":
                await using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync(ct);
                    await using var cmd = new SqlCommand(
                        "EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'", conn)
                    { CommandTimeout = 300 };
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                break;
            // MySQL: gestito per sessione dentro ExecuteMySqlAsync
        }
    }

    public async Task<(int statementsOk, int statementsSkipped, string? error)> ExecuteFileAsync(
        string filePath, CancellationToken ct)
    {
        var statements = ParseStatements(filePath);
        if (statements.Count == 0)
            return (0, 0, null);

        var sw = Stopwatch.StartNew();

        return databaseType.ToLowerInvariant() switch
        {
            "mysql"  => await ExecuteMySqlAsync(statements, ct),
            "mssql"  => await ExecuteMsSqlAsync(statements, ct),
            _ => throw new InvalidOperationException($"DatabaseType non supportato: '{databaseType}'")
        };
    }

    // ── Statement parser ─────────────────────────────────────────────────────
    // Accumula le righe del file; ogni volta che una riga (trimmed) termina con ';'
    // il blocco accumulato viene chiuso come statement.
    // Le righe di commento (-- ...) vengono ignorate.
    private static List<string> ParseStatements(string filePath)
    {
        var statements = new List<string>();
        var current    = new System.Text.StringBuilder();

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.TrimEnd();

            // righe di commento puro
            if (line.TrimStart().StartsWith("--"))
                continue;

            // righe vuote tra statement
            if (line.Length == 0 && current.Length == 0)
                continue;

            current.AppendLine(line);

            if (line.TrimEnd().EndsWith(';'))
            {
                var stmt = current.ToString().Trim();
                if (stmt.Length > 0)
                    statements.Add(stmt);
                current.Clear();
            }
        }

        // eventuale statement senza ; finale
        var tail = current.ToString().Trim();
        if (tail.Length > 0)
            statements.Add(tail);

        return statements;
    }

    // ── MySQL ────────────────────────────────────────────────────────────────
    private async Task<(int ok, int skipped, string? error)> ExecuteMySqlAsync(
        List<string> statements, CancellationToken ct)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(ct);
        // Disabilita FK check per questa sessione (ogni file ha la propria connessione)
        await using (var fkCmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 0;", conn))
            await fkCmd.ExecuteNonQueryAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        int ok = 0;
        try
        {
            foreach (var stmt in statements)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = new MySqlCommand(stmt, conn, tx)
                {
                    CommandTimeout = 300
                };
                await cmd.ExecuteNonQueryAsync(ct);
                ok++;
            }
            await tx.CommitAsync(ct);
            return (ok, 0, null);
        }
        catch (OperationCanceledException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return (ok, statements.Count - ok, ex.Message);
        }
    }

    // ── MSSQL ────────────────────────────────────────────────────────────────
    private async Task<(int ok, int skipped, string? error)> ExecuteMsSqlAsync(
        List<string> statements, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        int ok = 0;
        try
        {
            // Imposta DATEFORMAT ymd per garantire la corretta interpretazione delle date ISO
            // (necessario su SQL Server con locale italiana che usa dmy di default)
            await using (var initCmd = new SqlCommand("SET DATEFORMAT ymd;", conn, tx))
                await initCmd.ExecuteNonQueryAsync(ct);

            foreach (var stmt in statements)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = new SqlCommand(stmt, conn, tx)
                {
                    CommandTimeout = 300
                };
                await cmd.ExecuteNonQueryAsync(ct);
                ok++;
            }
            tx.Commit();
            return (ok, 0, null);
        }
        catch (OperationCanceledException)
        {
            tx.Rollback();
            throw;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (ok, statements.Count - ok, ex.Message);
        }
    }
}
