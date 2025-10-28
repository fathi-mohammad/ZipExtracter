using System.Net.Http.Json;
using ZipProcessor.Worker.Models;

namespace ZipProcessor.Worker.Services;

public interface IApiService
{
    Task<ApiResponse> SendFilePathAsync(string localPath, CancellationToken ct);
}

public sealed class ApiService : IApiService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;

    public ApiService(IHttpClientFactory httpFactory, IConfiguration cfg)
    {
        _httpFactory = httpFactory;
        _cfg = cfg;
    }

    public async Task<ApiResponse> SendFilePathAsync(string localPath, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient();
        var url = _cfg["Api:BaseUrl"]!;
        var body = new { file_path = localPath };

        // For now: if the API isn’t running, fake a response
        try
        {
            using var resp = await client.PostAsJsonAsync(url, body, ct);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<object>(cancellationToken: ct);
            return new ApiResponse
            {
                FileName = Path.GetFileName(localPath),
                LocalPath = localPath,
                Status = "OK",
                Data = data
            };
        }
        catch
        {
            return new ApiResponse
            {
                FileName = Path.GetFileName(localPath),
                LocalPath = localPath,
                Status = "ERROR",
                Data = null
            };
        }
    }
}
