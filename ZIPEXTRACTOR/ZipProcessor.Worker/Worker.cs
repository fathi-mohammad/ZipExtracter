using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ZipProcessor.Worker.Models;

namespace ZipProcessor.Worker;

public class WorkerService : BackgroundService
{
    private readonly ILogger<WorkerService> _logger;
    private readonly IServiceProvider _services;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly WorkerControlState _controlState;
    private WorkerSettings _workerSettings;
    private ApiSettings _apiSettings;
    private readonly string _workerId = Guid.NewGuid().ToString("N");

    public WorkerService(
        ILogger<WorkerService> logger,
        IServiceProvider services,
        IOptionsMonitor<WorkerSettings> workerOptions,
        IOptionsMonitor<ApiSettings> apiOptions,
        IConfiguration config,
        WorkerControlState controlState)
    {
        _logger = logger;
        _services = services;
        _httpClient = new HttpClient();
        _config = config;
        _controlState = controlState;

        _workerSettings = workerOptions.CurrentValue;
        _apiSettings = apiOptions.CurrentValue;

        _logger.LogInformation(
            "WorkerService initial config: BatchSize={Batch}, MaxParallel={Parallel}, DelayMs={Delay}, ApiBase={ApiBase}",
            _workerSettings.BatchSize,
            _workerSettings.MaxParallel,
            _workerSettings.DelayMilliseconds,
            _apiSettings.BaseUrl
        );

        // Subscribe to runtime changes (via /control/update)
        workerOptions.OnChange(ws =>
        {
            var old = _workerSettings;
            _workerSettings = ws;
            _logger.LogInformation(
                "WorkerSettings updated: BatchSize={Batch}, MaxParallel={Parallel}, DelayMs={Delay} (was {OldBatch}, {OldParallel}, {OldDelay})",
                ws.BatchSize, ws.MaxParallel, ws.DelayMilliseconds,
                old.BatchSize, old.MaxParallel, old.DelayMilliseconds
            );
        });

        apiOptions.OnChange(api =>
        {
            var oldBase = _apiSettings.BaseUrl;
            _apiSettings = api;
            _logger.LogInformation("ApiSettings updated: BaseUrl changed {Old} => {New}", oldBase, api.BaseUrl);
        });
        AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
        {
            try
            {
                await UpdateWorkerStatusOnExitAsync("Exited");
            }
            catch { /* ignore, since process is shutting down */ }
        };
    }

    private MySqlConnection CreateMainConnection() =>
        new(_config.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("DefaultConnection missing"));

    private MySqlConnection CreateProcedureConnection() =>
        new(_config.GetConnectionString("ProcedureDbConnection") ??
            throw new InvalidOperationException("ProcedureDbConnection missing"));
    private async Task HeartbeatLoopAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(15)); // update interval
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await UpdateWorkersTableHeartbeatAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update workers heartbeat.");
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started with BatchSize={Batch}, MaxParallel={Parallel}, Delay={Delay}ms",
            _workerSettings.BatchSize, _workerSettings.MaxParallel, _workerSettings.DelayMilliseconds);

        await RegisterWorkerAsync();

        await RegisterWorkerInWorkersTableAsync();
        _ = Task.Run(() => HeartbeatLoopAsync(stoppingToken), stoppingToken);


        while (!stoppingToken.IsCancellationRequested)
        {
            if (_controlState.IsStopped)
            {
                _logger.LogWarning("Worker stopped by control command.");
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            if (_controlState.IsPaused)
            {
                _logger.LogInformation("Worker paused. Waiting to resume...");
                await Task.Delay(3000, stoppingToken);
                continue;
            }

            try
            {
                await ProcessBatchAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch processing.");
            }

            await Task.Delay(_workerSettings.DelayMilliseconds, stoppingToken);
        }
    }
    private static string GetStatusString(WorkerControlState state)
    {
        if (state.IsStopped) return "Stopped";
        if (state.IsPaused) return "Paused";
        return "Active";
    }
    private async Task RegisterWorkerInWorkersTableAsync()
    {
        using var conn = CreateMainConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
        INSERT INTO workers (worker_id, container_name, api_url, status, last_ping, last_heartbeat, response_time_ms, remarks)
        VALUES (@worker_id, @container_name, @api_url, @status, NOW(), NOW(), 0, 'Initialized')
        ON DUPLICATE KEY UPDATE
            container_name = VALUES(container_name),
            api_url = VALUES(api_url),
            status = VALUES(status),
            last_heartbeat = NOW(),
            remarks = 'Re-registered';",
            new
            {
                worker_id = _workerId,
                container_name = Environment.MachineName,
                api_url = _config["ApiSettings:WorkerUrl"].Replace("0.0.0.0", "localhost"),
              //  var workerUrl = _config["ApiSettings:WorkerUrl"].Replace("0.0.0.0", "localhost");

        status = GetStatusString(_controlState)
            });

        _logger.LogInformation("Worker {WorkerId} registered in workers table.", _workerId);
    }
    public async Task UpdateWorkersTableHeartbeatAsync()
    {
        using var conn = CreateMainConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
        UPDATE workers
        SET status = @status,
            last_heartbeat = NOW(),
            remarks = 'Alive'
        WHERE worker_id = @worker_id;",
            new
            {
                worker_id = _workerId,
                status = GetStatusString(_controlState)
            });
    }

    private async Task RegisterWorkerAsync()
    {
        using var conn = CreateMainConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO health (worker_id, machine_name, process_name, start_time, last_heartbeat)
            VALUES (@worker_id, @machine_name, @process_name, NOW(), NOW())
            ON DUPLICATE KEY UPDATE start_time = NOW(), last_heartbeat = NOW();",
            new
            {
                worker_id = _workerId,
                machine_name = Environment.MachineName,
                process_name = AppDomain.CurrentDomain.FriendlyName
            });

        _logger.LogInformation("Worker {WorkerId} registered in health table.", _workerId);
    }

    private async Task UpdateHeartbeatAsync()
    {
        using var conn = CreateMainConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"UPDATE health SET last_heartbeat = NOW() WHERE worker_id = @worker_id;",
            new { worker_id = _workerId });
    }

    private async Task ProcessBatchAsync()
    {
        await UpdateHeartbeatAsync();

        using var scope = _services.CreateScope();
        var conn = scope.ServiceProvider.GetRequiredService<MySqlConnection>();
        await conn.OpenAsync();

        using var tran = await conn.BeginTransactionAsync();

        var files = (await conn.QueryAsync<FileRecord>(@"
            SELECT id, request_id, zip_id, file_id, file_path, status
            FROM request_jobs
            WHERE status = 'Pending'
            ORDER BY id
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED;",
            new { BatchSize = _workerSettings.BatchSize }, tran)).ToList();

        if (files.Count == 0)
        {
            await tran.RollbackAsync();
            _logger.LogInformation("No pending jobs found.");
            return;
        }

        await conn.ExecuteAsync(
            "UPDATE request_jobs SET status='Processing', processing_at=NOW() WHERE id IN @Ids;",
            new { Ids = files.Select(f => f.id) }, tran);

        await tran.CommitAsync();

        _logger.LogInformation("Claimed {Count} pending jobs for processing.", files.Count);

        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = _workerSettings.MaxParallel },
            async (file, _) => await HandleFileAsync(file));
    }

    private async Task HandleFileAsync(FileRecord file)
    {
        var apiResult = new ApiResult
        {
            request_id = file.request_id,
            zip_id = file.zip_id,
            file_id = file.file_id,
            file_name = file.file_path
        };

        try
        {
            var requestJson = JsonSerializer.Serialize(new { file.request_id, file.file_path });

            HttpResponseMessage? response = null;
            string apiResponseBody = "";

            try
            {
                response = await _httpClient.PostAsync(
                    _apiSettings.BaseUrl,
                    new StringContent(requestJson, Encoding.UTF8, "application/json"));

                apiResponseBody = await response.Content.ReadAsStringAsync();
                apiResult.API_StatusCode = (int)response.StatusCode;
                apiResult.API_response = apiResponseBody;
            }
            catch (Exception ex)
            {
                apiResult.API_StatusCode = -1;
                apiResult.API_response = $"API ERROR: {ex.Message}";
                await LogErrorAndMarkFailed(file.id, apiResult, "API Failure");
                return;
            }

            if (response != null && response.IsSuccessStatusCode)
            {
                try
                {
                    using var procConn = CreateProcedureConnection();
                    await procConn.OpenAsync();

                    var (procResult, rawProcJson) = await CallStoredProcedureAsync(procConn, apiResponseBody);
                    apiResult.procedure_response = rawProcJson;

                    using var conn = CreateMainConnection();
                    await conn.OpenAsync();
                    await conn.ExecuteAsync(
                        "UPDATE request_jobs SET status='Completed', completed_at=NOW() WHERE id=@Id",
                        new { Id = file.id });
                }
                catch (Exception spEx)
                {
                    apiResult.procedure_response = $"SP ERROR: {spEx.Message}";
                    await LogErrorAndMarkFailed(file.id, apiResult, "SP Failure");
                }
            }
            else
            {
                apiResult.procedure_response = "API returned non-success status";
                await LogErrorAndMarkFailed(file.id, apiResult, "API NonSuccess");
            }

            await LogResultAsync(apiResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing file {File}", file.file_path);
            apiResult.API_response = $"Unhandled error: {ex.Message}";
            await LogErrorAndMarkFailed(file.id, apiResult, "Unhandled");
        }
    }

    private async Task LogResultAsync(ApiResult result)
    {
        using var conn = CreateMainConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO api_results 
            (request_id, zip_id, file_id, file_name, API_StatusCode, API_response, procedure_response, API_calltime, procedure_calltime)
            VALUES (@request_id, @zip_id, @file_id, @file_name, @API_StatusCode, @API_response, @procedure_response, NOW(), NOW())", result);
    }

    private async Task LogErrorAndMarkFailed(int fileId, ApiResult result, string reason)
    {
        await LogResultAsync(result);

        using var conn = CreateMainConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE request_jobs SET status='Error' WHERE id=@Id;", new { Id = fileId });

        _logger.LogWarning("File {FileId} marked as Error due to {Reason}", fileId, reason);
    }

    private async Task<(ProcResult parsed, string rawJson)> CallStoredProcedureAsync(MySqlConnection conn, string json)
    {
        using var cmd = new MySqlCommand("InsertDataFromApiResp", conn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@jsonData", json);

        using var reader = await cmd.ExecuteReaderAsync();
        string? procResponse = null;

        if (await reader.ReadAsync())
            procResponse = reader["response"]?.ToString();

        if (string.IsNullOrEmpty(procResponse))
            return (new ProcResult { status = "0", message = "No response from procedure" }, "{}");

        try
        {
            var result = JsonSerializer.Deserialize<ProcResult>(procResponse);
            return (result ?? new ProcResult { status = "0", message = "Deserialize failed" }, procResponse);
        }
        catch (Exception ex)
        {
            return (new ProcResult { status = "0", message = $"Invalid JSON: {ex.Message}" }, procResponse);
        }
    }
    private async Task UpdateWorkerStatusOnExitAsync(string exitStatus)
    {
        using var conn = CreateMainConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
        UPDATE workers
        SET status = @status,
            remarks = 'Service stopped gracefully',
            last_heartbeat = NOW()
        WHERE worker_id = @worker_id;",
            new
            {
                worker_id = _workerId,
                status = exitStatus
            });

        _logger.LogInformation("Worker {WorkerId} marked as {Status} in database.", _workerId, exitStatus);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("WorkerService stopping...");

        try
        {
            await UpdateWorkerStatusOnExitAsync("Stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update worker status on shutdown.");
        }

        await base.StopAsync(cancellationToken);
    }
    
}
