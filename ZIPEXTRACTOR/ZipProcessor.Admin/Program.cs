using Dapper;
using MySql.Data.MySqlClient;
using ZipProcessor.Admin.Models;
using ZipProcessor.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

//  Register MySQL connection
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddTransient<MySqlConnection>(_ => new MySqlConnection(connStr));


//  Register HttpClient factory
builder.Services.AddHttpClient();
// register HttpClient factory for controller probes/ajax


//  Register  Docker management service
builder.Services.AddSingleton<DockerService>();

//  Enable session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});
using (var conn = new MySqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    await conn.OpenAsync();
   // await SeedServicesAsync(conn, builder.Configuration);
}




var app = builder.Build();
app.UseRouting();
//app.UseCors("AllowGUI");   

app.UseStaticFiles();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();


static async Task SeedServicesAsync(MySqlConnection conn, IConfiguration config)
{
    var services = config.GetSection("Services").Get<List<ServiceDefinition>>() ?? new();
    foreach (var svc in services)
    {
        Console.WriteLine($"Loaded service: {svc.Name}, Inputs: {svc.Inputs?.Count ?? 0}");

        var exists = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM service_master WHERE name=@name", new { svc.Name });

        if (exists == 0)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO service_master
                    (Name, Type, ExePath, Image, DefaultPort, Inputs)
                VALUES (@Name, @Type, @ExePath, @Image, @DefaultPort, @Inputs)",
                new
                {
                    svc.Name,
                    svc.Type,
                    svc.ExePath,
                    svc.Image,
                    svc.DefaultPort,
                 
                    InputsJson = System.Text.Json.JsonSerializer.Serialize(svc.Inputs ?? new())
                });
        }
    }
}
