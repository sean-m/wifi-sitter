using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Threading;

namespace WifiSitter
{
    class Program
    {
        internal static NetworkState netstate;

        static void Main(string[] args) {

            // Provision state
            Intialize();

            // May use this to excape loop based on events
            bool go = true;

            while (go) {
                Thread.Sleep(100);

                if (netstate.CheckNet) {
                    
                    var _nics = netstate.Nics;

                    // Show network availability
                    var color = netstate.NetworkAvailable ? ConsoleColor.Green : ConsoleColor.Red;
                    var stat = netstate.NetworkAvailable ? "is" : "not";
                    Console.ForegroundColor = color;
                    Console.WriteLine("{0}", String.Format("{0}  Connection {1} available", DateTime.Now.ToString(), stat));
                    Console.ResetColor();

                    // List adapters
                    Console.Write("\n");
                    Console.WriteLine("{0,32} {1,48}  {2,16}  {3}", "Name", "Description", "Type", "State"); 
                    foreach (var adapter in _nics) {                        
                        Console.WriteLine("{0,32} {1,48}  {2,16}  {3}", adapter.Name, adapter.Description, adapter.Nic.NetworkInterfaceType, adapter.Nic.OperationalStatus);
                    }


                    var wifi = _nics.Where(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).Where(x => x.Nic.OperationalStatus == OperationalStatus.Up);

                    if (netstate.NetworkAvailable) { // Network available
                        if (netstate.EthernetUp) { // Ethernet is up
                            if (wifi != null) {
                                foreach (var adapter in wifi) {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("{0}  Disable adaptor: {1,18}  {2}", DateTime.Now.ToString(), adapter.Name, adapter.Description);  // TODO log this
                                    Console.ResetColor();
                                    NetshHelper.DisableInterface(adapter.Name);
                                    NetshHelper.DisableInterface(adapter.Name);
                                }
                            }
                        }                      
                    }
                    else { // Network unavailable, enable wifi adapters
                        Console.ForegroundColor = ConsoleColor.Yellow;

                        foreach (var nic in _nics.Where(x => !x.IsEnabled
                                                                 && x.Nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet
                                                                 && x.Nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet3Megabit
                                                                 && x.Nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetT)) {

                            Console.WriteLine("{0}  Enable adaptor: {1,18}  {2}", DateTime.Now.ToString(), nic.Name, nic.Description);  //  TODO log this
                            NetshHelper.EnableInterface(nic.Name);
                        }

                        Console.ResetColor();
                    }

                    Console.WriteLine("\n");

                    netstate.StateChecked();
                    
                    if (!netstate.NetworkAvailable) {
                        Thread.Sleep(2000);
                        if (!netstate.NetworkAvailable) {
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine("{0}  Connection not available after fipping nics around.", DateTime.Now.ToString());  // TODO log this
                            Console.ResetColor();
                            DiscoverDisabledDevices();
                        }
                    }
                }
            }

        }


        /// <summary>
        /// Do initial nic discovery and netsh trickery
        /// </summary>
        private static void Intialize()
        {
            try {
                Console.WindowWidth = 120;
            }
            catch {
                // TOTO log this
            }

            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            DiscoverDisabledDevices();
            
            Console.WriteLine("{0}  Initialized...", DateTime.Now.ToString());
            // TODO log this
        }
        

        private static void DiscoverDisabledDevices() {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("{0}  Discovering disabled devices.", DateTime.Now.ToString());
            Console.ResetColor();

            var nics = NetworkState.QueryNetworkAdapters();
            List<UberNic> nicsPost;
            var netsh = NetshHelper.GetInterfaces();

            var notInNetstate = netsh.Where(x => !(nics.Select(y => y.Nic.Name).Contains(x.InterfaceName))).ToList();

            if (notInNetstate.Count > 0) {
                var disabledInterfaces = notInNetstate.Where(x => x.AdminState == "Disabled").ToArray();

                // Turn on disabled interfaces
                foreach (var nic in disabledInterfaces) {
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
                
                if (netstate == null) {
                    netstate = new NetworkState(nicsPost);
                }
                else {
                    netstate.UpdateNics(nicsPost);
                }

                return;
            }
            
            // Detected no disabled nics, so update accordingly.
            foreach (var n in nics) {
                n.UpdateState(netsh.Where(x => x.InterfaceName == n.Nic.Name).FirstOrDefault());
            }
            
            if (netstate == null) { netstate = new NetworkState(nics); } else { netstate.UpdateNics(nics); }
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
        private System.Timers.Timer _checkTick;
        private uint _ticks;
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

            _ticks = 0;
            _checkTick = new System.Timers.Timer();
            _checkTick.Interval = 20 * 1000;
            _checkTick.AutoReset = true;
            _checkTick.Elapsed += _checkTick_Elapsed;
            _checkTick.Start();
        }

        ~NetworkState() {
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged; ;
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
            _checkTick.Elapsed -= _checkTick_Elapsed;
        }

        #endregion // constructor


        #region methods

        public void StateChecked () {
            this.CheckNet = false;
        }

        public void UpdateNics (List<UberNic> Nics) {
            this.Nics = Nics;
        }

        internal static List<UberNic> QueryNetworkAdapters() {
            List<UberNic> result = new List<UberNic>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback 
                                                                                  && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                                                                                  && !x.Description.ToLower().Contains("bluetooth")
                                                                                  && !x.Description.StartsWith("Microsoft Wi-Fi Direct")))) {
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
            private set {
                _nics = value;
            }
        }

        public bool CheckNet {
            get {
                return _checkNet;
            }
            private set {
                _checkNet = value;
            }
        }
        
        public bool NetworkAvailable {
            get {
                return _netAvailable;
            }
            private set { _netAvailable = value; }
        }

        #endregion // properties


        #region eventhandlers

        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("{0}  Network availability changed.", DateTime.Now.ToString());
            Console.ResetColor();
            _netAvailable = NetworkInterface.GetIsNetworkAvailable();
            _nics = QueryNetworkAdapters();
            _checkNet = true;            
        }

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("{0}  Network address changed.", DateTime.Now.ToString());
            Console.ResetColor();
            _netAvailable = NetworkInterface.GetIsNetworkAvailable();
            _nics = QueryNetworkAdapters();
            _checkNet = true;
        }
        
        private void _checkTick_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var _currNics = QueryNetworkAdapters();
            var _checkNicsIds = _currNics.Select(x => x.Id).ToArray();
            var _currNicIds = _nics.Select(x => x.Id).ToArray();

            var diffCheck = from a in _checkNicsIds
                       join b in _currNicIds on a equals b
                       select a;
            var diff = diffCheck.ToArray();

            if (!(_checkNicsIds.Count() == _currNicIds.Count()
                && diff.Count() == _checkNicsIds.Count()))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("{0}  Network adapter set changed.", DateTime.Now.ToString());
                Console.ResetColor();
                this.UpdateNics(_currNics);
                this.CheckNet = true;
            }
            
            if (_ticks > 200) {
                _ticks = 0;
                System.GC.Collect();
            }
            _ticks++;
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
                _isEnabled = (NetshIf.State == "Enabled");
            }
        }

        #endregion // methods
    }
}
