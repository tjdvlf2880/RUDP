using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetLibrary.Utils
{
    public class Logger
    {
        static public void DebugLog(string message)
        {
            if ((int)Params.LoggerFlag == 1)
            {
                Console.WriteLine(message);
            }
        }
    }
}
