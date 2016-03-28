using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Threading;

namespace WifiSitter
{
    class Program {
        internal static NetworkState netstate;

        static void Main(string[] args) {

            // Provision state
            Intialize();

            // May use this to excape loop based on events
            bool go = true;

            while (go) {
                Thread.Sleep(1000);

                if (netstate.CheckNet) {

                    netstate.ProcessingState = true;

                    netstate.UpdateNics(DiscoverAllNetworkDevices());

                    var wifi = netstate.Nics.Where(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).Where(x => x.Nic.OperationalStatus == OperationalStatus.Up);

                    if (netstate.NetworkAvailable) { // Network available
                        if (netstate.EthernetUp) { // Ethernet is up
                            if (wifi != null) {
                                foreach (var adapter in wifi) {
                                    LogLine (ConsoleColor.Red, "Disable adaptor: {0,18}  {1}", adapter.Name, adapter.Description);  // TODO log this
                                    
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

                            LogLine(ConsoleColor.Yellow, "Enable adaptor: {0,18}  {1}", nic.Name, nic.Description);  //  TODO log this

                            bool enableResult = nic.Enable();
                            if (!enableResult) LogLine (ConsoleColor.Red, "Failed to enable NIC {0}", nic.Name);
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
                    Console.WriteLine("{0,32} {1,48}  {2,16}  {3}  {4}", "Name", "Description", "Type", "State", "Enabled");
                    foreach (var adapter in netstate.Nics) {
                        Console.WriteLine("{0,32} {1,48}  {2,16}  {3,5}  {4,7}", adapter.Name, adapter.Description, adapter.Nic.NetworkInterfaceType, adapter.Nic.OperationalStatus, adapter.IsEnabled);
                    }
                    Console.WriteLine("\n");


                    netstate.StateChecked();
                }
            }

        }


        /// <summary>
        /// Do initial nic discovery and netsh trickery
        /// </summary>
        private static void Intialize() {
            try {
                Console.WindowWidth = 120;
            }
            catch {
                // TODO log this
                LogLine(ConsoleColor.Red, "Failed to resize console window.");
            }

            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            netstate = new NetworkState(DiscoverAllNetworkDevices(false));

            LogLine("Initialized...");
            // TODO log this
        }


        public static List<UberNic> DiscoverAllNetworkDevices(bool quiet = true) {
            if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering all devices.");

            var nics = NetworkState.QueryNetworkAdapters();
            List<UberNic> nicsPost;
            var netsh = NetshHelper.GetInterfaces();

            var notInNetstate = netsh.Where(x => !(nics.Select(y => y.Nic.Name).Contains(x.InterfaceName))).ToList();

            if (notInNetstate.Count > 0) {
                if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering disabled devices.");
                var disabledInterfaces = notInNetstate.Where(x => x.AdminState == "Disabled").ToArray();

                // Turn on disabled interfaces
                foreach (var nic in disabledInterfaces) {
                    if (!nic.InterfaceName.Contains("VirtualBox"))
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
    }
    

    /// <summary>
    /// Object used to track state of detected network adapters
    /// </summary>
    public class NetworkState
    {
        #region fields
        private List<UberNic> _nics;
        private bool _checkNet;
        private bool _netAvailable;
        private bool _processingState;
        #endregion // fields


        #region constructor
        
        public NetworkState () {            
            this.Nics =  QueryNetworkAdapters();
            Initialize();
        }
        
        public NetworkState (List<UberNic> Nics) {
            this.Nics = Nics;
            Initialize();

        }

        private void Initialize() {
            CheckNet = true;
            _netAvailable = NetworkInterface.GetIsNetworkAvailable();
            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged; ;
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;

            _processingState = true;
        }

        ~NetworkState() {
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged; ;
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
        }

        #endregion // constructor


        #region methods

        public void StateChecked () {
            this.CheckNet = false;
            this.ProcessingState = false;
        }

        public void UpdateNics (List<UberNic> Nics) {
            this.Nics = Nics;
        }
        

        internal static List<UberNic> QueryNetworkAdapters () {
            List<UberNic> result = new List<UberNic>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback 
                                                                                  && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                                                                                  && !x.Description.ToLower().Contains("bluetooth")
                                                                                  && !x.Description.StartsWith("Microsoft Wi-Fi Direct")
                                                                                  && !x.Description.StartsWith("VirtualBox Host")))) {
                result.Add(new UberNic(n));
            }
            return result;
        }

        #endregion // methods


        #region properties

        public bool EthernetUp {
            get {
                if (Nics == null) return false;

                return Nics.Where(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                           .Any(x => x.Nic.OperationalStatus == OperationalStatus.Up);
            }
        }
        
        public List<UberNic> Nics {
            get {
                if (_nics == null) return new List<UberNic>(); 
                return _nics;
            }
            private set { _nics = value; }
        }


        public bool CheckNet {
            get { return _checkNet; }
            private set { _checkNet = value; }
        }
        

        public bool NetworkAvailable {
            get { return _netAvailable; }
            private set { _netAvailable = value; }
        }

        
        public bool ProcessingState {
            get { return _processingState; }
            set { _processingState = value; }
        }

        #endregion // properties


        #region eventhandlers

        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("{0}  Network availability changed.", DateTime.Now.ToString());
            Console.ResetColor();
            _netAvailable = NetworkInterface.GetIsNetworkAvailable();
            _checkNet = true;
        }

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("{0}  Network address changed.", DateTime.Now.ToString());
            Console.ResetColor();
            _netAvailable = NetworkInterface.GetIsNetworkAvailable();
            _checkNet = true;
        }
        
        #endregion // eventhandlers
    }


    /// <summary>
    /// Object that contains information from NetworkInterface objects
    /// as well as netsh output (Admin State: Enabled/Disabled).
    /// </summary>
    public class UberNic {
        private NetworkInterface _nic;
        private bool _isEnabled;

        #region constructor
        public UberNic(NetworkInterface Nic) {
            this._nic = Nic;
            _isEnabled = false;
        }

        public UberNic(NetworkInterface Nic, bool IsEnabled) {
            _nic = Nic;
            _isEnabled = IsEnabled;
        }
        #endregion // constructor


        #region properties

        public NetworkInterface Nic {
            get { return _nic; }
        }

        public bool IsEnabled {
            get { return _isEnabled; }
            set { _isEnabled = value; }
        }


        public string Name {
            get { return Nic.Name; }
        }


        public string Description {
            get { return Nic.Description; }
        }


        public string Id {
            get { return Nic.Id; }
        }

        #endregion // properties


        #region methods

        public void UpdateState (List<NetshInterface> NetshIfs) {
            this.UpdateState(NetshIfs.Where(x => x.InterfaceName == Nic.Name).FirstOrDefault());
        }

        public void UpdateState (NetshInterface NetshIf) {
            if (NetshIf == null) return;

            if (Nic.Name == NetshIf.InterfaceName) {
                _isEnabled = (NetshIf.AdminState == "Enabled");                
            }
        }

        public bool Disable()
        {
            // Release IP first and update NIC inforamtion so OperationalState reflects this
            this.ReleaseIp();
            var _iface = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.Id == this.Id).FirstOrDefault();
            if (_iface != null) {
                this._nic = _iface;
            }

            // Disable interface
            int exitCode = EnableDisableInterface(false);

            if (exitCode == 0) {
                this._isEnabled = true;
            }
            else {
                var netsh = NetshHelper.GetInterfaces().Where(x => x.InterfaceName == this.Name).FirstOrDefault();

                if (netsh != null) {
                    this._isEnabled = netsh.AdminState == "Enabled";
                }
                else {
                    this._isEnabled = false;
                }
            }

            return !IsEnabled;
        }

        public bool Enable()
        {
            int exitCode = EnableDisableInterface(true);

            if (exitCode == 0) {
                this._isEnabled = true;
            }
            else {
                var netsh = NetshHelper.GetInterfaces().Where(x => x.InterfaceName == this.Name).FirstOrDefault();

                if (netsh != null) {
                    this._isEnabled = netsh.AdminState == "Enabled";
                }
                else {
                    this._isEnabled = false;
                }
            }

            return IsEnabled;
        }
        
        private int EnableDisableInterface (bool Enable)
        {
            string state = Enable ? "ENABLED" : "DISABLED";

            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "netsh.exe";
            proc.StartInfo.Arguments = String.Format("interface set interface name=\"{0}\" admin={1}", this.Name, state);
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            while (!proc.HasExited) { System.Threading.Thread.Sleep(100); }

            return proc.ExitCode;
        }

        private bool ReleaseIp() {
            //ipconfig /release "Ethernet"

            if (String.IsNullOrEmpty(this.Name)) { throw new ArgumentException("InterfaceName cannot be null or empty"); }

            List<NetshInterface> results = new List<NetshInterface>();
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "ipconfig.exe";
            proc.StartInfo.Arguments = String.Format("/release \"{0}\"", this.Name);
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            while (!proc.HasExited) { System.Threading.Thread.Sleep(100); }

            return proc.ExitCode == 0;
        }

        #endregion // methods
    }
}
