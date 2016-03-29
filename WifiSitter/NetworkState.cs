using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace WifiSitter
{
    /// <summary>
    /// Class used to track state of detected network adapters
    /// </summary>
    public class NetworkState
    {
        #region fields
        private List<SitterNic> _nics;
        private bool _checkNet;
        private bool _netAvailable;
        private bool _processingState;
        private string[] _ignoreAdapters;  // List of Nic names to ignore during normal operation
        #endregion // fields


        #region constructor

        public NetworkState(string[] NicWhitelist) {
            if (NicWhitelist == null)
                NicWhitelist = new string[] { };
            this.Nics = QueryNetworkAdapters(NicWhitelist);
            _ignoreAdapters = NicWhitelist;
            Initialize();
        }

        public NetworkState(List<SitterNic> Nics, string[] NicWhitelist) {
            this.Nics = Nics;
            _ignoreAdapters = NicWhitelist;
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

        public void StateChecked() {
            this.CheckNet = false;
            this.ProcessingState = false;
        }

        public void UpdateNics(List<SitterNic> Nics) {
            this.Nics = Nics;
        }
        
        internal static List<SitterNic> QueryNetworkAdapters(string[] WhiteList) {
            List<SitterNic> result = new List<SitterNic>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                                                                  && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                                                                                  && !x.Description.ToLower().Contains("bluetooth")
                                                                                  && !WhiteList.Any(y => x.Description.StartsWith(y))))) {
                result.Add(new SitterNic(n));
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

        public List<SitterNic> Nics {
            get {
                if (_nics == null) return new List<SitterNic>();
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
}
