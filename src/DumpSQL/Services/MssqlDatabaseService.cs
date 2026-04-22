using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Serilog;

namespace DumpSQL.Services;

public class MssqlDatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public MssqlDatabaseService(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async IAsyncEnumerable<Dictionary<string, object?>> QueryTableAsync(
        string tableName,
        string referenceField,
        string referenceValue,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var quotedTable = SqlQuoting.QuoteMssql(tableName);
        var quotedField = SqlQuoting.QuoteMssql(referenceField);
        var sql = $"SELECT * FROM {quotedTable} WHERE {quotedField} > @refValue";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@refValue", referenceValue);

        Log.Debug("MSSQL: {Sql} | refValue={RefValue}", sql, referenceValue);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            yield return row;
        }
    }
}
