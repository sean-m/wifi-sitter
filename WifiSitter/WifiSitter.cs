using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Threading;
using System.ServiceProcess;

namespace WifiSitter
{
    public class WifiSitter : AbstractService
    {
        #region fields

        internal volatile static NetworkState netstate;
        private const string _serviceName = "WifiSitter";
        private Guid _uninstGuid;
        private Thread _thread;
        private ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
        private volatile bool _paused;

        #endregion // fields


        #region constructor

        public WifiSitter () : base(_serviceName) {
            _paused = false;
            if (this.ServiceExecutionMode != ServiceExecutionMode.Console) {
                this.AutoLog = true;
                this.CanPauseAndContinue = true;
            }
        }

        #endregion // constructor


        #region properties

        public override string DisplayName {
            get {
                return _serviceName;
            }
        }
        
        protected override Guid UninstallGuid {
            get {
                System.Guid.TryParse("23a42c57-a16c-4b93-a5cb-60cff20c1f7a", out _uninstGuid);
                return _uninstGuid;
            }
        }

        #endregion // properties


        #region methods

        /// <summary>
        /// Do initial nic discovery and netsh trickery
        /// </summary>
        private static void Intialize() {
            try {
                Console.WindowWidth = 120;
            }
            catch {
                LogLine(ConsoleColor.Red, "Failed to resize console window.");
            }

            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            netstate = new NetworkState(DiscoverAllNetworkDevices(false));

            LogLine("Initialized...");
        }


        public static List<SitterNic> DiscoverAllNetworkDevices(bool quiet = true) {
            if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering all devices.");

            var nics = NetworkState.QueryNetworkAdapters();
            List<SitterNic> nicsPost;
            var netsh = NetshHelper.GetInterfaces();

            var notInNetstate = netsh.Where(x => !(nics.Select(y => y.Nic.Name).Contains(x.InterfaceName))).ToList();

            if (notInNetstate.Count > 0) {
                if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering disabled devices.");
                var disabledInterfaces = notInNetstate.Where(x => x.AdminState == "Disabled").ToArray();

                // Turn on disabled interfaces
                foreach (var nic in disabledInterfaces) {
                    if (!nic.InterfaceName.Contains("VirtualBox")) // TODO make this configurable via registry
                        NetshHelper.EnableInterface(nic.InterfaceName);
                }

                // Query for network interfaces again
                nicsPost = NetworkState.QueryNetworkAdapters();

                // Disable nics again
                foreach (var nic in disabledInterfaces) {
                    NetshHelper.DisableInterface(nic.InterfaceName);
                }

                // Update the state on UberNic objects
                foreach (var n in nicsPost) {
                    n.UpdateState(netsh.Where(x => x.InterfaceName == n.Name).FirstOrDefault());
                }

                return nicsPost;
            }
            
            // Detected no disabled nics, so update accordingly.
            foreach (var nic in nics) {
                nic.UpdateState(netsh.Where(x => x.InterfaceName == nic.Nic.Name).FirstOrDefault());
            }

            return nics;
        }
        
        public static void LogLine(params string[] msg) {
            LogLine(ConsoleColor.White, msg);
        }

        public static void LogLine(ConsoleColor color, params string[] msg) {
            if (msg.Length == 0) return;
            Console.ForegroundColor = color;
            string log = msg.Length > 0 ? String.Format(msg[0], msg.Skip(1).ToArray()) : msg[0];
            Console.WriteLine("{0}  {1}", DateTime.Now.ToString(), log);
            Console.ResetColor();
        }


        public void WriteLog(LogType type, params string[] msg) {

            if (this.ServiceExecutionMode == ServiceExecutionMode.Console) {
                // Log to console
                ConsoleColor color = Console.ForegroundColor;
                switch (type) {
                    case LogType.error:
                        color = ConsoleColor.Red;
                        break;
                    case LogType.warn:
                        color = ConsoleColor.Yellow;
                        break;
                    case LogType.success:
                        color = ConsoleColor.Green;
                        break;
                    default:
                        // Do nothing
                        break;
                }

                LogLine(color, msg);
                return;
            }
            else {
                // Running as service
                // Log to Event Viewer
                // TODO log to event viewer

            }
        }

        private void WorkerThreadFunc() {

            Intialize();
            
            while (!_shutdownEvent.WaitOne(0)) {

                if (_paused) {
                    Thread.Sleep(1000);
                    continue;
                }

                if (netstate.CheckNet) {

                    netstate.ProcessingState = true;

                    netstate.UpdateNics(DiscoverAllNetworkDevices());

                    var wifi = netstate.Nics.Where(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).Where(x => x.Nic.OperationalStatus == OperationalStatus.Up);

                    if (netstate.NetworkAvailable) { // Network available
                        if (netstate.EthernetUp) { // Ethernet is up
                            if (wifi != null) {
                                foreach (var adapter in wifi) {
                                    WriteLog (LogType.warn, "Disable adaptor: {0,18}  {1}", adapter.Name, adapter.Description);

                                    adapter.Disable();
                                }
                            }
                        }
                    }
                    else { // Network unavailable, enable wifi adapters
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        bool enablingWifi = false;
                        foreach (var nic in netstate.Nics.Where(x => !x.IsEnabled
                                                                 && x.Nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet
                                                                 && x.Nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet3Megabit
                                                                 && x.Nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetT)) {

                            WriteLog (LogType.warn, "Enable adaptor: {0,18}  {1}", nic.Name, nic.Description);

                            bool enableResult = nic.Enable();
                            if (!enableResult) LogLine(ConsoleColor.Red, "Failed to enable NIC {0}", nic.Name);
                            if (enableResult && !enablingWifi) enablingWifi = true; // indicate that a wifi adapter has been successfully enabled
                        }

                        if (enablingWifi) {
                            Thread.Sleep(2 * 1000);
                        }
                    }


                    // Show network availability
                    var color = netstate.NetworkAvailable ? ConsoleColor.Green : ConsoleColor.Red;
                    var stat = netstate.NetworkAvailable ? "is" : "not";

                    LogLine(color, "Connection {0} available", stat);

                    // List adapters
                    Console.Write("\n");
                    Console.WriteLine("{0,32} {1,48}  {2,16}  {3}  {4}\n", "Name", "Description", "Type", "Connected", "Enabled");
                    foreach (var adapter in netstate.Nics) {
                        Console.WriteLine("{0,32} {1,48}  {2,16}  {3,7}  {4,7}", adapter.Name, adapter.Description, adapter.Nic.NetworkInterfaceType, adapter.IsConnected, adapter.IsEnabled);
                    }
                    Console.WriteLine("\n");
                    
                    netstate.StateChecked();

                    Thread.Sleep(1000);
                }
            }            
        }

        #endregion // methods


        #region events

        protected override void OnStartImpl(string[] args) {
            _thread = new Thread(WorkerThreadFunc);
            _thread.Name = "WifiSitter Main Loop";
            _thread.IsBackground = true;
            _thread.Start();
        }

        protected override void OnStopImpl() {
            _shutdownEvent.Set();
            _thread.Interrupt();
            if (!_thread.Join(3000)) {
                _thread.Abort();
            }
        }

        protected override void OnPause() {
            base.OnPause();
            this._paused = true;
        }

        protected override void OnContinue() {
            base.OnContinue();
            this._paused = false;
        }

        #endregion // events
    }

    public enum LogType
    {
        info,
        warn,
        error,
        success,
    }
}
