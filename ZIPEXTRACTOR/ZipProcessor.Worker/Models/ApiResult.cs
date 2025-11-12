using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZipProcessor.Worker.Models
{
    public class ApiResult
    {
        public string request_id { get; set; } = "";
        public int file_id { get; set; }
        public int zip_id { get; set; }
        public string file_name { get; set; } = "";
        public int API_StatusCode { get; set; }
        public string API_response { get; set; } = "";
        public string procedure_response { get; set; } = "";
        public string procedure_calltime { get; set; } = "";
    }

    public class ProcResult
    {
        public string status { get; set; } = "0";
        public string message { get; set; } = "Unknown";
    }

}
