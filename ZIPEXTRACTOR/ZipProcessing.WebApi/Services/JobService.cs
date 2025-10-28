using Dapper;
using MySqlConnector;
using ZipProcessing.WebApi.Models;

namespace ZipProcessing.WebApi.Services;

public class JobService
{
    private readonly string _connStr;
    public JobService(IConfiguration config) =>
        _connStr = config.GetConnectionString("Default")!;

    public async Task<long> AddJobAsync(JobRequest req)
    {
        const string sql = @"INSERT INTO request_jobs (request_id, zip_path, status, created_at, updated_at)
                             VALUES (@request_id, @zip_path, 'Pending', NOW(), NOW());";
        await using var conn = new MySqlConnection(_connStr);
        await conn.ExecuteAsync(sql, new { request_id = req.RequestId, zip_path = req.ZipPath });
        return await conn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID();");
    }

    public async Task<IEnumerable<dynamic>> GetAllJobsAsync()
    {
        await using var conn = new MySqlConnection(_connStr);
        return await conn.QueryAsync("SELECT * FROM request_jobs ORDER BY created_at DESC");
    }

    public async Task<dynamic?> GetJobAsync(string requestId)
    {
        await using var conn = new MySqlConnection(_connStr);
        return await conn.QueryFirstOrDefaultAsync("SELECT * FROM request_jobs WHERE request_id = @requestId",
            new { requestId });
    }
}
