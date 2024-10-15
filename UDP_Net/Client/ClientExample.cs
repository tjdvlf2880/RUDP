using NetLibrary;
using NetLibrary.Utils;
using System.Net;
using System.Text;


    internal class ClientExample
{
        static async Task Main(string[] args)
        {
            DefineFlag.UnityLog = false;

            // 1. 자신의 IP PORT 로 네트워크 객체를 생성한다.
            //      2-1 IP에 ANY 그리고 PORT에 0을 주면 자동 할당 된다.
            //      2-2 네트워크 객체 생성시 최대 연결 개수를 지정한다.
            IPEndPoint Any = new IPEndPoint(IPAddress.IPv6Any, 9000);
            Network client = new Network(Any, 255);
            // 2. 네트워크 객체에서 원격지와 1대1 연결성을 갖는 EndUser를 생성한다.
            //      2-1. 서버 IP PORT를 알아야 한다. (DNS 서버를 통해 알아와야 한다.)
            //      2-2  최대 연결수를 넘어서 생성을 시도할 시 함수는 실패한다.
            string Serverip = "192.168.0.38";
            ushort Serverport = 8000;
            IPEndPoint Server = new IPEndPoint(IPAddress.Parse(Serverip).MapToIPv6(), Serverport);
            bool Success = client.CreateEndUser(Server, SessionType.RUDP, out var user);
            // 3.  원격지에 동기화 요청을 보낸다. 
            //      3-1 동기화는 2 hand shake 를 거침으로 성공 여부를 정확히 알 수 없다.
            //      3-2 Timeout 기간이 짧다면 동기화 요청이 실패할 가능성이 높다.
            //      3-3 패킷을 보내 직접 응답을 체크하여 동기화 성공을 판단해야 한다. 
            bool MightBeSuccess = await user.SyncEndUser(1000);
            // 4.  패킷 송신 
            //      4-1 패킷량을 줄이기 위해서는 DefferedQueue에 메세지를 보낸다.
            //      4-2 DisPatch 호출시 큐에 쌓인 패킷이 블록 뭉탱이로 전송된다.
            //      4-3 SyncEndUser 호출시 Dispatch 된 메세지는 손실 될 수 있다.
            user.DefferedSend(Encoding.UTF8.GetBytes($"hello Server"));
            user.Dispatch();
            bool Run = true;
            // 5. 패킷 수신
            //      5-1 PacketCompleteQueue 에서 수신된 패킷을 가져온다.
            //      5-2 이때 가져온 패킷은 블록이 아닌 개별 패킷들이다. 
            while (Run)
            {
                user.DefferedSend(Encoding.UTF8.GetBytes($"hello Server"));
                user.Dispatch();
                if (user.PacketCompleteQueue.TryDequeue(out var packet))
                {
                    NetLogger.DebugLog(Encoding.UTF8.GetString(packet.Span));
                }

            }
            // 6. 메모리 해제
            //    6-1 네트워크 객체가 정리될때 User도 같이 정리된다.
            client.Dispose();
        }
    }