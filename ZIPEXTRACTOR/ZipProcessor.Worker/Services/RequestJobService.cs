using Dapper;
using MySqlConnector;
using ZipProcessor.Worker.Models;

namespace ZipProcessor.Worker.Services;

public interface IRequestJobService
{
    Task<IEnumerable<(long Id, string RequestId, string ZipPath)>> GetPendingJobsAsync();
    Task UpdateStatusAsync(long id, string status);
}

public class RequestJobService : IRequestJobService
{
    private readonly string _connStr;
    private readonly ILogger<RequestJobService> _logger;

    public RequestJobService(IConfiguration config, ILogger<RequestJobService> logger)
    {
        _connStr = config.GetConnectionString("Default")!;
        _logger = logger;
    }

    public async Task<IEnumerable<(long Id, string RequestId, string ZipPath)>> GetPendingJobsAsync()
    {
        await using var conn = new MySqlConnection(_connStr);
        const string sql = @"SELECT id, request_id AS RequestId, zip_path AS ZipPath 
                         FROM request_jobs WHERE status = 'Pending'";
        var jobs = await conn.QueryAsync<JobRow>(sql);

        return jobs
            .Where(j => !string.IsNullOrEmpty(j.ZipPath))
            .Select(j => (j.Id, j.RequestId ?? string.Empty, j.ZipPath ?? string.Empty));
    }

    public async Task UpdateStatusAsync(long id, string status)
    {
        await using var conn = new MySqlConnection(_connStr);
        var sql = "UPDATE request_jobs SET status=@status, updated_at=NOW() WHERE id=@id";
        await conn.ExecuteAsync(sql, new { id, status });
        _logger.LogInformation("Updated job {Id} -> {Status}", id, status);
    }
}
