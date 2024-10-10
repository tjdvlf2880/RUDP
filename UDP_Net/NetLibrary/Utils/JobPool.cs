using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetLibrary.Utils
{
    public class ObjectPool<T> where T : class, new()
    {
        protected Queue<T> pool;
        public ObjectPool(int num)
        {
            pool = new Queue<T>();
            for (int i = 0; i < num; i++)
            {
                pool.Enqueue(new T());
            }
        }
        public int GetCount()
        {
            return pool.Count;
        }

        public bool Get(out T e)
        {
            pool.TryDequeue(out e);
            return e != null;
        }

        public void Return(T e)
        {
            pool.Enqueue(e);
        }
        public void Dispose()
        {
            pool.Clear();
            pool = null;
        }

    }
}
