using System.IO.Compression;

namespace ZipProcessor.Worker.Services;

public interface IZipService
{
    Task<List<string>> ExtractAsync(string zipPath, string extractRoot, CancellationToken ct);
}

public sealed class ZipService : IZipService
{
    public Task<List<string>> ExtractAsync(string zipPath, string extractRoot, CancellationToken ct)
    {
        var target = Path.Combine(extractRoot, Path.GetFileNameWithoutExtension(zipPath));
        Directory.CreateDirectory(target);
        ZipFile.ExtractToDirectory(zipPath, target, overwriteFiles: true);
        var files = Directory.GetFiles(target, "*", SearchOption.AllDirectories).ToList();
        return Task.FromResult(files);
    }
}
