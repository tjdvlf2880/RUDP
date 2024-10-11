using NetLibrary.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetLibrary
{
    public class UDPSocket
    {
        Socket sock;
        public UDPSocket(IPEndPoint local)
        {
            if (!Socket.OSSupportsIPv6)
            {
                NetLogger.DebugLog("IPv6 is not supported by the OS");
                return;
            }
            CreateSocket(local);
        }
        void CreateSocket(IPEndPoint local)
        {
            if (sock == null)
            {
                try
                {
                    sock = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                    sock.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                    sock.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.ReuseAddress, true);

                    const int SIO_UDP_CONNRESET = -1744830452;
                    byte[] inValue = new byte[] { 0 };
                    byte[] outValue = new byte[] { 0 };
                    sock.IOControl(SIO_UDP_CONNRESET, inValue, outValue);
                    sock.Bind(local);
                    NetLogger.DebugLog($"Sock Create : {local.Port.ToString()}");
                }
                catch (SocketException ex)
                {
                    NetLogger.DebugLog($"CreateSocket Error : {ex.Message}");
                    Dispose();
                }
            }
        }
        public bool Receive(SocketAsyncEventArgs args, EventHandler<SocketAsyncEventArgs> callback)
        {
            if (sock == null) return false;

            if (!sock.ReceiveFromAsync(args))
            {
                if (args.SocketError == SocketError.Success)
                {
                    Task.Run(() => callback.Invoke(this, args));
                }
                else
                {
                    NetLogger.DebugLog("Receive 동기적 완료 중 오류 발생: " + args.SocketError);
                    return false;
                }
            }
            return true;
        }
        public bool Send(SocketAsyncEventArgs args, EventHandler<SocketAsyncEventArgs> callback)
        {
            if (sock == null) return false;
            if (DefineFlag.PacketLoseMode)
            {
                //절반확률로 패킷 버리기
                Random rand = new Random();
                if (rand.Next(2) == 0)
                {
                    return false;
                }
            }


            if (!sock.SendToAsync(args))
            {
                if (args.SocketError == SocketError.Success)
                {
                    Task.Run(() => callback.Invoke(this, args));
                }
                else
                {
                    NetLogger.DebugLog("SendAsync 동기적 완료 중 오류 발생: " + args.SocketError);
                    return false;
                }
            }
            return true;
        }
        public void Dispose()
        {
            if (sock != null)
            {
                sock.Close();
                sock = null;
            }
        }
    }
}
