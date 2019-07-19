using NETWORKLIST;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;

using Vanara.PInvoke;
using static Vanara.PInvoke.IpHlpApi;


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
        private static Logger LOG = LogManager.GetCurrentClassLogger();
        private NetworkListManager netManager = new NetworkListManager();

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
                .ForEach(x => _originalNicState.Add(new string[] { x.Id.ToString(), x.IsEnabled.ToString() }));

            _ignoreAdapters = NicWhitelist.ToList();

            Initialize();
        }

        public NetworkState(List<TrackedNic> Nics, string[] NicWhitelist) {
            this.Nics = Nics;

            // Loop through nics and add id:state to _originalNicState list
            Nics.ForEach(x => _originalNicState.Add(new string[] { x.Id.ToString(), x.IsEnabled.ToString() }));

            _ignoreAdapters = NicWhitelist.ToList();
            Initialize();
        }

        private void Initialize() {
            CheckNet = true;
            
            _processingState = true;

            // Register for network change events
            // TODO clean all this up, probably with Rx
            netManager.NetworkAdded += Netevent_NetworkAdded;
            netManager.NetworkDeleted += Netevent_NetworkDeleted;
            netManager.NetworkPropertyChanged += Netevent_NetworkPropertyChanged;
            netManager.NetworkConnectivityChanged += Netevent_NetworkConnectivityChanged;

            this.NetworkStateChanged += NetworkState_NetworkStateChanged;

            // TODO find a better way to do this
            // Check network state every 10 seconds, fixing issue where device is unplugged while laptop is asleep
            //_checkTimer = new Timer();
            //_checkTimer.AutoReset = true;
            //_checkTimer.Interval = 10 * 1000;
            //_checkTimer.Elapsed += async (obj, snd) => {
            //    if (!NetworkInterface.GetIsNetworkAvailable())
            //    {
            //        _nics.ForEach(x => x.CheckYourself());
            //        await Task.Delay(1000);
            //    }
            //};
            //_checkTimer.Start();
        }


        ~NetworkState() {
            netManager.NetworkAdded -= Netevent_NetworkAdded;
            netManager.NetworkDeleted -= Netevent_NetworkDeleted;
            netManager.NetworkPropertyChanged -= Netevent_NetworkPropertyChanged;
            netManager.NetworkConnectivityChanged -= Netevent_NetworkConnectivityChanged;
            netManager = null;
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
                if (!_originalNicState.Any(x => Guid.Parse(x[0]) == n.Id)) _originalNicState.Add(new string[] { n.Id.ToString(), n.IsEnabled.ToString() });
            }

            this.Nics = Nics;
        }

        public void UpdateWhitelist(List<string> Whitelist) {
            _ignoreAdapters = Whitelist;
            _checkNet = true;
        }

        public List<TrackedNic> QueryNetworkAdapters(IEnumerable<string> WhiteList) {
            List<TrackedNic> result = new List<TrackedNic>();
            if (WhiteList == null) WhiteList = new List<string>();

            var nicInfo = new Dictionary<string, IfRow>();
            try
            {
                LoadInterfaceState(nicInfo);

                foreach (var n in nicInfo.Values)
                {
                    if (WhiteList.Any(y => n.Description.StartsWith(y))) { continue; }
                    result.Add(new TrackedNic(n));
                }
            }
            catch (Exception e)
            {
                LOG.Log(LogLevel.Error, e);
            }
            
            foreach (var i in result) i.CheckYourself();

            return result;
        }


        /// <summary>
        /// Uses iphlpapi.dll and COM NetworkListManager to query for network interface information
        /// and network connection status.
        /// </summary>
        /// <param name="collection"></param>
        private void LoadInterfaceState(Dictionary<string, IfRow> collection)
        {
            List<string> attr = new List<string>() { "InterfaceLuid", "InterfaceIndex", "InterfaceGuid", "Alias", "Description", "Type", "TunnelType", "MediaType", "PhysicalMediumType", "AccessType", "DirectionType", "InterfaceAndOperStatusFlags", "OperStatus", "AdminStatus", "MediaConnectState", "NetworkGuid", "ConnectionType" };
            void CopyMibPropertiesTo<T, TU>(T source, TU dest)
            {
                foreach (var n in attr)
                {
                    bool isField = false;
                    MemberInfo sprop = typeof(T).GetProperty(n);
                    if (sprop == null)
                    {
                        sprop = typeof(T).GetField(n);
                        if (sprop != null) isField = true;
                    }
                    var dprop = typeof(TU).GetProperty(n);

                    if (dprop.CanWrite)
                    { // check if the property can be set or no.
                        if (isField) dprop.SetValue(dest, ((FieldInfo)sprop)?.GetValue(source), null);
                        else dprop.SetValue(dest, ((PropertyInfo)sprop)?.GetValue(source, null), null);
                    }
                }
            }


            Vanara.PInvoke.Win32Error err = Vanara.PInvoke.Win32Error.ERROR_SUCCESS;
            err = GetIfTable2(out MIB_IF_TABLE2 ifTable);
            try
            {
                var nets = netManager.GetNetworkConnections();

                err.ThrowIfFailed("Error enumerating network interfaces.");
                foreach (var f in ifTable)
                {
                    var row = new IfRow();
                    CopyMibPropertiesTo(f, row);
                    row.InterfaceLuid = f.InterfaceLuid.ToString();
                    row.IsConnected = false;
                    row.IsConnectedToInternet = false;

                    foreach (INetworkConnection n in nets)
                    {
                        if (row.InterfaceGuid is Guid)
                        {
                            if (row.InterfaceGuid == n.GetAdapterId())
                            {
                                row.IsConnected = n.IsConnected;
                                row.IsConnectedToInternet = n.IsConnectedToInternet;
                            }
                        }

                    }
                    collection.Add(f.InterfaceLuid.ToString(), row);
                }
            } // Should be handled in caller
            finally
            {
                IntPtr ptr = ifTable?.DangerousGetHandle() ?? IntPtr.Zero;
                if (ptr != IntPtr.Zero) FreeMibTable(ptr);
            }
        }

        #endregion // methods


        #region properties

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsEthernetUp {
            get {
                if (Nics == null) return false;
                return Nics.Any(x => x.InterfaceType == NetworkInterfaceType.Ethernet && x.IsConnected && (bool)!_ignoreAdapters?.Any(y => x.Description.StartsWith(y)));
            }
        }

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsWirelessUp {
            get {
                if (Nics == null) return false;
                return Nics.Any(x => x.InterfaceType == NetworkInterfaceType.Wireless80211 
                    && x.IsConnected 
                    && (bool)!_ignoreAdapters?.Any(y => x.Description.StartsWith(y)));
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
        
        internal event EventHandler<WSNetworkChangeEventArgs> NetworkStateChanged;

        internal virtual void OnNetworkChanged(WSNetworkChangeEventArgs e)
        {
            NetworkStateChanged.Invoke(this, e);
        }

        private void NetworkState_NetworkStateChanged(object sender, WSNetworkChangeEventArgs e)
        {
            LOG.Log(LogLevel.Info, $"Network change detected, ID {e.Id}  { Enum.GetName(e.ChangeType.GetType(), e.ChangeType) }");
        }


        /*=====   These all feed events to the handler above, bound in the Initialize method.       =====*/
        private void Netevent_NetworkAdded(Guid networkId) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.Added));
        private void Netevent_NetworkDeleted(Guid networkId) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.Deleted));
        private void Netevent_NetworkPropertyChanged(Guid networkId, NLM_NETWORK_PROPERTY_CHANGE Flags) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.PropertyChanged));
        private void Netevent_NetworkConnectivityChanged(Guid networkId, NLM_CONNECTIVITY newConnectivity) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.ConnectivityChanged));
        
        #endregion // eventhandlers
    }


    internal class WSNetworkChangeEventArgs : EventArgs
    {
        public Guid Id { get; private set; }

        public NetworkChanges ChangeType { get; private set; }

        public WSNetworkChangeEventArgs(Guid id, NetworkChanges change)
        {
            Id = id;
            ChangeType = change;
        }
    }

    enum NetworkChanges
    {
        Added,
        Deleted,
        PropertyChanged,
        ConnectivityChanged
    }

    internal class IfRow
    {
        public dynamic InterfaceLuid { get; set; }
        public dynamic InterfaceIndex { get; set; }
        public dynamic InterfaceGuid { get; set; }
        public dynamic Alias { get; set; }
        public dynamic Description { get; set; }
        public dynamic PhysicalAddress { get; set; }
        public dynamic Type { get; set; }
        public dynamic TunnelType { get; set; }
        public dynamic MediaType { get; set; }
        public dynamic PhysicalMediumType { get; set; }
        public dynamic InterfaceAndOperStatusFlags { get; set; }
        public IF_OPER_STATUS OperStatus { get; set; }
        public NET_IF_ADMIN_STATUS AdminStatus { get; set; }
        public NET_IF_MEDIA_CONNECT_STATE MediaConnectState { get; set; }
        public Guid NetworkGuid { get; set; }
        public NET_IF_CONNECTION_TYPE ConnectionType { get; set; }
        public bool IsConnected { get; set; }
        public bool IsConnectedToInternet { get; set; }
    }
}
