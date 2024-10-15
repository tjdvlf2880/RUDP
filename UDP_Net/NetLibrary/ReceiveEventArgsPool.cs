using System;
using System.Collections.Concurrent;

using System.Net.Sockets;

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

            for (int i = 0; i < DefineFlag.MaxReceiveArgsNum; i++)
            {
                SocketAsyncEventArgs e = new SocketAsyncEventArgs();

                /* .netStandard 2.1 mono unity 에서 Dll로 사용하려는데
                   socketError는  success 인데 0 바이트로 읽는 버그 발생
                   12시간의 삽질 후 e.SetBuffer(Memory<byte>); 이게 동작하지 않는다는걸 발견.. 후.... 후...후욱..
                 */

                e.SetBuffer(new byte[DefineFlag.MaxPacketBlockSize], 0, DefineFlag.MaxPacketBlockSize);
                e.UserToken = null;
                e.Completed += ReceiveCallback;
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