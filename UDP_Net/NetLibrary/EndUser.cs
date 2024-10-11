using NetLibrary.Event;
using NetLibrary.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
namespace NetLibrary
{
    public partial class EndUser
    {
        public Network Net;
        // 소켓 스레드에서 넣고 EndUser 스레드에서 뺀다.
        public Dictionary<Header, ConcurrentQueue<Memory<byte>>> SysPacketQueue = new Dictionary<Header, ConcurrentQueue<Memory<byte>>>();
        // EndUser 스레드에서 넣고 Logic 스레드에서 뺀다.
        public ConcurrentQueue<Memory<byte>> PacketCompleteQueue = new ConcurrentQueue<Memory<byte>>();
        //  Logic 스레드에서 넣고 EndUser 스레드에서 뺀다.
        ConcurrentQueue<Memory<byte>> DispatchedQueue = new ConcurrentQueue<Memory<byte>>();
        //  Logic 스레드에서 넣고 EndUser 스레드에서 뺀다.
        public ConcurrentQueue<SysRPCArgs> SysRPCQueue = new ConcurrentQueue<SysRPCArgs>();

        Queue<Memory<byte>> DefferedSendQueue = new Queue<Memory<byte>>();
        Queue<Memory<byte>> TemporaryQueue = new Queue<Memory<byte>>();

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
            RemoteEndPoint = null;
            ReInit(SessionType.UDP, 0);
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



        void Send_SYNACK(int ServerId, int OldSyncId, int NewId)
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

        void Send_SYN(IPEndPoint remote, int ClinetSynciD, int ServerSynciD, int NewID)
        {
            int HeaderLength = 1;
            int SessionTypeLength = 1;
            int SyncIDTypeLength = 1;
            if (Net.SendArgpool.Get(out var e))
            {
                int offset = 0;
                byte[] buffer = new byte[HeaderLength + SessionTypeLength + SyncIDTypeLength * 3];
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
                if ((byte)header != (SyncID + 3))
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
            FrameTimer timer = new FrameTimer();
            long Elapsed = 0;
            long TotalElapsed = 0;
            long sendDelay = 0;
            while (Run && (TotalElapsed < timeout))
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
                    if (ClientID == ACK_ClientID)
                    {
                        if (ServerID != ACK_ServerID)
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
                    if ((NewId != 0) && (NewId != ServerId) && (NewId != SyncID))
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
            rpc.notifier.Wait(out var result, timeout);
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




    public partial class EndUser
    {
        public uint SendCompleteSeq = 0;
        public uint ReceiveCompleteSeq = 0;
        uint NextSendSeq = 1;
        Dictionary<uint, Send_Job> SendJobs = new Dictionary<uint, Send_Job>();
        ObjectPool<Receive_Job> ReceiveJobPool = new ObjectPool<Receive_Job>((int)Params.MaxBlockReceiveNum);
        ObjectPool<Send_Job> SendJobPool = new ObjectPool<Send_Job>((int)Params.MaxBlockSendNum);
        uint MaxReceiveSeq = 0;
        Dictionary<uint, Receive_Job> ReceiveJobs = new Dictionary<uint, Receive_Job>();



        public class Receive_Job
        {
            public uint Sequence = 0;
            public Memory<byte> buffer;
            EndUser? user;
            Header header;

            public void UpdatePacket(EndUser user, Header header, uint Sequence, Memory<byte> buffer)
            {
                this.Sequence = Sequence;
                this.buffer = buffer;
                this.user = user;
                this.header = header;
            }
        }

        public class Send_Job
        {
            public uint Sequence = 0;
            public Memory<byte> buffer;
            int NakCount = 0;
            public bool Ack = false;
            IPEndPoint RemoteEndpoint;
            Network Net;
            Header header;
            public int SendCount = 0;
            public long MaxSendDelay = 0;
            public long CurSendDelay = 0;

            public void Reset()
            {
                Sequence = 0;
                buffer = null;
                NakCount = 0;
                RemoteEndpoint = null;
                SendCount = 0;
                MaxSendDelay = 0;
                CurSendDelay = 0;
                Ack = false;
            }

            public void UpdatePacket(Network Net, IPEndPoint RemoteEndpoint, uint Sequence, Header header, Memory<byte> buffer)
            {
                this.Net = Net;
                this.Sequence = Sequence;
                this.buffer = buffer;
                this.RemoteEndpoint = RemoteEndpoint;
                this.header = header;
                NakCount = 0;
                Ack = false;
                UpdatePacket(header, Sequence);
            }


            public void SetACK(bool b)
            {
                Ack |= b;
                if (!Ack)
                {
                    NakCount++;
                    if (!Ack && NakCount > (int)Params.NakNum)
                    {
                        Logger.DebugLog($"Nak {Sequence}");
                        SendPacket(0, true);
                        NakCount = 0;
                    }
                }
            }

            public void UpdatePacket(Header header, uint packetSeq)
            {
                int packetTypelength = 1;
                int Packetseqlength = 4;
                int offset = 0;
                buffer.Span[offset] = (byte)header;
                offset += packetTypelength;
                Serializer.ToByte((int)packetSeq, Packetseqlength).AsSpan().Slice(0, Packetseqlength).CopyTo(buffer.Span.Slice(offset, Packetseqlength));
                offset += Packetseqlength;
            }

            public void SendPacket(long tick, bool immediate = false)
            {
                if (Ack) return;
                CurSendDelay += tick;
                if (immediate || (CurSendDelay > MaxSendDelay))
                {
                    if (Net.SendArgpool.Get(out var e))
                    {
                        e.SetBuffer(buffer);
                        e.RemoteEndPoint = RemoteEndpoint;
                        e.UserToken = Net;
                        if (!Net.socket.Send(e, Network.SocketSendCallback))
                        {
                            Net.SendArgpool.Return(e);
                        }
                        else
                        {
                            MaxSendDelay += (int)Params.SendDelayIncrease;
                            CurSendDelay = 0;
                            SendCount++;
                        }
                    }
                }
            }
        }


        public bool IsLargerSeq(uint Lseq, uint Rseq)
        {
            uint diff = Lseq > Rseq ? Lseq - Rseq : Rseq - Lseq;
            return diff > uint.MaxValue / 2 ? Lseq < Rseq : Lseq > Rseq;
        }


        void NetJob_ReceiveACK(Memory<byte> packet)
        {
            int offset = 0;
            int PacketTypeLength = 1;
            int PacketSeqLength = 4;

            offset += PacketTypeLength;
            uint PacketSeq = BitConverter.ToUInt32(packet.Slice(offset, PacketSeqLength).Span);
            offset += PacketSeqLength;
            byte AckBitField = packet.Span[offset];
            int loopCount = 0;

            if (SendJobs.TryGetValue(PacketSeq, out var job))
            {
                job.SetACK(true);
            }

            for (uint i = PacketSeq - 1; IsLargerSeq(i, SendCompleteSeq) && (loopCount < 8); i--)
            {
                if (SendJobs.TryGetValue(i, out var prevjob))
                {
                    prevjob.SetACK(((AckBitField & (1 << loopCount)) != 0));
                }
                else
                {
                    break;
                }
                loopCount++;
            }
            CheckSendComplete();
        }
        void CheckSendComplete()
        {
            for (uint i = SendCompleteSeq + 1; IsLargerSeq(NextSendSeq, i); i++)
            {
                if (SendJobs.TryGetValue(i, out var job))
                {
                    if (job.Ack)
                    {
                        SendCompleteSeq++;
                        SendJobs.Remove(i, out var j);
                        SendJobPool.Return(j);
                        Logger.DebugLog($"SendCompleteSeq{SendCompleteSeq}");
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }
        void NetJob_ReceiveDATA(Memory<byte> packet)
        {
            int PacketTypeLength = 1;
            int PacketSeqLength = 4;
            int DataSize = packet.Length - (PacketTypeLength + PacketSeqLength);
            uint PacketSeq = BitConverter.ToUInt32(packet.Slice(PacketTypeLength, PacketSeqLength).Span);

            if (IsLargerSeq(PacketSeq, MaxReceiveSeq))
            {
                MaxReceiveSeq = PacketSeq;
            }

            if (IsLargerSeq(PacketSeq, ReceiveCompleteSeq))
            {
                if (ReceiveJobs.TryGetValue(PacketSeq, out var existJob))
                {
                    Send_DATAACK(packet);
                    CheckReceiveComplete();
                }
                else if (Math.Abs(PacketSeq - ReceiveCompleteSeq) <= (int)Params.MaxBlockReceiveNum)
                {
                    if (ReceiveJobPool.Get(out var Newjob))
                    {
                        if (ReceiveJobs.TryAdd(PacketSeq, Newjob))
                        {
                            Newjob.UpdatePacket(this, (Header)packet.Span[0], PacketSeq, packet.Slice(PacketTypeLength + PacketSeqLength, DataSize));
                            Send_DATAACK(packet);
                            CheckReceiveComplete();
                        }
                    }
                }
            }
            else
            {
                Send_DATAACK(packet);
                CheckReceiveComplete();
            }
        }
        public void CheckReceiveComplete()
        {
            int datalength = 1;

            for (uint i = ReceiveCompleteSeq + 1; IsLargerSeq(MaxReceiveSeq + 1, i); i++)
            {
                if (ReceiveJobs.TryGetValue(i, out var job))
                {
                    ReceiveCompleteSeq++;
                    int byteoffset = 0;
                    while (byteoffset != job.buffer.Length)
                    {
                        int size = Serializer.ToValue(job.buffer, byteoffset, datalength);
                        byteoffset += datalength;
                        PacketCompleteQueue.Enqueue(job.buffer.Slice(byteoffset, size));
                        byteoffset += size;
                    }
                    Logger.DebugLog($"ReceiveComplete {job.Sequence}");
                    ReceiveJobs.Remove(i, out var j);
                    ReceiveJobPool.Return(j);
                }
                else
                {
                    break;
                }
            }
        }
        void Send_DATAACK(Memory<byte> packet)
        {
            if (Net.SendArgpool.Get(out var e))
            {
                int offset = 0;
                int PacketTypeLength = 1;
                int PacketSeqLength = 4;
                int AckFieldLength = 1;
                Memory<byte> buffer = new byte[PacketTypeLength + PacketSeqLength + AckFieldLength];
                buffer.Span[offset] = (byte)(SyncID + 3);
                offset += PacketTypeLength;
                uint PacketSeq = BitConverter.ToUInt32(packet.Slice(offset, PacketSeqLength).Span);
                packet.Slice(offset, PacketSeqLength).CopyTo(buffer.Slice(offset, PacketSeqLength));
                offset += PacketSeqLength;

                // 해당 패킷 이전의 8개 패킷에 대한 ACK 도 함께 보내준다.
                byte bitfield = 1;
                int loopCount = 0;

                for (uint i = PacketSeq - 1; IsLargerSeq(i, ReceiveCompleteSeq) && (loopCount < 8); i--)
                {
                    if (ReceiveJobs.TryGetValue(i, out var job))
                    {
                        bitfield |= (byte)(1 << loopCount);
                    }
                    else
                    {
                        bitfield &= (byte)~(1 << loopCount);
                    }
                    loopCount++;
                }

                buffer.Span[offset] = bitfield;
                e.SetBuffer(buffer);
                e.RemoteEndPoint = RemoteEndPoint;
                e.UserToken = Net;
                if (!Net.socket.Send(e, Network.SocketSendCallback))
                {
                    Net.SendArgpool.Return(e);
                }
            }
        }

        void NetJob_Send()
        {
            int ServerMaxBlockSendNum = (int)Params.MaxBlockReceiveNum;
            if (Math.Abs(NextSendSeq - SendCompleteSeq) <= Math.Min(ServerMaxBlockSendNum, (int)Params.MaxBlockSendNum))
            {
                if (DispatchedQueue.TryPeek(out var packet))
                {
                    if (SendJobPool.Get(out var job))
                    {
                        job.Reset();
                        DispatchedQueue.TryDequeue(out packet);
                        uint PacketSeq = NextSendSeq;
                        if (SendJobs.TryAdd(PacketSeq, job))
                        {
                            job.UpdatePacket(Net, RemoteEndPoint, PacketSeq, (Header)SyncID, packet);
                            NextSendSeq++;
                        }
                        else
                        {
                            SendJobPool.Return(job);
                            Logger.DebugLog("송신 패킷 시퀀스 중복 버그 발생 버그 발생 삐용 삐용");
                        }
                    }

                }
            }
        }

        void NetJob_Update(long tick)
        {
            NetJob_Send();
            foreach (var kvp in SendJobs)
            {
                kvp.Value.SendPacket(tick);
            }
        }
    }
}