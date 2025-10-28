using System.Text.Json;
using Dapper;
using MySqlConnector;
using ZipProcessor.Worker.Models;

namespace ZipProcessor.Worker.Services;

public interface IDatabaseService
{
    Task SaveBatchAsync(IReadOnlyList<ApiResponse> batch, CancellationToken ct);
}

public sealed class DatabaseService : IDatabaseService
{
    private readonly string _cs;
    private readonly ILogger<DatabaseService> _log;

    public DatabaseService(IConfiguration cfg, ILogger<DatabaseService> log)
    {
        _cs = cfg.GetConnectionString("Default")!;
        _log = log;
    }

    public async Task SaveBatchAsync(IReadOnlyList<ApiResponse> batch, CancellationToken ct)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct);

        const string sql = @"
INSERT INTO api_results (file_name, local_path, status, result_json)
VALUES (@FileName, @LocalPath, @Status, @Json);";

        using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var r in batch)
        {
            var json = r.Data is null ? null : JsonSerializer.Serialize(r.Data);
            await conn.ExecuteAsync(new CommandDefinition(
                sql,
                new { r.FileName, r.LocalPath, r.Status, Json = json },
                transaction: (MySqlTransaction)tx,
                cancellationToken: ct));
        }
        await tx.CommitAsync(ct);

        _log.LogInformation("Saved batch: {Count}", batch.Count);
    }
}
