using ZipProcessing.WebApi.Models;
using ZipProcessing.WebApi.Services;
using Microsoft.OpenApi.Models; // Add this using directive

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<JobService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

//custom method api






app.MapPost("/api/process", async (JobService service, ApiRequest req) =>
{
    //  var id = await service.AddJobAsync(req);

    var fiveDigitNumber = RandomHelpers.GenerateFiveDigitNumber();        // int 10000..99999
    var fiveDigitString = RandomHelpers.GenerateFiveDigitString();        // "01234" or "54321"
    var alpha5Crypto = RandomHelpers.GenerateAlphanumeric(5, useCrypto: true);  // crypto-safe 5-char
    var alpha5Fast = RandomHelpers.GenerateAlphanumeric(5, useCrypto: false);  // faster non-crypto
    return Results.Ok(new {request_id = req.RequestId, 
        file_name = req.FileName,
        file_path = $"c://home/file_{fiveDigitNumber}_{fiveDigitString}_{alpha5Crypto}.pdf)",
        status = 1  , 
        message = "success" });
});


// POST /api/jobs → submit new ZIP request
app.MapPost("/api/jobs", async (JobService service, JobRequest req) =>
{
    var id = await service.AddJobAsync(req);
    return Results.Ok(new { message = "Job queued", id });
});

// GET /api/jobs → list all
app.MapGet("/api/jobs", async (JobService service) =>
    Results.Ok(await service.GetAllJobsAsync()));

// GET /api/jobs/{requestId} → get job status
app.MapGet("/api/jobs/{requestId}", async (JobService service, string requestId) =>
{
    var job = await service.GetJobAsync(requestId);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.Run();
