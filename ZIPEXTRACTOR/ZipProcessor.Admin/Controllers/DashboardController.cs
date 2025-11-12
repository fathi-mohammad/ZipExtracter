using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ZipProcessor.Admin.Models;
using ZipProcessor.Admin.Services;

namespace ZipProcessor.Admin.Controllers
{
    public class DashboardController : Controller
    {       
        private readonly MySqlConnection _conn;
        private readonly DockerService _docker;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;

        public DashboardController(MySqlConnection conn, DockerService docker, IConfiguration configuration, IHttpClientFactory httpFactory)
        {
            _conn = conn;
            _docker = docker;
            _config = configuration;
            _httpFactory = httpFactory;
        }
        [HttpGet]
        public async Task<IActionResult> GetWorkersJson()
        {
            using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            // Pull from workers + health: we want created (start_time) and last_heartbeat
            var sql = @"
        SELECT 
            w.worker_id,
            w.container_name,
            w.api_url,
            w.status,
            w.last_heartbeat,
            w.response_time_ms,
            h.start_time AS created_time,
            -- seconds since last heartbeat (for 'alive' calculation)
            TIMESTAMPDIFF(SECOND, w.last_heartbeat, NOW()) AS secs_since_hb,
            -- seconds between created and last heartbeat (what you asked to show)
            TIMESTAMPDIFF(SECOND, h.start_time, w.last_heartbeat) AS secs_created_to_hb
        FROM workers w
        LEFT JOIN health h ON h.worker_id = w.worker_id
        ORDER BY w.id DESC";

            var rows = await conn.QueryAsync(sql);

            // Mark stale if last heartbeat older than 60s
            var list = rows.Select(r => new
            {
                worker_id = (string)r.worker_id,
                container_name = (string?)r.container_name,
                api_url = (string?)r.api_url,
                status = (string?)r.status ?? "Unknown",
                last_heartbeat = ((DateTime?)r.last_heartbeat)?.ToString("yyyy-MM-dd HH:mm:ss"),
                created_time = ((DateTime?)r.created_time)?.ToString("yyyy-MM-dd HH:mm:ss"),
                response_time_ms = (int?)r.response_time_ms ?? 0,
                secs_since_hb = (int?)r.secs_since_hb ?? int.MaxValue,
                secs_created_to_hb = (int?)r.secs_created_to_hb ?? 0,
                is_stale = ((int?)r.secs_since_hb ?? int.MaxValue) > 60   // 💡 > 60s => show indicator
            });

            return Json(list);
        }


        [HttpGet]
        public async Task<IActionResult> IndexDocker()
        {
            using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            // 1 Fetch worker containers
            var workers = await conn.QueryAsync<WorkerInfo>(@"
                SELECT worker_id, container_name, api_url, status,
                       last_ping, last_heartbeat, response_time_ms, remarks
                FROM workers ORDER BY id DESC;");

            // 2 Fetch available control APIs
            var apis = await conn.QueryAsync<ApiInfo>(@"
                SELECT api_name, endpoint, http_method, request_template, description
                FROM api_master ORDER BY id;");

            var model = new DockerDashboardVM
            {
                Workers = workers.ToList(),
                ApiList = apis.ToList()
            };

            return View(model);
        }
        public async Task<IActionResult> Index()
        {
            if (HttpContext.Session.GetString("User") == null)
                return RedirectToAction("Login", "Account");

            ViewBag.User = HttpContext.Session.GetString("User");

            // Fetch workers
            var workers = await _conn.QueryAsync("SELECT * FROM worker_registry ORDER BY id DESC");

            // Fetch service definitions from DB
            var services = (await _conn.QueryAsync<ServiceDefinition>("SELECT * FROM service_master ORDER BY id ASC")).ToList();

            foreach (var s in services)
            {
                if (!string.IsNullOrWhiteSpace(s.InputsJson))
                {
                    try
                    {
                        s.Inputs = System.Text.Json.JsonSerializer.Deserialize<List<ServiceInputDefinition>>(s.InputsJson) ?? new();
                    }
                    catch
                    {
                        s.Inputs = new();
                    }
                }
            }
          //  var apis = await _conn.QueryAsync(
        // "SELECT api_name, endpoint, http_method, request_template, description FROM api_master ORDER BY id ASC");
            var apis = await _conn.QueryAsync(
    "SELECT api_name, endpoint, http_method, request_template, description FROM api_master ORDER BY id ASC");
            ViewBag.ApiList = apis.ToList();

            ViewBag.AvailableServices = services;
            ViewBag.ApiList = apis.ToList();
            
            return View(workers);
        }

        //  GENERIC API CALLER
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CallWorkerApi([FromForm] string id, [FromForm] string apiName, [FromForm] string? requestBody)
        {
            var worker = await _conn.QueryFirstOrDefaultAsync(
                "SELECT container_id, api_url FROM worker_registry WHERE container_id=@id", new { id });

            if (worker == null)
                return NotFound(new { ok = false, message = "Worker not found" });

            var api = await _conn.QueryFirstOrDefaultAsync(
                "SELECT endpoint, http_method, request_template FROM api_master WHERE api_name=@apiName",
                new { apiName });

            if (api == null)
                return NotFound(new { ok = false, message = "API not defined" });

            string apiUrl = worker.api_url;
            if (string.IsNullOrWhiteSpace(apiUrl))
                return BadRequest(new { ok = false, message = "Worker API URL missing" });

            var fullUrl = apiUrl.TrimEnd('/') + api.endpoint;
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            try
            {
                HttpResponseMessage resp;

                if (api.http_method.ToString().ToUpper() == "GET")
                {
                    resp = await client.GetAsync(fullUrl);
                }
                else
                {
                    string payload = string.IsNullOrWhiteSpace(requestBody)
                        ? (api.request_template ?? "{}")
                        : requestBody;

                    var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    resp = await client.PostAsync(fullUrl, content);
                }

                var body = await resp.Content.ReadAsStringAsync();

                // Optional registry updates for known commands
                if (apiName.Equals("Pause", StringComparison.OrdinalIgnoreCase))
                    await _conn.ExecuteAsync("UPDATE worker_registry SET status='Paused', last_successful_ping=NOW() WHERE container_id=@id", new { id });
                else if (apiName.Equals("Resume", StringComparison.OrdinalIgnoreCase))
                    await _conn.ExecuteAsync("UPDATE worker_registry SET status='Running', last_successful_ping=NOW() WHERE container_id=@id", new { id });
                else if (apiName.Equals("Stop", StringComparison.OrdinalIgnoreCase))
                    await _conn.ExecuteAsync("UPDATE worker_registry SET status='Stopped', last_successful_ping=NOW() WHERE container_id=@id", new { id });
                else if (apiName.Equals("Start", StringComparison.OrdinalIgnoreCase))
                    await _conn.ExecuteAsync("UPDATE worker_registry SET status='Running', last_successful_ping=NOW() WHERE container_id=@id", new { id });

                return Json(new
                {
                    ok = resp.IsSuccessStatusCode,
                    status = resp.StatusCode,
                    apiName,
                    response = body,
                    last_successful_ping = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, apiName, message = ex.Message });
            }
        }

        private async Task<ServiceDefinition?> GetServiceDefinitionAsync(string name)
        {
            var svc = await _conn.QueryFirstOrDefaultAsync<ServiceDefinition>(
                "SELECT * FROM service_master WHERE name=@name LIMIT 1", new { name });
            if (svc != null && !string.IsNullOrWhiteSpace(svc.InputsJson))
            {
                svc.Inputs = System.Text.Json.JsonSerializer
                    .Deserialize<List<ServiceInputDefinition>>(svc.InputsJson) ?? new();
            }
            return svc;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartWorker([FromForm] StartWorkerModel model)
        {
            if (model.Port == 0)
            {
                var basePort = _config.GetValue<int?>("WorkerSettings:BaseApiPort") ?? _config.GetValue<int?>("Worker:BaseApiPort") ?? 8080;
                model.Port = basePort + new Random().Next(1, 100);
            }

            // load services
            var services = _config.GetSection("Services").Get<List<ServiceDefinition>>() ?? new List<ServiceDefinition>();
           // ServiceDefinition? selectedService = null;
           // if (!string.IsNullOrWhiteSpace(model.SelectedServiceName))
           // {
              //  selectedService = services.FirstOrDefault(s => s.Name == model.SelectedServiceName);
          //  }
            var selectedService = await GetServiceDefinitionAsync(model.SelectedServiceName);

            // apply service defaults
            if (selectedService != null)
            {
                if (selectedService.Type?.Equals("Docker", StringComparison.OrdinalIgnoreCase) == true)
                {
                    model.ImageName ??= selectedService.Image ?? _config.GetValue<string>("Worker:DefaultImage");
                    model.ServerType = model.ParseServerType("Docker");
                }
                else
                {
                    model.ExePath ??= selectedService.ExePath ?? _config.GetValue<string>("Worker:ExePath");
                    if (model.Port == 0 && selectedService.DefaultPort.HasValue)
                        model.Port = selectedService.DefaultPort.Value;
                    model.ServerType = model.ParseServerType("Exe");
                }
            }

            // If requested port is already used by a Running or Paused worker, pick a free port
            var conflict = await _conn.QueryFirstOrDefaultAsync("SELECT 1 FROM worker_registry WHERE port=@p AND status IN ('Running','Paused') LIMIT 1", new { p = model.Port });
            if (conflict != null && !_docker.IsPortFree(model.Port))
            {
                // try to find a nearby free port
                var start = model.Port + 1;
                var found = -1;
                for (int p = start; p < start + 100; p++)
                {
                    if (_docker.IsPortFree(p))
                    {
                        found = p;
                        break;
                    }
                }
                if (found == -1)
                {
                    return BadRequest("Requested port is in use and no alternative free port found nearby.");
                }
                model.Port = found;
            }

            // Build arguments from posted form keys named "arg__{Key}"
            string BuildArgsFromForm()
            {
                var args = new List<string>();

                // collect dynamic args posted from UI
                foreach (var entry in Request.Form)
                {
                    var k = entry.Key;
                    if (!k.StartsWith("arg__")) continue;
                    var keyPart = k.Substring("arg__".Length); // this should equal InputDefinition.Key
                    var value = entry.Value.ToString();
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    // quote values with spaces
                    var safe = value.Contains(' ') ? $"\"{value}\"" : value;
                    // format as "--Key value"
                    args.Add($"--{keyPart} {safe}");
                }

                // include free-form additional args if provided
                if (!string.IsNullOrWhiteSpace(model.AdditionalArguments))
                {
                    args.Add(model.AdditionalArguments.Trim());
                }

                // ensure urls present
                if (!args.Any(a => a.Contains("--urls")))
                {
                    args.Add($"--urls=http://localhost:{model.Port}");
                }

                return string.Join(' ', args);
            }

            string containerId;
            if (model.ServerType == ServerType.Docker || (selectedService != null && selectedService.Type?.Equals("Docker", StringComparison.OrdinalIgnoreCase) == true))
            {
                var imageName = !string.IsNullOrWhiteSpace(model.ImageName) ? model.ImageName : _config.GetValue<string>("Worker:DefaultImage") ?? "zipprocessor-worker:latest";
                containerId = await _docker.StartWorkerContainerAsync(imageName, model.Port);
            }
            else
            {
                var argString = BuildArgsFromForm();
                containerId = await _docker.StartWorkerExeAsync(model.Port, model.ExePath, argString);
            }

            await _conn.ExecuteAsync(@"
    INSERT INTO worker_registry (worker_name, container_id, status, port, mode, api_url, last_successful_ping)
    VALUES (@n, @c, 'Running', @p, @m, @u, NOW())",
    new
    {
        n = containerId.Length >= 8 ? containerId[..8] : containerId,
        c = containerId,
        p = model.Port,
        m = containerId.StartsWith("exe-") ? "exe" : "docker",
        u = ExtractWorkerUrl(model)
    });


            return RedirectToAction("Index");
        }
        private static string? ExtractWorkerUrl(StartWorkerModel model)
        {
            // Prefer explicit WorkerUrl input if posted
            if (!string.IsNullOrWhiteSpace(model.AdditionalArguments) && model.AdditionalArguments.Contains("ApiSettings:WorkerUrl"))
            {
                // Try to parse the value, crude split
                var parts = model.AdditionalArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var idx = Array.FindIndex(parts, p => p.Contains("ApiSettings:WorkerUrl"));
                if (idx >= 0 && idx + 1 < parts.Length)
                    return parts[idx + 1].Trim('"');
            }

            // Or look for dynamic form field (arg__ApiSettings:WorkerUrl)
            var formValue = model.AdditionalArguments?.Trim();
            if (!string.IsNullOrWhiteSpace(formValue))
                return formValue;

            // Fallback default
            return $"http://localhost:{model.Port}";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopWorker(string id)
        {
            await _docker.StopContainerAsync(id);
            await _conn.ExecuteAsync("UPDATE worker_registry SET status='Stopped' WHERE container_id=@id", new { id });
            return RedirectToAction("Index");
        }
        // POST /Dashboard/PauseWorker
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PauseWorker([FromForm] string id)
        {
            //  Get worker info including api_url
            var worker = await _conn.QueryFirstOrDefaultAsync(
                "SELECT container_id, api_url, status FROM worker_registry WHERE container_id=@id",
                new { id });

            if (worker == null)
                return NotFound(new { ok = false, message = "Worker not found" });

            string? apiUrl = worker.api_url;
            if (string.IsNullOrWhiteSpace(apiUrl))
                return BadRequest(new { ok = false, message = "API URL missing for worker" });

            // Ensure URL format and append /api/control/pause
            var pauseUrl = apiUrl.TrimEnd('/') + "/control/pause";

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                var resp = await client.PostAsync(pauseUrl, null);
                if (resp.IsSuccessStatusCode)
                {
                    await _conn.ExecuteAsync(
                        "UPDATE worker_registry SET status='Paused', last_successful_ping=NOW() WHERE container_id=@id",
                        new { id });

                    var body = await resp.Content.ReadAsStringAsync();
                    return Json(new
                    {
                        ok = true,
                        message = "Paused",
                        raw = body,
                        last_successful_ping = DateTime.UtcNow
                    });
                }

                return StatusCode((int)resp.StatusCode, new { ok = false, message = "Worker returned error" });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }


        // POST /Dashboard/ResumeWorker
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeWorker([FromForm] string id)
        {
            var worker = await _conn.QueryFirstOrDefaultAsync(
                "SELECT container_id, api_url, status FROM worker_registry WHERE container_id=@id",
                new { id });

            if (worker == null)
                return NotFound(new { ok = false, message = "Worker not found" });

            string? apiUrl = worker.api_url;
            if (string.IsNullOrWhiteSpace(apiUrl))
                return BadRequest(new { ok = false, message = "API URL missing for worker" });

            var resumeUrl = apiUrl.TrimEnd('/') + "/control/resume";

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);


            try
            {
                var resp = await client.PostAsync(resumeUrl, null);
                if (resp.IsSuccessStatusCode)
                {
                    await _conn.ExecuteAsync(
                        "UPDATE worker_registry SET status='Running', last_successful_ping=NOW() WHERE container_id=@id",
                        new { id });

                    var body = await resp.Content.ReadAsStringAsync();
                    return Json(new
                    {
                        ok = true,
                        message = "Resumed",
                        raw = body,
                        last_successful_ping = DateTime.UtcNow
                    });
                }

                return StatusCode((int)resp.StatusCode, new { ok = false, message = "Worker returned error" });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }




        private async Task<IActionResult> PostControlAction(string id, string endpoint, string newStatus)
        {
            var worker = await _conn.QueryFirstOrDefaultAsync(
                "SELECT container_id, api_url FROM worker_registry WHERE container_id=@id", new { id });

            if (worker == null)
                return NotFound(new { ok = false, message = "Worker not found" });

            string? apiUrl = worker.api_url;
            if (string.IsNullOrWhiteSpace(apiUrl))
                return BadRequest(new { ok = false, message = "Missing API URL" });

            var target = apiUrl.TrimEnd('/') + endpoint;

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                var resp = await client.PostAsync(target, null);
                if (resp.IsSuccessStatusCode)
                {
                    await _conn.ExecuteAsync(
                        "UPDATE worker_registry SET status=@s, last_successful_ping=NOW() WHERE container_id=@id",
                        new { id, s = newStatus });

                    var body = await resp.Content.ReadAsStringAsync();
                    return Json(new { ok = true, message = $"{newStatus}", raw = body, last_successful_ping = DateTime.UtcNow });
                }

                return StatusCode((int)resp.StatusCode, new { ok = false, message = "Worker returned error" });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }


        // GET /Dashboard/ProbeAll
        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> ProbeAll()
        {
            // 1Get only Running or Paused workers (any mode)
            var rows = await _conn.QueryAsync(@"
        SELECT container_id, api_url, status
        FROM worker_registry
        WHERE status IN ('Running', 'Paused')
        ORDER BY id DESC");

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var results = new List<object>();

            // 2 Probe each one sequentially (await each call)
            foreach (var r in rows)
            {
                string cid = r.container_id;
                string? apiUrl = r.api_url;
                string currentStatus = r.status;

                if (string.IsNullOrWhiteSpace(apiUrl))
                {
                    results.Add(new { container_id = cid, ok = false, message = "Missing API URL" });
                    continue;
                }

                // Ensure URL has no trailing slash, then append /api/control/status
                var normalizedUrl = apiUrl.TrimEnd('/') + "/control/status";

                try
                {
                    var resp = await client.GetAsync(normalizedUrl);
                    if (resp.IsSuccessStatusCode)
                    {
                        var statusJson = await resp.Content.ReadAsStringAsync();
                        string newStatus = currentStatus;

                        try
                        {
                            using var doc = JsonDocument.Parse(statusJson);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("stopped", out var stopped) && stopped.GetBoolean())
                                newStatus = "Stopped";
                            else if (root.TryGetProperty("paused", out var paused) && paused.GetBoolean())
                                newStatus = "Paused";
                            else
                                newStatus = "Running";
                        }
                        catch
                        {
                            // ignore parse errors, keep existing
                        }

                        await _conn.ExecuteAsync(
                            "UPDATE worker_registry SET status=@s, last_successful_ping=NOW() WHERE container_id=@id",
                            new { id = cid, s = newStatus });

                        results.Add(new { container_id = cid, ok = true, status = newStatus, api_url = apiUrl, last_successful_ping = DateTime.UtcNow });
                    }
                    else
                    {
                        results.Add(new { container_id = cid, ok = false, status = currentStatus, api_url = apiUrl, message = $"HTTP {resp.StatusCode}" });
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { container_id = cid, ok = false, status = currentStatus, api_url = apiUrl, message = ex.Message });
                }
            }

            return Json(results);
        }

    }

    // helper on StartWorkerModel to parse ServerType
    public static class StartWorkerModelExtensions
    {
        public static ServerType ParseServerType(this StartWorkerModel _, string s) =>
            Enum.TryParse<ServerType>(s, true, out var v) ? v : ServerType.Exe;
    }
}
