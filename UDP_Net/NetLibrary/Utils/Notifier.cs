using System.Collections.Concurrent;
using System.Threading;

namespace NetLibrary.Utils
{
    public class Notifier<T>
    {
        ConcurrentQueue<T> values = new ConcurrentQueue<T>();
        SemaphoreSlim semaphoreSlim;
        public Notifier(int max)
        {
            semaphoreSlim = new SemaphoreSlim(0, max);
        }

        public void Notify(T val)
        {
            values.Enqueue(val);
            semaphoreSlim.Release(1);
        }

        public bool Wait(out T val, int? timeout)
        {
            val = default;
            if (timeout == null)
            {
                semaphoreSlim.Wait();
                if (values.TryDequeue(out val))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (semaphoreSlim.Wait((int)timeout))
                {
                    if (values.TryDequeue(out val))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

    }
}
