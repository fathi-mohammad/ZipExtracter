using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace WorkerService1
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    
                    _logger.LogInformation("Worker1 running at: {time}", DateTimeOffset.Now);
                }
                await Task.Delay(1000, stoppingToken);
            }
        }
        private void myfun() {


            string folderPath = "D:\\data\\zips\\35MBZIP";
           
            var sourceFile = Path.Combine(folderPath, "TEST.pdf");
            copyfiles(sourceFile, 99);
        }

        private void copyfiles(string filePath = "", int copies = 1000000)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogWarning("No file path provided to copyfiles.");
                return;
            }

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found: {file}", filePath);
                return;
            }

            if (copies <= 0)
            {
                _logger.LogWarning("Copies must be greater than 0. Received: {copies}", copies);
                return;
            }

            var directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("File must have a directory.");
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath);

            for (int i = 1; i <= copies; i++)
            {
                try
                {
                    string newName = $"{baseName}_{i}{ext}";
                    string newPath = Path.Combine(directory, newName);

                    // If name collides, append a small numeric suffix until unique
                    int attempt = 0;
                    while (File.Exists(newPath))
                    {
                        attempt++;
                        newName = $"{baseName}{i}_{attempt}{ext}";
                        newPath = Path.Combine(directory, newName);

                        // Safety break to avoid infinite loops
                        if (attempt > 1000)
                        {
                            _logger.LogError("Too many name collisions while creating copy {i} of {file}", i, filePath);
                            break;
                        }
                    }

                    File.Copy(filePath, newPath);
                    _logger.LogInformation("Copied {source} -> {dest}", Path.GetFileName(filePath), newName);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SystemException)
                {
                    _logger.LogError(ex, "Failed to copy file on iteration {i} for source {file}", i, filePath);
                }
            }

            _logger.LogInformation("Completed creating {count} copies of {file}", copies, Path.GetFileName(filePath));
        }

        // to rename the pdf files in a folder to test_1.pdf, test_2.pdf, ...
        private void RenamePdfFiles(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                _logger.LogWarning("Folder not found: {path}", folderPath);
                return;
            }

            var pdfFiles = Directory.GetFiles(folderPath, "*.pdf")
                                    .OrderBy(f => f)
                                    .ToList();

            int counter = 1;
            foreach (var filePath in pdfFiles)
            {
                var directory = Path.GetDirectoryName(filePath)!;
                var newFileName = $"test_{counter}.pdf";
                var newPath = Path.Combine(directory, newFileName);

                // Skip if same name already exists
                if (File.Exists(newPath))
                {
                    _logger.LogWarning("File already exists: {file}", newPath);
                    counter++;
                    continue;
                }

                File.Move(filePath, newPath);
                _logger.LogInformation("Renamed {old} -> {new}", Path.GetFileName(filePath), newFileName);

                counter++;
            }

            _logger.LogInformation("Renaming completed. Total files processed: {count}", pdfFiles.Count);
        }
    }
}
