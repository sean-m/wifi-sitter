using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers;

namespace WifiSitter
{
    /// <summary>
    /// Class used to track state of detected network adapters
    /// </summary>
    public class NetworkState {
        #region fields

        private List<TrackedNic> _nics;
        private bool _checkNet;
        private bool _processingState;
        private List<string> _ignoreAdapters;  // List of Nic descriptions to ignore during normal operation
        private List<string[]> _originalNicState = new List<string[]>();
        private Timer _checkTimer;
        private NLog.Logger LOG = NLog.LogManager.GetCurrentClassLogger();

        #endregion // fields


        #region constructor
        public NetworkState() {
            Initialize();
        }

        public NetworkState(string[] NicWhitelist) {
            if (NicWhitelist == null)
                NicWhitelist = new string[] { };
            this.Nics = QueryNetworkAdapters(NicWhitelist);

            // Loop through nics and add id:state to _originalNicState list
            Nics.Where(x => !NicWhitelist.Any(y => x.Description.StartsWith(y))).ToList()
                .ForEach(x => _originalNicState.Add(new string[] { x.Id, x.IsEnabled.ToString() }));

            _ignoreAdapters = NicWhitelist.ToList();

            Initialize();
        }

        public NetworkState(List<TrackedNic> Nics, string[] NicWhitelist) {
            this.Nics = Nics;

            // Loop through nics and add id:state to _originalNicState list
            Nics.ForEach(x => _originalNicState.Add(new string[] { x.Id, x.IsEnabled.ToString() }));

            _ignoreAdapters = NicWhitelist.ToList();
            Initialize();
        }

        private void Initialize() {
            CheckNet = true;
            NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged; ;
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;

            _processingState = true;

            // Check network state every 10 seconds, fixing issue where device is unplugged while laptop is asleep
            _checkTimer = new Timer();
            _checkTimer.AutoReset = true;
            _checkTimer.Interval = 10 * 1000;
            _checkTimer.Elapsed += async (obj, snd) => {
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    _nics.ForEach(x => x.CheckYourself());
                    await Task.Delay(1000);
                }

                NetworkAvailable = NetworkInterface.GetIsNetworkAvailable() && _nics.Any(x => x.IsConnected);

                if (!NetworkAvailable) {
                    LOG.Log(LogLevel.Warn, "Intermittent check failed, network connection unavailable.");
                    this.CheckNet = true;
                }
            };
            _checkTimer.Start();
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

        public void ShouldCheckState() {
            this.CheckNet = true;
        }

        public void UpdateNics(List<TrackedNic> Nics) {
            foreach (var n in Nics) {
                if (!_originalNicState.Any(x => x[0] == n.Id)) _originalNicState.Add(new string[] { n.Id, n.IsEnabled.ToString() });
            }

            this.Nics = Nics;
        }

        public void UpdateWhitelist(List<string> Whitelist) {
            _ignoreAdapters = Whitelist;
            _checkNet = true;
        }

        public static List<TrackedNic> QueryNetworkAdapters(IEnumerable<string> WhiteList) {
            List<TrackedNic> result = new List<TrackedNic>();
            if (WhiteList == null) WhiteList = new List<string>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                    && !x.Description.ToLower().Contains("bluetooth")))) {

                if (WhiteList.Any(y => n.Description.StartsWith(y))) { continue; }

                result.Add(new TrackedNic(n));
            }

            foreach (var i in result) i.CheckYourself();

            return result;
        }

        #endregion // methods


        #region properties

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsEthernetUp {
            get {
                if (Nics == null) return false;
                return Nics.Any(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet && x.IsConnected && (bool)!_ignoreAdapters?.Any(y => x.Nic.Description.StartsWith(y)));
            }
        }

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsWirelessUp {
            get {
                if (Nics == null) return false;
                return Nics.Any(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && x.IsConnected && (bool)!_ignoreAdapters?.Any(y => x.Nic.Description.StartsWith(y)));
            }
        }

        public List<string> IgnoreAdapters {
            get {
                if (_ignoreAdapters == null) _ignoreAdapters = new List<string>();
                return _ignoreAdapters; }
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


        public bool NetworkAvailable { get; private set; }


        public bool ProcessingState {
            get { return _processingState; }
            set { _processingState = value; }
        }

        #endregion // properties


        #region eventhandlers

        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e) {
            LOG.Log(LogLevel.Info, "Event: Network availability changed.");
            NetworkAvailable = NetworkInterface.GetIsNetworkAvailable() && _nics.Any(x => x.IsConnected);
            _checkNet = true;
        }

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e) {
            LOG.Log(LogLevel.Info, "Event: Network address changed.");
            NetworkAvailable = NetworkInterface.GetIsNetworkAvailable() && _nics.Any(x => x.IsConnected);
            _checkNet = true;
        }

        #endregion // eventhandlers
    }
}
