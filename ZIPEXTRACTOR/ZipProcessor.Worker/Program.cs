using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using ZipProcessor.Worker;
using ZipProcessor.Worker.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowGUI", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5183",   // Local dev
            "http://127.0.0.1:5183",   // Local IP
            "http://guiapp:5183"       // Docker GUI
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
// ------------------------------------------------------
// CONFIGURATION
// ------------------------------------------------------
builder.Host.ConfigureAppConfiguration((hostingContext, config) =>
{
    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    config.AddEnvironmentVariables();
    config.AddCommandLine(args);

    // Add in-memory collection for runtime updates (for API + worker settings only)
    config.AddInMemoryCollection(new Dictionary<string, string>());
});

var configuration = builder.Configuration;

// Bind options
builder.Services.Configure<WorkerSettings>(configuration.GetSection("WorkerSettings"));
builder.Services.Configure<ApiSettings>(configuration.GetSection("ApiSettings"));

// ------------------------------------------------------
// DATABASE CONNECTION (ALWAYS FROM appsettings.json)
// ------------------------------------------------------
builder.Services.AddTransient(provider =>
{
    var cs = configuration.GetConnectionString("DefaultConnection")
             ?? throw new InvalidOperationException("DefaultConnection missing");
    return new MySqlConnection(cs);
});

// ------------------------------------------------------
// BACKGROUND WORKER + STATE
// ------------------------------------------------------
builder.Services.AddSingleton<WorkerControlState>();
builder.Services.AddHostedService<WorkerService>();

// ------------------------------------------------------
// HEALTH CHECKS + LOGGING
// ------------------------------------------------------
var connStr = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
builder.Services.AddHealthChecks().AddMySql(connStr, name: "mysql");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ------------------------------------------------------
// HOST URL CONFIGURATION
// ------------------------------------------------------
var url = configuration["ApiSettings:WorkerUrl"] ?? "http://0.0.0.0:8080";
builder.WebHost.UseUrls(url);

var app = builder.Build();
app.UseRouting();
app.UseCors("AllowGUI");   

// ------------------------------------------------------
// MEMORY PROVIDER (for in-memory config update API)
// ------------------------------------------------------
var configRoot = (IConfigurationRoot)app.Configuration;
var memoryProvider = configRoot.Providers.OfType<MemoryConfigurationProvider>().FirstOrDefault()
    ?? throw new InvalidOperationException("MemoryConfigurationProvider not found");

// ------------------------------------------------------
// CONTROL STATE
// ------------------------------------------------------
var controlState = app.Services.GetRequiredService<WorkerControlState>();

// ------------------------------------------------------
// LOG STARTUP CONFIG
// ------------------------------------------------------
void LogStartupConfig(ILogger logger)
{
    var cfg = app.Configuration;
    logger.LogInformation("Resolved configuration at startup:");
    logger.LogInformation("  ApiSettings:BaseUrl={BaseUrl}", cfg["ApiSettings:BaseUrl"]);
    logger.LogInformation("  ApiSettings:WorkerUrl={WorkerUrl}", cfg["ApiSettings:WorkerUrl"]);
    logger.LogInformation("  WorkerSettings:BatchSize={BatchSize}", cfg["WorkerSettings:BatchSize"]);
    logger.LogInformation("  WorkerSettings:MaxParallel={MaxParallel}", cfg["WorkerSettings:MaxParallel"]);
    logger.LogInformation("  WorkerSettings:DelayMilliseconds={Delay}", cfg["WorkerSettings:DelayMilliseconds"]);
}
LogStartupConfig(app.Logger);

// ------------------------------------------------------
// CONTROL ENDPOINTS
// ------------------------------------------------------
app.MapGet("/control/status",  () =>
{
 //   await workerService.UpdateWorkersTableHeartbeatAsync();
    return Results.Ok(new
    {
        paused = controlState.IsPaused,
        stopped = controlState.IsStopped,
        message = controlState.IsStopped ? "Worker stopped" :
                  controlState.IsPaused ? "Worker paused" : "Worker active"
    });
});


app.MapPost("/control/pause", () =>
{
    controlState.Pause();
    return Results.Ok(new { message = "Worker paused" });
});

app.MapPost("/control/resume", () =>
{
    controlState.Resume();
    return Results.Ok(new { message = "Worker resumed" });
});

app.MapPost("/control/stop", () =>
{
    controlState.Stop();
    return Results.Ok(new { message = "Worker stopped (no more processing)" });
});

app.MapPost("/control/start", () =>
{
    controlState.Start();
    return Results.Ok(new { message = "Worker started (processing resumed)" });
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", time = DateTime.UtcNow }));

// ------------------------------------------------------
// UPDATE CONFIG (ONLY API + WORKER SETTINGS)
// ------------------------------------------------------
app.MapPost("/control/update", ([FromBody] UpdateConfigDto dto, IConfiguration config) =>
{
    var cfg = config;
    var configRoot = (IConfigurationRoot)config;

    // old values
    var oldApi = cfg["ApiSettings:BaseUrl"] ?? "<missing>";
    var oldBatch = cfg["WorkerSettings:BatchSize"] ?? "<missing>";
    var oldParallel = cfg["WorkerSettings:MaxParallel"] ?? "<missing>";
    var oldDelay = cfg["WorkerSettings:DelayMilliseconds"] ?? "<missing>";

    // update values directly
    if (!string.IsNullOrWhiteSpace(dto.ApiBaseUrl))
        configRoot["ApiSettings:BaseUrl"] = dto.ApiBaseUrl;

    if (dto.BatchSize.HasValue && dto.BatchSize.Value > 0)
        configRoot["WorkerSettings:BatchSize"] = dto.BatchSize.Value.ToString();

    if (dto.MaxParallel.HasValue && dto.MaxParallel.Value > 0)
        configRoot["WorkerSettings:MaxParallel"] = dto.MaxParallel.Value.ToString();

    if (dto.DelayMilliseconds.HasValue && dto.DelayMilliseconds.Value > 0)
        configRoot["WorkerSettings:DelayMilliseconds"] = dto.DelayMilliseconds.Value.ToString();

    // reload
    configRoot.Reload();

    // read new values
    var newApi = cfg["ApiSettings:BaseUrl"];
    var newBatch = cfg["WorkerSettings:BatchSize"];
    var newParallel = cfg["WorkerSettings:MaxParallel"];
    var newDelay = cfg["WorkerSettings:DelayMilliseconds"];

    app.Logger.LogInformation("Configuration update via API:");
    app.Logger.LogInformation("  ApiSettings:BaseUrl: {Old} => {New}", oldApi, newApi);
    app.Logger.LogInformation("  WorkerSettings:BatchSize: {Old} => {New}", oldBatch, newBatch);
    app.Logger.LogInformation("  WorkerSettings:MaxParallel: {Old} => {New}", oldParallel, newParallel);
    app.Logger.LogInformation("  WorkerSettings:DelayMilliseconds: {Old} => {New}", oldDelay, newDelay);
    app.Logger.LogInformation("Configuration reloaded. New settings active immediately (except WorkerUrl, which is fixed).");

    return Results.Ok(new
    {
        message = "Configuration updated in memory",
        before = new { apiBaseUrl = oldApi, batchSize = oldBatch, maxParallel = oldParallel, delayMilliseconds = oldDelay },
        after = new { apiBaseUrl = newApi, batchSize = newBatch, maxParallel = newParallel, delayMilliseconds = newDelay }
    });
});

// ------------------------------------------------------
// CURRENT CONFIGURATION ENDPOINT
// ------------------------------------------------------
app.MapGet("/control/current", (IOptionsMonitor<ApiSettings> apiOpts, IOptionsMonitor<WorkerSettings> workerOpts) =>
{
    var api = apiOpts.CurrentValue;
    var ws = workerOpts.CurrentValue;

    var response = new
    {
        message = "Current live configuration values",
        updatedAt = DateTime.UtcNow.ToString("u"),
        apiSettings = new
        {
            baseUrl = api.BaseUrl ?? "<not set>"
        },
        workerSettings = new
        {
            batchSize = ws.BatchSize,
            maxParallel = ws.MaxParallel,
            delayMilliseconds = ws.DelayMilliseconds
        }
    };

    return Results.Ok(response);
});


// ------------------------------------------------------
// RUN
// ------------------------------------------------------
app.Logger.LogInformation("Worker + Web Host running at {Url}", url);
app.Run();

record UpdateConfigDto(
    string? ApiBaseUrl,
   
    int? BatchSize,
    int? MaxParallel,
    int? DelayMilliseconds
);
