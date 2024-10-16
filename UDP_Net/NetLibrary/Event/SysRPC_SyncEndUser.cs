﻿using NetLibrary.Utils;
using System.Net;

namespace NetLibrary.Event
{
    public class SysRPC_SyncEndUser : SysRPCArgs
    {
        public IPEndPoint remote;
        public int timeout;
        public Notifier<bool> notifier;
        public EndUser user;
        public SysRPC_SyncEndUser(EndUser user, IPEndPoint remote, int timeout)
        {
            header = SysRPC.SYNC;
            this.user = user;
            this.remote = remote;
            this.timeout = timeout;
            notifier = new Notifier<bool>(1);
        }
    }
}
