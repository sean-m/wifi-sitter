using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace WifiSitter
{
    class NetshHelper
    {
        public static List<NetShInterface> GetInterfaces()
        {
            List<NetShInterface> results = new List<NetShInterface>();
            var proc = new Process();
            proc.StartInfo.FileName = "netsh.exe";
            proc.StartInfo.Arguments = "interface show interface";
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            string stdout;
            string stderr;

            using (StreamReader stdReader = proc.StandardOutput) {
                using (StreamReader errReader = proc.StandardError) {
                    stdout = stdReader.ReadToEnd();
                    stderr = errReader.ReadToEnd();
                }
            }


            bool threwError = String.IsNullOrEmpty(stderr);
            //TODO handle error condition

            bool startParse = false;
            foreach (var line in stdout.Split(new char[] { '\r', '\n' }).Where(x => !String.IsNullOrEmpty(x)).ToArray()) {

                if (startParse) {
                    string[] tokens = line.Split(null).Where(x => !String.IsNullOrEmpty(x)).ToArray();
                    results.Add(new NetShInterface(tokens[0], tokens[1], tokens[2], tokens[3]));
                }
                else {
                    startParse = line.Trim().StartsWith("------------");
                }
            }

            return results;
        }


        public static bool EnableInterface(string InterfaceName)
        {
            if (String.IsNullOrEmpty(InterfaceName)) { throw new ArgumentException("InterfaceName cannot be null or empty"); }

            List<NetShInterface> results = new List<NetShInterface>();
            var proc = new Process();
            proc.StartInfo.FileName = "netsh.exe";
            proc.StartInfo.Arguments = String.Format("interface set interface name=\"{0}\" admin=ENABLED", InterfaceName);
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            while(!proc.HasExited) { System.Threading.Thread.Sleep(100); }

            return proc.ExitCode == 0;
        }


        public static bool DisableInterface(string InterfaceName)
        {
            if (String.IsNullOrEmpty(InterfaceName)) { throw new ArgumentException("InterfaceName cannot be null or empty"); }

            List<NetShInterface> results = new List<NetShInterface>();
            var proc = new Process();
            proc.StartInfo.FileName = "netsh.exe";
            proc.StartInfo.Arguments = String.Format("interface set interface name=\"{0}\" admin=DISABLED", InterfaceName);
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            while (!proc.HasExited) { System.Threading.Thread.Sleep(100); }

            return proc.ExitCode == 0;
        }


        public static bool ReleaseIp (string InterfaceName)
        {
            //ipconfig /release "Ethernet"

            if (String.IsNullOrEmpty(InterfaceName)) { throw new ArgumentException("InterfaceName cannot be null or empty"); }

            List<NetShInterface> results = new List<NetShInterface>();
            var proc = new Process();
            proc.StartInfo.FileName = "ipconfig.exe";
            proc.StartInfo.Arguments = String.Format("/release \"{0}\"", InterfaceName);
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            while (!proc.HasExited) { System.Threading.Thread.Sleep(100); }

            return proc.ExitCode == 0;
        }
    }

    public sealed class NetShInterface
    {
        // Admin State    State          Type             Interface Name

        private string _adminState;
        private string _state;
        private string _type;
        private string _interfaceName;


        public string AdminState { get { return _adminState; } }
        public string State { get { return _state; } }
        public string Type { get { return _type; } }
        public string InterfaceName { get { return _interfaceName; } }


        public NetShInterface(string AdminState, string State, string Type, string Name)
        {
            _adminState = AdminState;
            _state = State;
            _type = Type;
            _interfaceName = Name;
        }
    }
}
