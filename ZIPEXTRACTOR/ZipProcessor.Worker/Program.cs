using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ZipProcessor.Worker;
using ZipProcessor.Worker.Services;

var builder = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .WriteTo.Console())
    .ConfigureServices((ctx, services) =>
    {
        services.AddHostedService<Worker>();
        services.AddSingleton<IZipService, ZipService>();
        services.AddSingleton<IApiService, ApiService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IRequestJobService, RequestJobService>(); 
        services.AddHttpClient();
    });

await builder.Build().RunAsync();
