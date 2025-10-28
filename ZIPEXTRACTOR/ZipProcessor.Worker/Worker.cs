using Microsoft.Extensions.Hosting;
using ZipProcessor.Worker.Services;
using ZipProcessor.Worker.Models;

namespace ZipProcessor.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IRequestJobService _jobService;
    private readonly IZipService _zipService;
    private readonly IApiService _apiService;
    private readonly IDatabaseService _dbService;
    private readonly IConfiguration _config;

    public Worker(
        ILogger<Worker> logger,
        IRequestJobService jobService,
        IZipService zipService,
        IApiService apiService,
        IDatabaseService dbService,
        IConfiguration config)
    {
        _logger = logger;
        _jobService = jobService;
        _zipService = zipService;
        _apiService = apiService;
        _dbService = dbService;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started...");

        while (!stoppingToken.IsCancellationRequested)
        {
            var jobs = await _jobService.GetPendingJobsAsync();

            if (!jobs.Any())
            {

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            _logger.LogInformation("Found {Count} pending jobs", jobs.Count());

            foreach (var job in jobs)
            {
                _ = ProcessJobAsync(job.Id, job.RequestId, job.ZipPath, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessJobAsync(long jobId, string requestId, string zipPath, CancellationToken ct)
    {
        try
        {
            await _jobService.UpdateStatusAsync(jobId, "Processing");

            var extractFolder = _config["Paths:ExtractFolder"] ?? "data/extracted";
            var extractedFiles = await _zipService.ExtractAsync(zipPath, extractFolder, ct);

            _logger.LogInformation("[{Req}] Extracted {Count} files from {Zip}", requestId, extractedFiles.Count, zipPath);

            var results = new List<ApiResponse>();

            foreach (var file in extractedFiles)
            {
                var response = await _apiService.SendFilePathAsync(file, ct);
                results.Add(response);

                if (results.Count >= 500)
                {
                    await _dbService.SaveBatchAsync(results, ct);
                    results.Clear();
                }
            }

            if (results.Count > 0)
                await _dbService.SaveBatchAsync(results, ct);

            await _jobService.UpdateStatusAsync(jobId, "Completed");
            _logger.LogInformation("[{Req}] Job completed successfully", requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Req}] Job failed", requestId);
            await _jobService.UpdateStatusAsync(jobId, "Failed");
        }
    }
}
