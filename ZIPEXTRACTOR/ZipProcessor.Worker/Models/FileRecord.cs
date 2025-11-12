using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZipProcessor.Worker.Models
{
    public class FileRecord
    {
        public int id { get; set; }
        public string request_id { get; set; } = "";
        public int zip_id { get; set; }
        public int file_id { get; set; }
        public string file_path { get; set; } = "";
        public string status { get; set; } = "";
    }
}
