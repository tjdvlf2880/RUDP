using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetLibrary.Utils
{
    public class FrameTimer
    {
        private Stopwatch stopwatch;
        private long lastElapsedMilliseconds;

        public FrameTimer()
        {
            stopwatch = new Stopwatch();
            stopwatch.Start();
        }

        public long GetFrameElapsed()
        {
            long currentElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            long frameTime = currentElapsedMilliseconds - lastElapsedMilliseconds;
            lastElapsedMilliseconds = currentElapsedMilliseconds;
            return frameTime;
        }
    }
}
