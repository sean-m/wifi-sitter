using System.Diagnostics;
using System.Linq;
using System;

using WifiSitter;

namespace WifiSitter
{
    class Program
    {
        static void Main(string[] args) { // entry point for cmd
            Configuration.SetOptions(args);

            var isRunning = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location)).Count() > 1;
            string mode = (string)Configuration.GetOption("operating_mode");
            if (isRunning && (mode == "console" || mode == "service")) {
                    Console.WriteLine("WifiSitter already running...\nQuiting in 10 seconds.");
                    System.Threading.Thread.Sleep(10 * 1000);
                    Environment.Exit(7);
            }
            else {
                (new WifiSitter()).Run(args);
            }
        }
    }
}
