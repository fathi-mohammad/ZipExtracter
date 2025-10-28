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
app.MapPost("/api/process", async (JobService service, JobRequest req) =>
{
  //  var id = await service.AddJobAsync(req);
    return Results.Ok(new { message = "success" , status=1 , path ="c://home/file_1000_2000_3000.pdf)" });
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
