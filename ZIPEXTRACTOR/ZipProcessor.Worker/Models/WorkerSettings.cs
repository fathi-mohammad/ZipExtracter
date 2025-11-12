using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZipProcessor.Worker.Models
{
    public class WorkerSettings
    {
        public int BatchSize { get; set; } = 500;
        public int MaxParallel { get; set; } = 20;
        public int DelayMilliseconds { get; set; } = 2000;
    }
}
