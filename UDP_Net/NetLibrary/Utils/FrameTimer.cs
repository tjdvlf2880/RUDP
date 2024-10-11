using System.Diagnostics;

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
