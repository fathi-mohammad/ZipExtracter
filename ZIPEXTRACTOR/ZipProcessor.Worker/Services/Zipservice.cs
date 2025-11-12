using System.IO.Compression;

namespace ZipProcessor.Worker.Services;

public interface IZipService
{
    Task<List<string>> ExtractAsync(string FilePath, string extractRoot, CancellationToken ct);
}

public sealed class ZipService : IZipService
{
    public Task<List<string>> ExtractAsync(string FilePath, string extractRoot, CancellationToken ct)
    {
        var target = Path.Combine(extractRoot, Path.GetFileNameWithoutExtension(FilePath));
        Directory.CreateDirectory(target);
        ZipFile.ExtractToDirectory(FilePath, target, overwriteFiles: true);
        var files = Directory.GetFiles(target, "*", SearchOption.AllDirectories).ToList();
        return Task.FromResult(files);
    }
}
