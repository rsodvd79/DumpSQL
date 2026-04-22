namespace DumpSQL.Services;

public interface IDatabaseService
{
    IAsyncEnumerable<Dictionary<string, object?>> QueryTableAsync(
        string tableName,
        string referenceField,
        string referenceValue,
        CancellationToken cancellationToken = default);
}
