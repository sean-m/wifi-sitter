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
        public static List<NetshInterface> GetInterfaces()
        {
            List<NetshInterface> results = new List<NetshInterface>();
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
            if (!threwError)
                return null;

            bool startParse = false;
            foreach (var line in stdout.Split(new char[] { '\r', '\n' }).Where(x => !String.IsNullOrEmpty(x)).ToArray()) {

                if (startParse) {
                    string[] tokens = line.Split(null).Where(x => !String.IsNullOrEmpty(x)).ToArray();
                    results.Add(new NetshInterface(tokens[0], tokens[1], tokens[2],  String.Join(" ",tokens.Skip(3))));
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

            List<NetshInterface> results = new List<NetshInterface>();
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

            List<NetshInterface> results = new List<NetshInterface>();
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

            List<NetshInterface> results = new List<NetshInterface>();
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


        public static List<SitterNic> DiscoverAllNetworkDevices(List<SitterNic> CurrentAdapters = null, string[] IgnoreNics = null, bool quiet = false) {
            if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering all devices.");

            var nics = (CurrentAdapters == null) ? NetworkState.QueryNetworkAdapters(IgnoreNics) : CurrentAdapters;

            List<SitterNic> nicsPost;
            var netsh = NetshHelper.GetInterfaces();

            List<NetshInterface> notInNetstate = new List<NetshInterface>();

            // Skip checking for disabled adapters we already know about
            foreach (var n in netsh) {
                if (!nics.Any(x => x.Name == n.InterfaceName)) {
                    notInNetstate.Add(n);
                }
            }


            if (notInNetstate.Count > 0) {
                if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering disabled devices.");
                var disabledInterfaces = notInNetstate.Where(x => x.AdminState == "Disabled")
                                                      .Where(x => !nics.Any(y => y.Name == x.InterfaceName)) // Ignore nics we already know about
                                                      .ToArray();

                // Turn on disabled interfaces
                foreach (var nic in disabledInterfaces) {
                    if (!IgnoreNics.Any(x => nic.InterfaceName.StartsWith(x)))
                        NetshHelper.EnableInterface(nic.InterfaceName);
                }

                // Query for network interfaces again
                nicsPost = NetworkState.QueryNetworkAdapters(IgnoreNics);

                // Disable nics again
                foreach (var nic in disabledInterfaces) {
                    NetshHelper.DisableInterface(nic.InterfaceName);
                }

                nics?.AddRange(nicsPost.Where(x => !nics.Any(y => y.Name == x.Name)));

                // Update the state on SitterNic objects
                foreach (var n in nics) {
                    n.UpdateState(netsh?.Where(x => x.InterfaceName == n.Name).FirstOrDefault());
                }

                return nics;
            }

            // Detected no disabled nics, so update accordingly.
            foreach (var nic in nics) {
                nic.UpdateState(netsh?.Where(x => x.InterfaceName == nic.Nic.Name).FirstOrDefault());
            }

            return nics;
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

    public sealed class NetshInterface
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


        public NetshInterface(string AdminState, string State, string Type, string Name)
        {
            _adminState = AdminState;
            _state = State;
            _type = Type;
            _interfaceName = Name;
        }
    }
}
