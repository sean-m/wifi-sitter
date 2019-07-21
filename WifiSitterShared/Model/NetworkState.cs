﻿using NETWORKLIST;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;

using WifiSitter;

using Vanara.PInvoke;
using static Vanara.PInvoke.IpHlpApi;
using ConsoleTableExt;
using System.Diagnostics;
using static NativeWifi.Wlan;

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
        private List<(Guid, bool)> _originalNicState = new List<(Guid, bool)>();
        private Timer _checkTimer;
        private static Logger LOG = LogManager.GetCurrentClassLogger();
        private NetworkListManager netManager = new NetworkListManager();
        private IDisposable netChangeObservable;
        private List<WSNetworkChangeEventArgs> reccentEvents = new List<WSNetworkChangeEventArgs>();
        private object eventLock = new object();

        #endregion // fields


        #region constructor
        public NetworkState() {
            Initialize();
        }

        public NetworkState(IEnumerable<string> NicWhitelist) {

            _ignoreAdapters = NicWhitelist.ToList() ?? new List<string>();
            this.Nics = QueryNetworkAdapters();

            // Loop through nics and add id:state to _originalNicState list
            Nics.Where(x => !NicWhitelist.Any(y => x.Description.StartsWith(y))).ToList()
                .ForEach(x => _originalNicState.Add( (x.Id, x.IsEnabled) ));

            Initialize();
        }

        internal void Initialize() {
            CheckNet = true;
            
            _processingState = true;

            // Register for network change events
            // TODO clean all this up, probably with Rx
            netManager.NetworkAdded += Netevent_NetworkAdded;
            netManager.NetworkDeleted += Netevent_NetworkDeleted;
            netManager.NetworkPropertyChanged += Netevent_NetworkPropertyChanged;
            netManager.NetworkConnectivityChanged += Netevent_NetworkConnectivityChanged;

            // TODO refactor all of this into main loop in WifiSitter.cs
            netChangeObservable = Observable.FromEventPattern<WSNetworkChangeEventArgs>(this, nameof(NetworkStateChanged))
                .Select(x => { lock (eventLock) { reccentEvents.Add(x.EventArgs); } return x; })
                .Throttle(TimeSpan.FromSeconds(4))
                .Subscribe(
                (_) =>
                {
                LOG.Log(LogLevel.Info, "Throttle started");
                List<WSNetworkChangeEventArgs> _events;
                lock (eventLock)
                {
                    _events = reccentEvents;
                    reccentEvents = new List<WSNetworkChangeEventArgs>();
                }

                foreach (var e in _events)
                {
                    LOG.Log(LogLevel.Info, $">> {e.EventTime.Ticks}  {e.Id}  {e.ChangeType.ToString()} ");
                }

                    // Update connection status and wlan profile info
                    var _nic_list = QueryNetworkAdapters().Select(
                        _nic =>
                        {
                            var matching_nic = Nics.Where(n => n.Id == _nic.Id).FirstOrDefault();
                            if (!(matching_nic.LastWirelessConnection.Equals(default(WlanConnectionAttributes)) && _nic.Equals(default(WlanConnectionAttributes)))) {
                                _nic.LastWirelessConnection = matching_nic.LastWirelessConnection;
                            }
                            else if (!(_nic.Equals(default(WlanConnectionAttributes))))
                            {
                                _nic.LastWirelessConnection = matching_nic.LastWirelessConnection;
                            }
                            return _nic;
                        })
                    .ToList();

                    Nics = _nic_list;

                    var table = ConsoleTableBuilder
                       .From(Nics.Where(x => !_ignoreAdapters.Any(y => x.Description.StartsWith(y))).ToList())
                       .WithFormat(ConsoleTableBuilderFormat.Default)
                       .Export();

                    LOG.Log(LogLevel.Debug, $"\n{table.ToString()}");

                    // TODO Track when actions were last taken to avoid nic flapping
                    if (IsEthernetInternetConnected && IsWirelessInternetConnected)
                    {
                        LOG.Log(LogLevel.Warn, "Both Wired and Wireless connections are internet routable, assume proper dual homing, kick wireless.");
                        var wnics = Nics.Where(x => !_ignoreAdapters.Any(y => x.Description.StartsWith(y)))
                            .Where(x => x.InterfaceType == NetworkInterfaceType.Wireless80211 && x.IsInternetConnected);

                        foreach (var n in wnics)
                        {
                            try
                            {
                                LOG.Log(LogLevel.Info, $"Releasing IP on adapter: {n.Id}");
                                ReleaseIp(n);
                                WlanDisconnect(n);
                            }
                            catch (Exception ex)
                            {
                                LOG.Log(LogLevel.Error, ex);
                            }
                        }
                    }
                    else if (!IsInternetConnected)
                    {
                        LOG.Log(LogLevel.Warn, "No internet connection available, light 'em up!");
                        var wnics = Nics.Where(x => !_ignoreAdapters.Any(y => x.Description.StartsWith(y)))
                            .Where(x => x.InterfaceType == NetworkInterfaceType.Wireless80211 && (!x.IsConnected));

                        foreach (var n in wnics)
                        {
                            ConnectToLastSsid(n);
                        }
                    }
                    else
                    {
                        LOG.Log(LogLevel.Info, "We can get to the internet, nothing to see here.");
                    }
                    
                });


            //Nics = QueryNetworkAdapters();

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
            netChangeObservable.Dispose();
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

        /// <summary>
        /// Update internal Nic list.
        /// </summary>
        /// <param name="Nics"></param>
        public void UpdateNics(List<TrackedNic> Nics) {
            foreach (var n in Nics) {
                if (!_originalNicState.Any(x => x.Item1 == n.Id)) _originalNicState.Add( (n.Id, n.IsEnabled) );
            }

            this.Nics = Nics;
        }

        public void UpdateWhitelist(List<string> Whitelist) {
            _ignoreAdapters = Whitelist;
            _checkNet = true;
        }

        // DEPRECATED remove later
        /// <summary>
        /// Query NetworkListManager for connection status for specified adapter.
        /// </summary>
        /// <param name="AdapterId"></param>
        /// <returns></returns>
        public bool QueryNetworkAdapter(Guid AdapterId)
        {
            var tnic = Nics.Where(x => x.Id == AdapterId).FirstOrDefault();
            if (tnic == null) return false;

            try
            {
                var connection = netManager.GetNetworkConnection(AdapterId);
                if (connection == null)
                {
                    tnic.ConnectionStatus = ConnectionState.Unknown;
                }
                else
                {
                    if (connection.IsConnected) tnic.ConnectionStatus = tnic.ConnectionStatus | ConnectionState.Connected;
                    if (connection.IsConnectedToInternet) tnic.ConnectionStatus = tnic.ConnectionStatus | ConnectionState.InternetConnected;
                }
                return true;
            }
            catch (Exception e)
            {

            }
            return false;
        }

        public List<TrackedNic> QueryNetworkAdapters() {
            List<TrackedNic> result = new List<TrackedNic>();

            var nicInfo = new Dictionary<string, IfRow>();
            try
            {
                LoadInterfaceState(nicInfo);
                var wclient = new NativeWifi.WlanClient();

                foreach (var n in nicInfo.Values)
                {
                    if (_ignoreAdapters?.Any(y => n.Description.StartsWith(y)) ?? false) { continue; }
                    var nic = new TrackedNic(n);
                    if (nic.InterfaceType == NetworkInterfaceType.Wireless80211 && nic.IsConnected)
                    {
                        // Check if it's connected and what the wireless profile is. May need this later.
                        var adapter = wclient.Interfaces.Where(x => x.InterfaceGuid == nic.Id).FirstOrDefault();
                        if (adapter == null)
                        {
                            LOG.Log(LogLevel.Error, $"Cannot find wlan adapter with ID {nic.Id}");
                        }
                        else
                        {
                            if (adapter.CurrentConnection.Equals(default(WlanConnectionAttributes))) nic.LastWirelessConnection = adapter.CurrentConnection;
                        }
                    }
                    result.Add(nic);
                }
            }
            catch (Exception e)
            {
                LOG.Log(LogLevel.Error, e);
            }

            return result;
        }


        /// <summary>
        /// Uses iphlpapi.dll and COM NetworkListManager to query for network interface information
        /// and network connection status.
        /// </summary>
        /// <param name="collection"></param>
        private void LoadInterfaceState(Dictionary<string, IfRow> collection)
        {
            List<string> attr = new List<string>() { "InterfaceLuid", "InterfaceIndex", "InterfaceGuid", "Alias", "Description", "Type", "TunnelType", "MediaType", "PhysicalMediumType", "InterfaceAndOperStatusFlags", "OperStatus", "AdminStatus", "MediaConnectState", "NetworkGuid", "ConnectionType", "InterfaceIndex" };
            void CopyMibPropertiesTo<T, TU>(T source, TU dest)
            {
                foreach (var a in attr)
                {
                    bool isField = false;
                    MemberInfo sprop = typeof(T).GetProperty(a);
                    if (sprop == null)
                    {
                        sprop = typeof(T).GetField(a);
                        if (sprop != null) isField = true;
                    }
                    var dprop = typeof(TU).GetProperty(a);
                    try
                    {
                        if (dprop.CanWrite)
                        { // check if the property can be set or no.
                            if (isField) dprop.SetValue(dest, ((FieldInfo)sprop)?.GetValue(source), null);
                            else dprop.SetValue(dest, ((PropertyInfo)sprop)?.GetValue(source, null), null);
                        }
                    }
                    catch
                    {
                        LOG.Log(LogLevel.Error, $"Error copying attribute: {a}");
                    }
                }
            }


            Vanara.PInvoke.Win32Error err = Vanara.PInvoke.Win32Error.ERROR_SUCCESS;
            err = GetIfTable2(out MIB_IF_TABLE2 ifTable);
            try
            {
                var nets = netManager.GetNetworkConnections();

                err.ThrowIfFailed("Error enumerating network interfaces.");
                foreach (MIB_IF_ROW2 f in ifTable)
                {
                    var row = new IfRow();
                    CopyMibPropertiesTo(f, row);
                    row.InterfaceLuid = f.InterfaceLuid;
                    row.ConnectionStatus = ConnectionState.Unknown;

                    foreach (INetworkConnection n in nets)
                    {   
                        if (row.InterfaceGuid is Guid)
                        {
                            if (row.InterfaceGuid == n.GetAdapterId())
                            {
                                if (n.IsConnected) row.ConnectionStatus = row.ConnectionStatus | ConnectionState.Connected;
                                if (n.IsConnectedToInternet) row.ConnectionStatus = row.ConnectionStatus | ConnectionState.InternetConnected;
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


        private void NetworkState_NetworkStateChanged(object sender, WSNetworkChangeEventArgs e)
        {
            LOG.Log(LogLevel.Info, $"Network change detected at: {e.EventTime.Ticks}  ID {e.Id}  { Enum.GetName(e.ChangeType.GetType(), e.ChangeType) }");

            // Pend interface query to update information
        }

        /// <summary>
        /// Releases any IPv4 address associated with the adapter. Throws on error.
        /// </summary>
        /// <param name="Nic"></param>
        private void ReleaseIp(TrackedNic Nic)
        {
            Debug.Assert(Nic.InterfaceIndex != default(uint));
            using (var ifInfo = GetInterfaceInfo()) {
                var interface_map = ifInfo.Adapter.Where(x => x.Index == Nic.InterfaceIndex).FirstOrDefault();
                Win32Error err = IpReleaseAddress(ref interface_map);
                err.ThrowIfFailed();
            }
        }

        /// <summary>
        /// Disconnects from any currently connected WiFi network. Throws on error.
        /// </summary>
        /// <param name="Nic"></param>
        public void WlanDisconnect(TrackedNic Nic)
        {
            if (Nic.InterfaceType != NetworkInterfaceType.Wireless80211) return;

            var wclient = new NativeWifi.WlanClient();
            var adapter = wclient.Interfaces.Where(x => x.InterfaceGuid == Nic.Id).FirstOrDefault();
            if (adapter == null)
            {
                LOG.Log(LogLevel.Error, $"Cannot find wlan adapter with ID {Nic.Id}");
            }

            // Store connection info and disconnect
            // * note: Connection info is stored as struct so incurs a copy
            Nic.LastWirelessConnection = adapter.CurrentConnection;
            LOG.Log(LogLevel.Info, $"{Nic.Name}  disconnecting from: {adapter.CurrentConnection.profileName}");
            adapter?.Disconnect();
        }

        /// <summary>
        /// Attempts to reconnect to the last network the given wireless adapter was connected to. This is a best
        /// effort thing and will only log a failure. It's about the best we can do since the last network may not
        /// be in range and doesn't persist across service restarts.
        /// </summary>
        /// <param name="Nic"></param>
        public void ConnectToLastSsid(TrackedNic Nic)
        {
            if (Nic.InterfaceType != NetworkInterfaceType.Wireless80211) return;  // Shouldn't happen but still..

            if (String.IsNullOrEmpty(Nic.LastWirelessConnection.profileName))
            {
                LOG.Warn("No previous connection profile logged. I can't reconnect to nothing...");
                return;
            }

            LOG.Log(LogLevel.Info, $"{Nic.Name} attempting reconnect to: {Nic.LastWirelessConnection.profileName}");
            var wclient = new NativeWifi.WlanClient();
            var adapter = wclient.Interfaces.Where(x => x.InterfaceGuid == Nic.Id).FirstOrDefault();
            adapter.SetProfile(WlanProfileFlags.AllUser, adapter.GetProfileXml(Nic.LastWirelessConnection.profileName), true);
            adapter.ConnectSynchronously(WlanConnectionMode.Profile, Dot11BssType.Any, Nic.LastWirelessConnection.profileName, 30);
        }

        #endregion // methods


        #region properties

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsEthernetInternetConnected {
            get {
                if (Nics == null) return false;
                return Nics.Where(x => (bool)!_ignoreAdapters?.Any(y => x.Description.StartsWith(y)))
                    .Any(nic => nic.InterfaceType == NetworkInterfaceType.Ethernet && nic.IsInternetConnected);
            }
        }

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsWirelessInternetConnected {
            get {
                if (Nics == null) return false;
                return Nics.Where(x => (bool)!_ignoreAdapters?.Any(y => x.Description.StartsWith(y)))
                    .Any(nic => nic.InterfaceType == NetworkInterfaceType.Wireless80211 && nic.IsInternetConnected);
            }
        }

        public bool IsInternetConnected {
            get => netManager.IsConnectedToInternet;
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

        public List<(Guid, bool)> OriginalNicState {
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


        #region events
        
        public event EventHandler<WSNetworkChangeEventArgs> NetworkStateChanged;

        public void OnNetworkChanged(WSNetworkChangeEventArgs e) => NetworkStateChanged?.Invoke(this, e);
        
        /*=====   These all feed events to the handler above, bound in the Initialize method.   =====*/
        private void Netevent_NetworkAdded(Guid networkId) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.Added));
        private void Netevent_NetworkDeleted(Guid networkId) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.Deleted));
        private void Netevent_NetworkPropertyChanged(Guid networkId, NLM_NETWORK_PROPERTY_CHANGE Flags) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.PropertyChanged));
        private void Netevent_NetworkConnectivityChanged(Guid networkId, NLM_CONNECTIVITY newConnectivity) => OnNetworkChanged(new WSNetworkChangeEventArgs(networkId, NetworkChanges.ConnectivityChanged));

        #endregion // events
    }


    public class WSNetworkChangeEventArgs : EventArgs
    {
        public Guid Id { get; set; }

        public NetworkChanges ChangeType { get; set; }

        public DateTime EventTime { get; set; } = DateTime.Now;

        public WSNetworkChangeEventArgs() { }

        public WSNetworkChangeEventArgs(Guid id, NetworkChanges change)
        {
            Id = id;
            ChangeType = change;
        }
    }

    public enum NetworkChanges
    {
        Added,
        Deleted,
        PropertyChanged,
        ConnectivityChanged
    }

    public enum ConnectionState
    {
        Unknown = 0,
        Connected = 1,
        InternetConnected = 2,
    }

    internal class IfRow
    {
        public NET_LUID InterfaceLuid { get; set; }
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
        public ConnectionState ConnectionStatus { get; set; } = 0;
        
        public bool IsConnected { get => ConnectionStatus.HasFlag(ConnectionState.Connected) || ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }

        public bool IsInternetConnected { get => ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }
    }
}