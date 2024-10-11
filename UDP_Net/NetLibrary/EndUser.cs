using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetLibrary.Event;
using NetLibrary.Utils;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NetLibrary
{
    public partial class EndUser
    {
        public Network Net;
        // 소켓 스레드에서 넣고 EndUser 스레드에서 뺀다.
        public Dictionary<Header, ConcurrentQueue<Memory<byte>>> SysPacketQueue = new();
        // EndUser 스레드에서 넣고 Logic 스레드에서 뺀다.
        public ConcurrentQueue<Memory<byte>> PacketCompleteQueue = new();
        //  Logic 스레드에서 넣고 EndUser 스레드에서 뺀다.
        ConcurrentQueue<Memory<byte>> DispatchedQueue = new();
        //  Logic 스레드에서 넣고 EndUser 스레드에서 뺀다.
        public ConcurrentQueue<SysRPCArgs> SysRPCQueue = new();

        Queue<Memory<byte>> DefferedSendQueue = new();
        Queue<Memory<byte>> TemporaryQueue = new();

        // 네트워크 송수신 관련 
        public IPEndPoint RemoteEndPoint;
        public byte SyncID = 0;
        SessionType SessionType;


        public EndUser(SessionType type, Network net)
        {
            this.Net = net;
            this.SessionType = type;
            SysPacketQueue.Add(Header.SYN, new ConcurrentQueue<Memory<byte>>());
            SysPacketQueue.Add(Header.SYNACK, new ConcurrentQueue<Memory<byte>>());
            SysPacketQueue.Add(Header.DATA, new ConcurrentQueue<Memory<byte>>());
            SysPacketQueue.Add(Header.DATAACK, new ConcurrentQueue<Memory<byte>>());
            ReInit(SessionType.UDP,0);
        }

        public void ReInit(SessionType type, byte syncId)
        {
            SessionType = type;
            SyncID = syncId;
            /* 재 연결에 대해 두가지 선택지가 있다....
             * 송신 중인 패킷을 중복하여 보내거나
             * 송신 중인 패킷을 손실 시키거나
             * 선택하자면 패킷을 손실 시키고, 애플리케이션이 손실 되면 안되는 중요한 패킷에 대해 
             * 직접 고유 번호를 할당하여 중복 송신 처리 로직을 구현하는게 낫다.  
            */

            foreach (var kvp in ReceiveJobs)
            {
                var job = kvp.Value;
                ReceiveJobPool.Return(job);
                ReceiveJobs.Remove(kvp.Key);
            }

            ReceiveCompleteSeq = 0;
            MaxReceiveSeq = 0;

            foreach (var kvp in SendJobs)
            {
                var job = kvp.Value;
                SendJobPool.Return(job);
                SendJobs.Remove(kvp.Key);
            }
            SendCompleteSeq = 0;
            NextSendSeq = 1;

        }

        public ConcurrentQueue<Memory<byte>> GetSysQueue(Header header)
        {
            SysPacketQueue.TryGetValue(header, out var queue);
            return queue;
        }

        

        void Send_SYNACK(int ServerId,int OldSyncId, int NewId)
        {
            int HeaderLength = 1;
            int SyncIDTypeLength = 1;
            if (Net.SendArgpool.Get(out var e))
            {
                int offset = 0;
                byte[] buffer = new byte[HeaderLength + SyncIDTypeLength * 3];
                buffer[offset] = (byte)Header.SYNACK;
                offset += HeaderLength;
                buffer[offset] = (byte)ServerId;
                offset += SyncIDTypeLength;
                buffer[offset] = (byte)OldSyncId;
                offset += SyncIDTypeLength;
                buffer[offset] = (byte)NewId;
                e.SetBuffer(buffer);
                e.RemoteEndPoint = RemoteEndPoint;
                e.UserToken = Net;
                if (!Net.socket.Send(e, Network.SocketSendCallback))
                {
                    Net.SendArgpool.Return(e);
                }
            }
        }

        void Send_SYN(IPEndPoint remote, int ClinetSynciD, int ServerSynciD,int NewID)
        {
            int HeaderLength = 1;
            int SessionTypeLength = 1;
            int SyncIDTypeLength = 1;
            if (Net.SendArgpool.Get(out var e))
            {
                int offset = 0;
                byte[] buffer = new byte[HeaderLength + SessionTypeLength + SyncIDTypeLength*3];
                buffer[offset] = (byte)Header.SYN;
                offset += HeaderLength;
                buffer[offset] = (byte)this.SessionType;
                offset += SessionTypeLength;
                buffer[offset] = (byte)ClinetSynciD;
                offset += SyncIDTypeLength;
                buffer[offset] = (byte)ServerSynciD;
                offset += SyncIDTypeLength;
                buffer[offset] = (byte)NewID;
                offset += SyncIDTypeLength;
                e.SetBuffer(buffer);
                e.RemoteEndPoint = remote;
                e.UserToken = Net;
                if (!Net.socket.Send(e, Network.SocketSendCallback))
                {
                    Net.SendArgpool.Return(e);
                }
            }
        }

        void ProcDATAACK()
        {
            var ACKQueue = GetSysQueue(Header.DATAACK);
            if (ACKQueue.TryDequeue(out var packet))
            {
                Header header = (Header)packet.Span[0];
                if ((byte)header != (SyncID+3))
                {
                    Logger.DebugLog("과거로 부터의 유산입니다.");
                    return;
                }
                NetJob_ReceiveACK(packet);
            }

        }
        void ProcDATA()
        {
            var DATAQueue = GetSysQueue(Header.DATA);
            if (DATAQueue.TryDequeue(out var packet))
            {
                Header header = (Header)packet.Span[0];
                if ((byte)header != SyncID)
                {
                    Logger.DebugLog("과거로부터의 패킷입니다.");
                    return;
                }
                NetJob_ReceiveDATA(packet);

                if (DATAQueue.Count > (int)Params.MaxBlockReceiveNum * 2)
                {
                    Logger.DebugLog($"Too Many Packet... {DATAQueue.Count} Discard");
                    DATAQueue.Clear();
                }
            }
        }
        bool ProcSyncEndUser(IPEndPoint remote, long timeout)
        {
            var SYNACKQueue = GetSysQueue(Header.SYNACK);
            bool Success = false;
            bool Run = true;
            byte ClientID = this.SyncID;
            byte ServerID = unchecked((byte)-1);
            byte NewID = unchecked((byte)-1);
            SYNACKQueue.Clear();
            FrameTimer timer = new();
            long Elapsed = 0;
            long TotalElapsed = 0;
            long sendDelay = 0;
            while (Run && (TotalElapsed< timeout))
            {
                Elapsed = timer.GetFrameElapsed();
                TotalElapsed += Elapsed;
                sendDelay += Elapsed;
                if (sendDelay >= 10)
                {
                    Send_SYN(remote, ClientID, ServerID, NewID);
                    sendDelay = 0;
                }
                while (Run && SYNACKQueue.TryDequeue(out var ACK) && (TotalElapsed < timeout))
                {
                    Elapsed = timer.GetFrameElapsed();
                    TotalElapsed += Elapsed;
                    sendDelay += Elapsed;

                    int HeaderLength = 1;
                    int SyncIDTypeLength = 1;
                    int offset = HeaderLength;
                    byte ACK_ClientID = ACK.Span[offset];
                    offset += SyncIDTypeLength;
                    byte ACK_ServerID = ACK.Span[offset];
                    offset += SyncIDTypeLength;
                    byte ACK_NewID = ACK.Span[offset];
                    offset += SyncIDTypeLength;
                    if(ClientID == ACK_ClientID)
                    {
                        if(ServerID != ACK_ServerID)
                        {
                            ServerID = ACK_ServerID;
                            NewID = ACK_NewID;

                            if ((NewID != 0) && (NewID != ClientID) && (NewID != ServerID))
                            {
                                ReInit(SessionType, NewID);
                            }
                            Send_SYN(remote, ClientID, ServerID, SyncID);
                            Run = false;
                            Success = true;
                            break;
                        }
                    }
                }
            }
            return Success;
        }
        void ProcSYN()
        {
            var SYNQueue = GetSysQueue(Header.SYN);
            if (SYNQueue.TryDequeue(out var e))
            {
                int offset = 1;
                int SessionTypeLength = 1;
                int SyncIdLength = 1;
                SessionType type = (SessionType)e.Span[offset];
                offset += SessionTypeLength;
                byte ServerId = e.Span[offset];
                offset += SyncIdLength;
                byte ClientId = e.Span[offset];
                offset += SyncIdLength;
                byte NewId = e.Span[offset];
                offset += SyncIdLength;
                byte ValidId = 0;
                for (byte i = 1; i < 3; i++)
                {
                    if ((i != SyncID) && (ServerId != i))
                    {
                        ValidId = i;
                        break;
                    }
                }
                if ((ClientId == SyncID) && (NewId == ValidId))
                {
                    if ((NewId !=0 ) &&(NewId != ServerId) && (NewId != SyncID))
                    {
                        ReInit(type, NewId);
                    }
                    Net.SyncNotifer.Notify(this);
                }
                else
                {
                    Send_SYNACK(ServerId, SyncID, ValidId);
                }
            }
        }

        public bool SyncEndUser(int timeout)
        {
            SysRPC_SyncEndUser rpc = new SysRPC_SyncEndUser(this, RemoteEndPoint, timeout);
            SysRPCQueue.Enqueue(rpc);
            rpc.notifier.Wait(out var result , timeout);
            return result;
        }
        
        void SysRPCProc()
        {
            if (SysRPCQueue.TryDequeue(out var e))
            {
                switch (e.header)
                {
                    case SysRPC.SYNC:
                        {
                            SysRPC_SyncEndUser rpc = e as SysRPC_SyncEndUser;
                            rpc.notifier.Notify(ProcSyncEndUser(rpc.remote, rpc.timeout));
                        }
                        break;

                }
            }
        }
        void SysPacketProc()
        {
            ProcSYN();
            ProcDATAACK();
            ProcDATA();
        }

        internal void Work(long tick)
        {
            SysRPCProc();
            SysPacketProc();
            NetJob_Update(tick);
        }

        public bool DefferedSend(byte[] data)
        {
            int packetTypelength = 1;
            int seqlength = 4;
            int datalength = 1;
            int MaxBlcoklength = byte.MaxValue;
            if (packetTypelength + seqlength + datalength + data.Length > MaxBlcoklength)
            {
                Logger.DebugLog($"Data Byte is over MaxPacketSize({MaxBlcoklength})... it will be discarded");
                return false;
            }
            else
            {
                DefferedSendQueue.Enqueue(data);
                return true;
            }
        }

        public void Dispose()
        {
            Net = null;
        }

        public void Dispatch()
        {
            int packetTypelength = 1;
            int seqlength = 4;
            int datalength = 1;
            int MaxBlcoklength = byte.MaxValue;
            int dataSize = 0;
            int dataNum = 0;
            if (TemporaryQueue.Count != 0)
            {
                Logger.DebugLog("Temporary queue 메모리 누수");
            }
            while (DefferedSendQueue.TryPeek(out var peekData))
            {
                if (packetTypelength + seqlength + datalength + peekData.Length > MaxBlcoklength)
                {
                    break;
                }
                DefferedSendQueue.TryDequeue(out var data);
                TemporaryQueue.Enqueue(data);
                dataSize += datalength;
                dataSize += peekData.Length;
                dataNum++;
                if (packetTypelength + seqlength + datalength + dataSize > MaxBlcoklength)
                {
                    break;
                }
            }

            if (dataNum != 0)
            {
                int offset = 0;
                Memory<byte> buffer = new byte[packetTypelength + seqlength + dataSize];
                offset += packetTypelength;
                buffer.Slice(offset, seqlength).Span.Fill(0);
                offset += seqlength;
                for (int i = 0; i < dataNum; i++)
                {
                    // 큐에서 데이터 꺼내기
                    var data = TemporaryQueue.Dequeue();
                    buffer.Slice(offset, seqlength).Span.Fill(0);
                    Serializer.ToByte(data.Length, datalength).AsMemory<byte>().CopyTo(buffer.Slice(offset, datalength));
                    offset += datalength;
                    // 데이터를 buffer에 추가하기
                    data.CopyTo(buffer.Slice(offset, data.Length));
                    offset += data.Length;
                }
                DispatchedQueue.Enqueue(buffer);
            }
        }
    }
}