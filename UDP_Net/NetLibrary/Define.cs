using System;

namespace NetLibrary
{
    [Flags]
    public enum SessionType : byte
    {
        UDP = 0,
        TimeLined = 1 << 0,
        Arrival = 1 << 1,
        RUDP = 1 << 2
    }

    [Flags]
    public enum SysRPC : byte
    {
        SYNC = 0,
    }



    [Flags]
    public enum Header : byte
    {
        DATA_ID_1 = 1,
        DATA_ID_2 = 2,
        DATA_ID_3 = 3,

        DATAACK_ID_1 = 4,
        DATAACK_ID_2 = 5,
        DATAACK_ID_3 = 6,

        SYN = 7,
        SYNACK = 8,

        DATA = 10,
        DATAACK = 11,
    }

    public enum Params : int
    {
        // 디버그 플래그, 설정 시 절반 확률로 패킷을 버림
        LoggerFlag = 1,
        UnityLogger = 0,
        PacketLoseMode = 0,

        // 소켓 송수신 지원 크기
        MaxReceiveArgsNum = 255,
        MaxSendArgsNum = 255,
        // 수신 패킷의 최대 사이즈
        MaxPacketBlockSize = 512,

        /* 
         * 다음의 Param 은 네트워크 상태에 따라 최적화가 가능하며 
         * Sync 단계에서 주고 받으면 좋을 듯 하다.  - 기능 추가 필요(2024.10.11) 
        */

        // 블록 최대 송수신 개수
        MaxBlockReceiveNum = 10,
        MaxBlockSendNum = 10,
        // 재송신 딜레이 증가량 
        SendDelayIncrease = 10,
        // 재송신 기준 Nak 개수
        NakNum = 3,
    }
}
