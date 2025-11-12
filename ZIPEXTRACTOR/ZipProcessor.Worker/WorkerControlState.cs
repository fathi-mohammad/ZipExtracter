using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZipProcessor.Worker
{
    public class WorkerControlState
    {
        private volatile bool _paused;
        private volatile bool _stopped;

        public bool IsPaused => _paused;
        public bool IsStopped => _stopped;

        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        public void Stop() => _stopped = true;
        public void Start() => _stopped = false;
    }
}
