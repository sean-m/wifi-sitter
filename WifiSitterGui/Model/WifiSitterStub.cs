using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WifiSitter
{
    static class WifiSitter
    {
        public static void LogLine(params string[] msg) {
            LogLine(ConsoleColor.White, msg);
        }

        public static void LogLine(ConsoleColor color, params string[] msg) {
            if (msg.Length == 0) return;
            string log = msg.Length > 0 ? String.Format(msg[0], msg.Skip(1).ToArray()) : msg[0];
            Console.Write(DateTime.Now.ToString());
            Console.ForegroundColor = color;
            Console.WriteLine("  {0}", log);
            Console.ResetColor();
        }

    }
}
