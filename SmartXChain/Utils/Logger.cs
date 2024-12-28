using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartXChain.Utils
{ 
    public static class Logger
    {   
        public static void LogMessage(string message)
        {
            if (message.Length > 80)
                Console.WriteLine(message.Substring(0, 80) + "...");
            else
                Console.WriteLine(message);
        }

    }
}
