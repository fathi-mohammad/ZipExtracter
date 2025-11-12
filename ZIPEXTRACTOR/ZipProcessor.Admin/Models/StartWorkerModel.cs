using System.ComponentModel.DataAnnotations;

namespace ZipProcessor.Admin.Models
{
    public enum ServerType
    {
        Docker,
        Exe
    }

    public class StartWorkerModel
    {
      
        public string? SelectedServiceName { get; set; }

        public ServerType ServerType { get; set; } = ServerType.Docker;

      
        public int Port { get; set; }

        
        public string? ExePath { get; set; }

       
        public string? ImageName { get; set; }

       
        public string? AdditionalArguments { get; set; }
    }
}