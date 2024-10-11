using System;
using UnityEngine;
namespace NetLibrary.Utils
{
    public class NetLogger
    {
        static public void DebugLog(string message)
        {
            if (DefineFlag.LogEnable)
            {
                if (DefineFlag.UnityLog)
                {
                    Debug.Log(message);
                }
                else
                {
                    Console.WriteLine(message);
                }

            }
        }
    }
}
