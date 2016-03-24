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
        internal static List<UberNic> initialNicState;
        internal static NetworkState netstate;

        static void Main(string[] args) {

            Intialize();

            Console.WriteLine("Initialized...");

            bool go = true;

            while (go) {
                Thread.Sleep(100);

                if (netstate.CheckNet) {

                    var _nics = netstate.Nics;

                    var color = netstate.NetworkAvailable ? ConsoleColor.Green : ConsoleColor.Red;
                    var stat = netstate.NetworkAvailable ? "is" : "not";
                    Console.ForegroundColor = color;
                    Console.WriteLine("\n{0,48}", String.Format("Connection {0} available", stat));
                    Console.ResetColor();

                    foreach (var adapter in _nics) {
                        Console.WriteLine("{0,32} {1,48}  {2,16} {3}", adapter.Name, adapter.Description, adapter.Nic.NetworkInterfaceType, adapter.Nic.OperationalStatus);
                    }


                    var wifi = _nics.Where(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).Where(x => x.Nic.OperationalStatus == OperationalStatus.Up);

                    if (netstate.NetworkAvailable) { // Network available
                        if (netstate.EthernetUp) { // Ethernet is up
                            if (wifi != null) {
                                foreach (var adapter in wifi) {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Disable adaptor {0}  {1}", adapter.Name, adapter.Description);
                                    Console.ResetColor();
                                    NetshHelper.DisableInterface(adapter.Name);
                                    NetshHelper.DisableInterface(adapter.Name);
                                }
                            }
                        }                        
                    }
                    else { // Network unavailable                        
                        CheckWifiNicsAndEnable(initialNicState, _nics);
                    }

                    Console.WriteLine("\n");

                    netstate.StateChecked();
                    
                    if (!netstate.NetworkAvailable) {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine("Connection not available after fipping nics around.");
                        Console.ResetColor();
                        Thread.Sleep(1000);
                        DiscoverDisabledDevices();
                    }
                }
            }

        }


        /// <summary>
        /// Do initial nic discovery and netsh trickery
        /// </summary>
        private static void Intialize()
        {
            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            DiscoverDisabledDevices();
        }


        private static void CheckWifiNicsAndEnable(List<UberNic> InitialState, List<UberNic> CurrentState) {
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            
            foreach (var nic in InitialState.Where(x => !x.IsEnabled 
                                                     && !x.Nic.NetworkInterfaceType.HasFlag(NetworkInterfaceType.Ethernet 
                                                                                          | NetworkInterfaceType.Ethernet3Megabit 
                                                                                          | NetworkInterfaceType.FastEthernetT ))) {
                
                Console.WriteLine("Enable adaptor {0}  {1}", nic.Name, nic.Description);
                NetshHelper.EnableInterface(nic.Name);
            }

            Console.ResetColor();            
        }


        private static void DiscoverDisabledDevices() {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Discovering disabled devices.");
            Console.ResetColor();

            var nics = NetworkState.QueryNetworkAdapters();
            var netsh = NetshHelper.GetInterfaces();

            var notInNetstate = netsh.Where(x => !(nics.Select(y => y.Nic.Name).Contains(x.InterfaceName))).ToList();

            if (notInNetstate.Count > 0) {
                var disabledInterfaces = notInNetstate.Where(x => x.AdminState == "Disabled").ToArray();

                foreach (var nic in disabledInterfaces) {
                    NetshHelper.EnableInterface(nic.InterfaceName);
                }

                if (disabledInterfaces.Count() > 0) {
                    var tmpNics = NetworkState.QueryNetworkAdapters();

                    if (tmpNics.Count() != nics.Count()) {

                        netsh = NetshHelper.GetInterfaces();
                        foreach (var n in tmpNics) {
                            n.UpdateState(netsh.Where(x => x.InterfaceName == n.Name).FirstOrDefault());
                        }

                        initialNicState = tmpNics;
                        if (netstate == null) {
                            netstate = new NetworkState(tmpNics);
                        }
                        else {
                            netstate.UpdateNics(tmpNics);
                        }
                    }
                }

                foreach (var nic in disabledInterfaces) {
                    NetshHelper.DisableInterface(nic.InterfaceName);
                }
                return;
            }
            

            foreach (var n in nics) {
                n.UpdateState(netsh.Where(x => x.InterfaceName == n.Nic.Name).FirstOrDefault());
            }

            initialNicState = nics;
            if (netstate == null) { netstate = new NetworkState(nics); } else { netstate.UpdateNics(nics); }
        }        
    }
    

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

        /// <summary>
        /// Constructor, initializes nics list and sets up event handlers.
        /// </summary>
        public NetworkState () {            
            this.Nics =  QueryNetworkAdapters();
            Initialize();
        }

        /// <summary>
        /// Constructor, initializes nics list and sets up event handlers.
        /// </summary>
        /// <param name="Nics"></param>
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
            _checkTick.Interval = 10 * 1000;
            _checkTick.AutoReset = true;
            _checkTick.Elapsed += _checkTick_Elapsed;
            //_checkTick.Start();
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
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel))) {
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
            _netAvailable = NetworkInterface.GetIsNetworkAvailable();
            _nics = QueryNetworkAdapters();
            _checkNet = true;            
        }

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e) {
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
                Console.WriteLine("Network adapter set changed.");
                Console.ResetColor();
                this.UpdateNics(_currNics);
                this.CheckNet = true;
            }
            
            if (_ticks > 1000) {
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
