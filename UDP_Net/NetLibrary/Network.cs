﻿
using NetLibrary.Utils;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetLibrary
{
    public class Network
    {
        public UDPSocket socket;
        public SendEventArgsPool SendArgpool { get; }
        public ReceiveEventArgsPool ReceiveArgpool { get; }

        public Notifier<EndUser> SyncNotifer;

        InterLockedVal EndUserNum;

        //혼잡을 방지하기 위해 최대 개수를 정한다.  
        public ConcurrentDictionary<IPEndPoint, EndUser> EndUsers = new();

        Thread NetThread;
        bool Run = false;

        public Network(IPEndPoint local , int MaxConnection)
        {
            socket = new UDPSocket(local);
            SendArgpool = new(SocketSendCallback);
            ReceiveArgpool = new(SocketReceiveCallback);
            EndUserNum  = new InterLockedVal(MaxConnection);
            SyncNotifer = new Notifier<EndUser>(MaxConnection);
            for (int i = 0; i < 5; i++)
            {
                if (ReceiveArgpool.Get(out var e))
                {
                    RequestReceive();
                }
            }
            NetThread= new Thread(NetworkThread);
            Run = true;
            NetThread.Start();

        }

        public void NetworkThread()
        {
            FrameTimer timer = new FrameTimer();
            while (Run)
            {
                foreach (var kv in EndUsers)
                {
                    kv.Value.Work(timer.GetFrameElapsed());
                }
            }
        }

        public bool CreateEndUser(IPEndPoint remote, SessionType type , out EndUser user)
        {
            EndUser Newuser = new EndUser(type, this);
            Newuser.RemoteEndPoint = remote;
            AddOrUpdateUser(Newuser, remote);
            return EndUsers.TryGetValue(remote,out user);
        }

        public bool WaitSyncRequest(out EndUser user ,int? timeout)
        {
            SyncNotifer.Wait(out user, timeout);
            return user != null;
        }

        void Enqueue(Header header , SocketAsyncEventArgs e)
        {
            IPEndPoint remote = e.RemoteEndPoint as IPEndPoint;
            if (EndUsers.TryGetValue(remote, out var user))
            {
                user.GetSysQueue(header).Enqueue(e.MemoryBuffer.Slice(0, e.BytesTransferred).ToArray());
            }
        }

        public bool AddOrUpdateUser(EndUser NewUser, IPEndPoint remote)
        {
            if (!EndUsers.TryGetValue(remote, out var user))
            {
                if (!EndUserNum.SetVal((val) => { return val - 1; }, (val) => { return (val > 0); }))
                {
                    return false;
                }
                NewUser.RemoteEndPoint = remote;
                if(!EndUsers.TryAdd(remote, NewUser))
                {
                    EndUserNum.Increase();
                }
            }
            else
            {
                user.RemoteEndPoint = remote;
            }
            return true;
        }

        public static void SocketReceiveCallback(object sender, SocketAsyncEventArgs e)
        {
            Network net = e.UserToken as Network;
            if (e.SocketError != SocketError.Success)
            {
                Logger.DebugLog(e.SocketError.ToString());
                net.ReceiveArgpool.Return(e);
                net.RequestReceive();

                Logger.DebugLog(e.SocketError.ToString());
                return;
            };
            Header header = (Header)e.MemoryBuffer.Span[0];
            IPEndPoint remote = e.RemoteEndPoint as IPEndPoint;
            net.AddOrUpdateUser(new EndUser(SessionType.UDP,net), remote);
            switch (header)
            {
                case (Header.SYN):
                    {
                        net.Enqueue(header,e);
                    }
                    break;
                case (Header.SYNACK):
                    {
                        net.Enqueue(header, e);
                    }
                    break;
                case (Header.DATAACK_ID_1):
                    {
                        net.Enqueue(Header.DATAACK, e);
                    }
                    break;
                case (Header.DATAACK_ID_2):
                    {
                        net.Enqueue(Header.DATAACK, e);
                    }
                    break;
                case (Header.DATAACK_ID_3):
                    {
                        net.Enqueue(Header.DATAACK, e);
                    }
                    break;
                case (Header.DATA_ID_1):
                    {
                        net.Enqueue(Header.DATA, e);
                    }
                    break;
                case (Header.DATA_ID_2):
                    {
                        net.Enqueue(Header.DATA, e);
                    }
                    break;
                case (Header.DATA_ID_3):
                    {
                        net.Enqueue(Header.DATA, e);
                    }
                    break;
            }

            net.ReceiveArgpool.Return(e);
            net.RequestReceive();
        }
        public void RequestReceive()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    if (ReceiveArgpool.Get(out var arg))
                    {
                        arg.RemoteEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                        arg.UserToken = this;
                        if (!socket.Receive(arg, SocketReceiveCallback))
                        {
                            ReceiveArgpool.Return(arg);
                            continue;
                        }
                    }
                    else
                    {
                        //메모리풀에 반환 될 때 까지 잠시 대기
                        Thread.Sleep(3000);
                        continue;
                    }
                    break;
                }
            });
        }
        public static void SocketSendCallback(object sender, SocketAsyncEventArgs e)
        {
            Network net = e.UserToken as Network;
            if (e.SocketError != SocketError.Success)
            {
                Logger.DebugLog(e.SocketError.ToString());
                net.SendArgpool.Return(e);
                return;
            };
            net.SendArgpool.Return(e);
        }

        public void Dispose()
        {
            Run = false;
            NetThread.Join();
            socket.Dispose();
        }
    }
}
