namespace ZipProcessor.Worker.Models;

public sealed class ApiResponse
{
    public required string FileName { get; init; }
    public required string LocalPath { get; init; }
    public required string Status { get; init; }   // e.g., "OK" / "ERROR"
    public object? Data { get; init; }             // raw API data (we’ll JSON it)
}

public class JobRow
{
    public long Id { get; set; }
    public string? RequestId { get; set; }
    public string? FilePath { get; set; }
}

