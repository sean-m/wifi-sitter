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
        

        internal static readonly List<NetworkInterface> initialNicState = NetworkInterface.GetAllNetworkInterfaces().Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel)).ToList();

        static void Main(string[] args) {

            var netstate = new NetworkState();
            
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
                        IPInterfaceProperties properties = adapter.GetIPProperties();
                        Console.WriteLine("{0,48}  {1,16}  {2}", adapter.Description, adapter.NetworkInterfaceType, adapter.OperationalStatus);
                    }


                    var wifi = _nics.Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).Where(x => x.OperationalStatus == OperationalStatus.Up);

                    if (netstate.NetworkAvailable) { // Network available
                        if (netstate.EthernetUp) { // Ethernet is up
                            if (wifi != null) {
                                foreach (var adapter in wifi) {
                                    DisableAdapter(adapter);
                                }
                            }
                        }                        
                    }
                    else { // Network unavailable
                        CheckWifiNicsAndEnable(initialNicState, _nics);
                    }

                    Console.WriteLine("\n");

                    netstate.StateChecked();
                }
            }

        }

        private static void CheckWifiNicsAndEnable(List<NetworkInterface> InitialState, List<NetworkInterface> CurrentState) {
            
            var initialIds = InitialState.Select(x => x.Id).ToArray();
            var currentIds = CurrentState.Select(x => x.Id).ToArray();

            Console.ForegroundColor = ConsoleColor.Yellow;

            foreach (var nic in InitialState) {
                
                if (   initialIds.Contains(nic.Id)
                   && !currentIds.Contains(nic.Id)) {
                    Console.WriteLine("Adapter existed initialy but doesn't now, assuming disabled: {0}", nic.Description);
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
                        EnableAdapter(nic);
                }
            }

            Console.ResetColor();            
        }

        private static void EnableAdapter(NetworkInterface nic) {
            Console.WriteLine("Enable adaptor {0}", nic.Description);
        }

        private static void DisableAdapter(NetworkInterface nic) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Disable adaptor {0}", nic.Description);
            Console.ResetColor();
        }
    }
    
    public class NetworkState
    {
        #region fields
        private List<NetworkInterface> _nics;
        private bool _checkNet;
        private bool _netAvailable;
        private System.Timers.Timer _checkTick;
        #endregion // fields


        #region constructor

        /// <summary>
        /// Constructor, initializes nics list and sets up event handlers.
        /// </summary>
        public NetworkState () {            
            this.Nics = QueryNetworkAdapters();
            Initialize();
        }

        /// <summary>
        /// Constructor, initializes nics list and sets up event handlers.
        /// </summary>
        /// <param name="Nics"></param>
        public NetworkState (List<NetworkInterface> Nics) {
            this.Nics = Nics;
            Initialize();

        }

        private void Initialize() {
            CheckNet = true;
            _netAvailable = NetworkInterface.GetIsNetworkAvailable();
            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged; ;
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;

            _checkTick = new System.Timers.Timer();
            _checkTick.Interval = 5000;
            _checkTick.AutoReset = true;
            _checkTick.Elapsed += _checkTick_Elapsed;
            _checkTick.Start();
        }

        ~NetworkState() {
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged; ;
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
        }

        #endregion // constructor


        #region methods

        public void StateChecked () {
            this.CheckNet = false;
        }

        public void UpdateNics (List<NetworkInterface> Nics) {
            this.Nics = Nics;
        }

        internal static List<NetworkInterface> QueryNetworkAdapters() {
            return NetworkInterface.GetAllNetworkInterfaces().Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel)).ToList();
        }

        #endregion // methods


        #region properties

        public bool EthernetUp {
            get {
                if (Nics == null) return false;

                return Nics.Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                           .Any(x => x.OperationalStatus == OperationalStatus.Up);
            }
        }
        
        public List<NetworkInterface> Nics {
            get {
                if (_nics == null) return new List<NetworkInterface>(); 
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
        }
        
        #endregion // eventhandlers
    }
}
