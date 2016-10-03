using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace WifiSitter
{
    /// <summary>
    /// Class used to track state of detected network adapters
    /// </summary>
    public class NetworkState {
        #region fields
        private List<TrackedNic> _nics;
        private bool _checkNet;
        private bool _netAvailable;
        private bool _processingState;
        private string[] _ignoreAdapters;  // List of Nic names to ignore during normal operation
        private List<string[]> _originalNicState = new List<string[]>();
        #endregion // fields


        #region constructor

        public NetworkState(string[] NicWhitelist) {
            if (NicWhitelist == null)
                NicWhitelist = new string[] { };
            this.Nics = QueryNetworkAdapters(NicWhitelist);

            // Loop through nics and add id:state to _originalNicState list
            Nics.ForEach(x => _originalNicState.Add(new string[] { x.Id, x.IsEnabled.ToString() }));

            _ignoreAdapters = NicWhitelist;
            Initialize();
        }

        public NetworkState(List<TrackedNic> Nics, string[] NicWhitelist) {
            this.Nics = Nics;

            // Loop through nics and add id:state to _originalNicState list
            Nics.ForEach(x => _originalNicState.Add(new string[] { x.Id, x.IsEnabled.ToString() }));

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

        public void UpdateNics(List<TrackedNic> Nics) {
            foreach (var n in Nics) {
                if (!_originalNicState.Any(x => x[0] == n.Id)) _originalNicState.Add(new string[] { n.Id, n.IsEnabled.ToString() });
            }

            this.Nics = Nics;
        }

        internal static List<TrackedNic> QueryNetworkAdapters(string[] WhiteList) {
            List<TrackedNic> result = new List<TrackedNic>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                                                                  && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                                                                                  && !x.Description.ToLower().Contains("bluetooth")
                                                                                  && !WhiteList.Any(y => x.Description.StartsWith(y))))) {
                result.Add(new TrackedNic(n));
            }
            return result;
        }

        #endregion // methods


        #region properties

        public bool EthernetUp {
            get {
                if (Nics == null) return false;

                return Nics.Any(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet && x.IsConnected);
            }
        }

        public List<TrackedNic> Nics {
            get {
                if (_nics == null) return new List<TrackedNic>();
                return _nics;
            }
            private set { _nics = value; }
        }

        public List<string[]> OriginalNicState {
            get { return _originalNicState; }
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
