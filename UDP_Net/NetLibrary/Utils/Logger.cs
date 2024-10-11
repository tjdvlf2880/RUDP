using System;
using Unity;
using UnityEngine;
namespace NetLibrary.Utils
{
    public class Logger
    {
        static public void DebugLog(string message)
        {
            if ((int)Params.LoggerFlag == 1)
            {
                if((int)Params.UnityLogger == 1)
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
