using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace NetLibrary
{
    public class SendEventArgsPool : IDisposable
    {
        EventHandler<SocketAsyncEventArgs> SendCallback;
        protected ConcurrentBag<SocketAsyncEventArgs> pool;
        public SendEventArgsPool(EventHandler<SocketAsyncEventArgs> callback)
        {
            SendCallback = callback;
            pool = new ConcurrentBag<SocketAsyncEventArgs>();
            for (int i = 0; i < DefineFlag.MaxSendArgsNum; i++)
            {
                SocketAsyncEventArgs e;
                _Create(out e);
                pool.Add(e);
            }
        }

        public bool Get(out SocketAsyncEventArgs e)
        {
            pool.TryTake(out e);
            return e != null;
        }

        public void Return(SocketAsyncEventArgs e)
        {
            e.SetBuffer(null);
            e.RemoteEndPoint = null;
            e.UserToken = null;
            pool.Add(e);
        }

        void _Create(out SocketAsyncEventArgs e)
        {
            e = new SocketAsyncEventArgs();
            e.SetBuffer(null);
            e.RemoteEndPoint = null;
            e.UserToken = null;
            e.Completed += SendCallback;
        }
        public void Dispose()
        {
            SocketAsyncEventArgs e;
            while (pool.TryTake(out e))
            {
                e.SetBuffer(null);
                e.RemoteEndPoint = null;
                e.UserToken = null;
                e.Completed -= SendCallback;
                e.Dispose();
            }
            pool = null;
        }
    }
}
