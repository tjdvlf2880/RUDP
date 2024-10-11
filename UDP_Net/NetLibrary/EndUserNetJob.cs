using NetLibrary.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using static NetLibrary.EndUser;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NetLibrary
{
    public partial class EndUser
    {
        public uint SendCompleteSeq = 0;
        public uint ReceiveCompleteSeq = 0;
        uint NextSendSeq = 1;
        Dictionary<uint, Send_Job> SendJobs = new();
        ObjectPool<Receive_Job> ReceiveJobPool = new((int)Params.MaxBlockReceiveNum);
        ObjectPool<Send_Job> SendJobPool = new((int)Params.MaxBlockSendNum);
        uint MaxReceiveSeq =0;
        Dictionary<uint, Receive_Job> ReceiveJobs = new();
        


        public class Receive_Job
        {
            public uint Sequence = 0;
            public Memory<byte> buffer;
            EndUser user;
            Header header;

            public void UpdatePacket(EndUser user,Header header ,uint Sequence, Memory<byte> buffer)
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

            public void UpdatePacket(Network Net ,IPEndPoint RemoteEndpoint, uint Sequence, Header header, Memory<byte> buffer)
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
                        SendPacket(0,true);
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
                if (immediate ||(CurSendDelay > MaxSendDelay))
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
            int AckFieldLength = 1;

            offset += PacketTypeLength;
            uint PacketSeq = BitConverter.ToUInt32(packet.Slice(offset, PacketSeqLength).Span);
            offset += PacketSeqLength;
            byte AckBitField = packet.Span[offset];
            int loopCount = 0;

            if (SendJobs.TryGetValue(PacketSeq, out var job))
            {
                job.SetACK(true);
            }

            for (uint i = PacketSeq-1; IsLargerSeq(i , SendCompleteSeq) && (loopCount < 8); i--)
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
            for (uint i = SendCompleteSeq + 1; IsLargerSeq(NextSendSeq ,i); i++)
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
            int DataSize = packet.Length - (PacketTypeLength +PacketSeqLength);
            uint PacketSeq = BitConverter.ToUInt32(packet.Slice(PacketTypeLength, PacketSeqLength).Span);

            if (IsLargerSeq(PacketSeq,MaxReceiveSeq))
            {
                MaxReceiveSeq = PacketSeq;
            }

            if (IsLargerSeq( PacketSeq , ReceiveCompleteSeq))
            {
                if (ReceiveJobs.TryGetValue(PacketSeq, out var existJob))
                {
                    Send_DATAACK(packet);
                    CheckReceiveComplete();
                }
                else if( Math.Abs(PacketSeq - ReceiveCompleteSeq) <= (int)Params.MaxBlockReceiveNum)
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
            int packetTypelength = 1;
            int seqlength = 4;
            int datalength = 1;

            for (uint i = ReceiveCompleteSeq + 1; IsLargerSeq(MaxReceiveSeq+1,i); i++)
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

                for (uint i = PacketSeq - 1; IsLargerSeq(i , ReceiveCompleteSeq) && (loopCount < 8); i--)
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
            int ServerMaxBlockSendNum =(int)Params.MaxBlockReceiveNum;
            if( Math.Abs(NextSendSeq - SendCompleteSeq) <= Math.Min(ServerMaxBlockSendNum, (int)Params.MaxBlockSendNum))
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
