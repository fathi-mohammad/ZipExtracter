namespace ZipProcessor.Admin.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}


public class WorkerInfo
{
    public string worker_id { get; set; } = "";
    public string container_name { get; set; } = "";
    public string api_url { get; set; } = "";
    public string status { get; set; } = "";
    public DateTime? last_ping { get; set; }
    public DateTime? last_heartbeat { get; set; }
    public int? response_time_ms { get; set; }
    public string remarks { get; set; } = "";
}

public class ApiInfo
{
    public string api_name { get; set; } = "";
    public string endpoint { get; set; } = "";
    public string http_method { get; set; } = "";
    public string request_template { get; set; } = "";
    public string description { get; set; } = "";
}

public class DockerDashboardVM
{
    public List<WorkerInfo> Workers { get; set; } = new();
    public List<ApiInfo> ApiList { get; set; } = new();
}