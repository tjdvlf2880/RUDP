using NetLibrary;
using NetLibrary.Utils;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.IO.IsolatedStorage;

namespace Client
{
    internal class Program
    {
        static void Main(string[] args)
        { 
            // 1. 자신의 IP PORT 로 네트워크 객체를 생성한다.
            //      1-1 네트워크 객체 생성시 최대 연결 개수를 지정한다.
            string Serverip = "192.168.0.38";
            ushort Serverport = 8000;
            IPEndPoint ServerAddress = new IPEndPoint(IPAddress.Parse(Serverip).MapToIPv6(), Serverport);
            Network Server = new Network(ServerAddress, 255);

            // 2. 클라이언트의 Sync 요청을 대기한다. 
            //      2-1 동기화는 2 hand shake 를 거침으로 성공 여부를 정확히 알 수 없다.
            //      2-2 Timeout 기간이 짧다면 동기화 요청이 실패할 가능성이 높다.
            //      2-3 패킷을 보내 직접 응답을 체크하여 동기화 성공을 판단해야 한다. 
            //      2-4 WaitSyncRequest는 싱크 요청을 한 EndUser 객체를 알려준다.
            ConcurrentBag<EndUser> SyncList = new ConcurrentBag<EndUser>();
            Task.Run(() =>
            {
                while (true)
                {
                    if (Server.WaitSyncRequest(out var user, null))
                    {
                        SyncList.Add(user);
                    }
                }
            });
            // 3. 서버 로직을 수행한다.
            //     3-1 어플리케이션 단계에서 패킷규약을 잘 약속하여 정보를 주고 받는다.
            //     3-2 최대 패킷 수신 크기는 512 바이트 임으로 큰 파일 전송시 알아서 분할 로직을 작성할 것.  
            FrameTimer timer = new FrameTimer();
            List<EndUser> userList = new List<EndUser>();
            bool Run = true;
            while (Run)
            {
                if (SyncList.TryTake(out var user))
                {
                    // 새로운 유저인가
                    if (userList.Find(u => u == user) == null)
                    {
                        userList.Add(user);
                    }
                    for (int i = 0; i < 1000; i++)
                    {
                        user.DefferedSend(Encoding.UTF8.GetBytes($"New Sync{user.SyncID} Request?{i} Take This!"));
                        user.Dispatch();
                    }
                }

                // 서버 로직
                foreach (var u in userList)
                {
                    if (u.PacketCompleteQueue.TryDequeue(out var packet))
                    {
                        Logger.DebugLog(Encoding.UTF8.GetString(packet.Span));
                    }
                }
            }

            //4. 메모리 해제
            Server.Dispose();
        }
    }
}