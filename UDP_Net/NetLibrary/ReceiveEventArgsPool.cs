using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net.Sockets;
using System.Net;

namespace NetLibrary
{

    public class ReceiveEventArgsPool
    {
        public delegate void ReceiveFunc(object sender, SocketAsyncEventArgs e);

        EventHandler<SocketAsyncEventArgs> ReceiveCallback;

        ConcurrentBag<SocketAsyncEventArgs> pool;
        public ReceiveEventArgsPool(EventHandler<SocketAsyncEventArgs> callback)
        {
            pool = new ConcurrentBag<SocketAsyncEventArgs>();
            ReceiveCallback = callback;
            Memory<byte> mem = new byte[(int)Params.MaxReceiveArgsNum * (int)Params.MaxPacketBlockSize];
            mem.Span.Fill(0);

            for (int i = 0; i < (int)Params.MaxReceiveArgsNum; i++)
            {
                SocketAsyncEventArgs e;
                Create(out e, mem.Slice(i * (int)Params.MaxPacketBlockSize, (int)Params.MaxPacketBlockSize));
                pool.Add(e);
            }
        }
        void Create(out SocketAsyncEventArgs e , Memory<byte> buffer)
        {
            e = new SocketAsyncEventArgs();
            e.SetBuffer(buffer);
            e.UserToken = null;
            e.Completed += ReceiveCallback;
        }

        public bool Get(out SocketAsyncEventArgs e)
        {
            pool.TryTake(out e);
            return e != null;
        }

        public void Return(SocketAsyncEventArgs e)
        {
            e.MemoryBuffer.Span.Slice(0, e.BytesTransferred).Clear();
            e.RemoteEndPoint = null;
            e.UserToken = null;
            pool.Add(e);
        }


        public void Dispose()
        {
            SocketAsyncEventArgs e;
            while (pool.TryTake(out e))
            {
                e.SetBuffer(null);
                e.RemoteEndPoint = null;
                e.UserToken = null;
                e.Completed -= ReceiveCallback;
                e.Dispose();
            }
            pool = null;
        }
    }
}