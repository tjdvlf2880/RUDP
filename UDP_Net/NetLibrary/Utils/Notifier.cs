using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NetLibrary.Utils
{
    public class Notifier<T>
    {
        ConcurrentQueue<T> values = new ConcurrentQueue<T>();
        SemaphoreSlim semaphoreSlim;

        private CancellationTokenSource NotifyTaskTokenSource = null;
        private CancellationToken NotifyTaskToken;

        private int MaxSemaphoreCount;


        public Notifier(int max)
        {
            MaxSemaphoreCount = max;
            semaphoreSlim = new SemaphoreSlim(0, MaxSemaphoreCount);
            NotifyTaskTokenSource = new CancellationTokenSource();
            NotifyTaskToken = NotifyTaskTokenSource.Token;
        }

        public void Dispose()
        {
            NotifyTaskTokenSource.Cancel();
            for (int i = 0; i < MaxSemaphoreCount; i++)
            {
                semaphoreSlim.Release();
            }
            semaphoreSlim.Dispose();
            NotifyTaskTokenSource.Dispose();
            NotifyTaskTokenSource = null;
            semaphoreSlim = null;
        }
        public void Notify(T val)
        {
            values.Enqueue(val);
            semaphoreSlim.Release(1);
        }
        public async Task<(bool success, T val)> Wait(int timeout)
        {
            T val = default;
            try
            {
                if (await semaphoreSlim.WaitAsync(timeout, NotifyTaskToken))
                {
                    // 큐에서 값을 가져옵니다.
                    if (values.TryDequeue(out val))
                    {
                        return (true, val);
                    }
                }
                return (false, val);
            }
            catch
            {
                NetLogger.DebugLog("Notifier Stopped!");
                return (false, val);
            }
        }
    }
}
