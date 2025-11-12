using Dapper;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace ZipProcessor.Admin.Services;

public class DockerService
{
    private readonly MySqlConnection _conn;
    private readonly DockerClient? _docker;
    private readonly ILogger<DockerService> _logger;
    private readonly string _defaultWorkerExePath;
    private readonly string _defaultWorkerImage;
    private readonly int _maxExeWorkers;
    private readonly ConcurrentDictionary<string, (Process Process, DateTime Started)> _startedExe = new();

    public DockerService(MySqlConnection conn,ILogger<DockerService> logger, IConfiguration configuration)
    {
        _conn = conn;
        _logger = logger;
        _defaultWorkerExePath = configuration.GetValue<string>("Worker:ExePath")
            ?? @"D:\WORK\ZIPEXTRACTOR\ZipProcessor.Worker\bin\Release\net9.0\ZipProcessor.Worker.exe";

        _defaultWorkerImage = configuration.GetValue<string>("Worker:DefaultImage") ?? "zipprocessor-worker:latest";
        _maxExeWorkers = configuration.GetValue<int>("Worker:MaxExeWorkers", 5);

        try
        {
            _docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Docker unavailable: {ex.Message}");
            _docker = null;
        }
    }

    public async Task<bool> IsDockerAvailableAsync()
    {
        if (_docker == null) return false;
        try
        {
            await _docker.System.PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("IsDockerAvailableAsync : " + ex.Message);
            return false;
        }
    }

    // New helper to allow callers to check port availability (bind to IPAddress.Any)
    public bool IsPortFree(int port)
    {
        try
        {
            using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            sock.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> StartWorkerContainerAsync(string imageName, int port)
    {
        if (!await IsDockerAvailableAsync())
        {
            _logger.LogWarning("Docker not available — fallback to EXE mode");
            return await StartWorkerExeAsync(port, null, null);
        }

        var container = await _docker!.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = imageName,
            ExposedPorts = new Dictionary<string, EmptyStruct> { [port.ToString()] = default },
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    [port.ToString()] = new List<PortBinding> { new PortBinding { HostPort = port.ToString() } }
                }
            }
        });

        await _docker.Containers.StartContainerAsync(container.ID, null);
        _logger.LogInformation($"Started Docker worker: {container.ID[..8]}");
        return container.ID;
    }

    // Start EXE with optional exePath (fallback to configured default) and optional additional arguments.
    public async Task<string> StartWorkerExeAsync(int port, string? exePath, string? additionalArgs)
    {
        // enforce max exe workers: stop oldest exe if we are over the limit
        var exeCount = _startedExe.Count;
        if (exeCount >= _maxExeWorkers)
        {
            var oldest = _startedExe.OrderBy(kv => kv.Value.Started).FirstOrDefault();
            if (!string.IsNullOrEmpty(oldest.Key))
            {
                _logger.LogInformation("Max exe workers reached ({Max}). Stopping oldest exe {Id}", _maxExeWorkers, oldest.Key);
                try
                {
                    await StopContainerAsync(oldest.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed stopping oldest exe {Id}: {Msg}", oldest.Key, ex.Message);
                }
            }
        }

        exePath ??= _defaultWorkerExePath;

        // choose a free port if requested port is not free
        var requestedPort = port;
        var finalPort = requestedPort;
        const int maxAttempts = 20;
        int attempt = 0;
        while (attempt < maxAttempts && !IsPortFree(finalPort))
        {
            _logger.LogWarning("Port {Port} is in use; trying next port", finalPort);
            finalPort++;
            attempt++;
        }
        if (attempt >= maxAttempts && !IsPortFree(finalPort))
        {
            throw new InvalidOperationException($"Could not find a free port near {requestedPort} after {maxAttempts} attempts.");
        }
        if (finalPort != requestedPort)
        {
            _logger.LogInformation("Using alternative port {Port} (requested {Requested})", finalPort, requestedPort);
        }

        // Build final argument string: user-provided additional args + ensure urls arg for binding
        var argsBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            argsBuilder.Append(additionalArgs.Trim());
            argsBuilder.Append(' ');
        }

        // only append urls if not present in additional args
        if (additionalArgs == null || !additionalArgs.Contains("--urls"))
        {
            argsBuilder.Append($"--urls=http://localhost:{finalPort}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = argsBuilder.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger.LogInformation("Worker[{Pid}]: {Line}", process.Id, e.Data); };
        process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger.LogError("Worker[{Pid}] ERR: {Line}", process.Id, e.Data); };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start worker process");

        // capture PID immediately after start to avoid accessing process after it may be disposed
        var pid = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var id = $"exe-{pid}";
        _startedExe[id] = (process, DateTime.UtcNow);

        // When process exits, remove from tracking. Use captured pid and protect against disposed process.
        process.Exited += async (s, e) =>
        {
            try
            {
                _startedExe.TryRemove(id, out _);

                // disposing may already have happened in StopContainerAsync; attempt safely
                try
                {
                    process.Dispose();
                    await _conn.ExecuteAsync("UPDATE worker_registry SET status='Stopped' WHERE container_id=@id", new { id });
                }
                catch
                {
                    // swallow disposal exceptions
                }

                _logger.LogInformation("EXE worker exited (PID={Pid})", pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Exception in Exited handler for PID {Pid}: {Msg}", pid, ex.Message);
            }
        };

        _logger.LogInformation("Started EXE worker, PID: {Pid}, Args: {Args}", pid, startInfo.Arguments);
        await Task.Delay(250); // small delay to let process initialize
        return id;
    }

    public async Task StopContainerAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        if (id.StartsWith("exe-"))
        {
            if (_startedExe.TryRemove(id, out var tuple))
            {
                var proc = tuple.Process;
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                        _logger.LogInformation($"Stopped EXE worker (PID={proc.Id})");
                        // give a brief moment for OS to release socket
                        await Task.Delay(200);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to stop EXE worker : {ex.Message}");
                }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }
            else
            {
                // Not tracked, still try to kill by PID
                var pidPart = id.Split('-', 2).ElementAtOrDefault(1);
                if (int.TryParse(pidPart, out var pid))
                {
                    try
                    {
                        var p = Process.GetProcessById(pid);
                        p.Kill(true);
                        p.Dispose();
                        _logger.LogInformation($"Stopped EXE worker (PID={pid})");
                        await Task.Delay(200);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to stop EXE worker {pid}: {ex.Message}");
                    }
                }
            }
            return;
        }

        if (_docker == null) return;

        try
        {
            await _docker.Containers.StopContainerAsync(id, new ContainerStopParameters());
            await _docker.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true });
            _logger.LogInformation($"Stopped Docker worker {id[..8]}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to stop Docker container {id}: {ex.Message}");
        }
    }
}
