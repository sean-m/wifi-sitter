using NETWORKLIST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;

using WifiSitterShared;
using WifiSitter.Model;

using NLog;
using NativeWifi;
using Vanara.PInvoke;
using ConsoleTableExt;

using static NativeWifi.Wlan;
using static Vanara.PInvoke.IpHlpApi;
using System.Runtime.InteropServices;
using System.Management;

namespace WifiSitter
{
    /// <summary>
    /// Class used to track state of detected network adapters
    /// </summary>
    public class NetworkState
    {
        #region fields

        private bool _checkNet;
        private volatile bool _paused = false;
        private IEnumerable<TrackedNic> _nics;
        private IEnumerable<string> _ignoreAdapters;  // List of Nic descriptions to ignore during normal operation
        private static Logger LOG = LogManager.GetCurrentClassLogger();
        private NetworkListManager netManager = new NetworkListManager();
        private List<(Guid, bool)> _originalNicState = new List<(Guid, bool)>();
        internal List<WSNetworkChangeEventArgs> reccentEvents = new List<WSNetworkChangeEventArgs>();
        WlanClient wclient = new WlanClient();
        internal object eventLock = new object();
        internal Task workerTask;
        private CancellationTokenSource tokenSource = new CancellationTokenSource();
        internal BlockingCollection<IEnumerable<WSNetworkChangeEventArgs>> eventQueue = new BlockingCollection<IEnumerable<WSNetworkChangeEventArgs>>();
        IDisposable netChangeObservable;
        IDisposable periodicIntervalCheckObservable;

        #endregion // fields


        #region constructor
        public NetworkState()
        {
            Initialize();
        }

        public NetworkState(IEnumerable<string> NicWhitelist)
        {

            _ignoreAdapters = NicWhitelist.ToList() ?? new List<string>();
            this.Nics = QueryNetworkAdapters();

            // Loop through nics and add id:state to _originalNicState list
            Nics.Where(x => !NicWhitelist.Any(y => x.Description.StartsWith(y))).ToList()
                .ForEach(x => _originalNicState.Add((x.Id, x.IsEnabled)));

            Initialize();
        }

        internal void Initialize()
        {
            CheckNet = true;

            // Register for network change events
            netManager.NetworkAdded += (netId) => OnNetworkChanged(new WSNetworkChangeEventArgs(netId, NetworkChanges.Added));
            netManager.NetworkDeleted += (netId) => OnNetworkChanged(new WSNetworkChangeEventArgs(netId, NetworkChanges.Deleted));
            netManager.NetworkPropertyChanged += (netId, flags) => OnNetworkChanged(new WSNetworkChangeEventArgs(netId, NetworkChanges.PropertyChanged) { AdditionalInfo = flags });
            netManager.NetworkConnectivityChanged += (netId, flags) => OnNetworkChanged(new WSNetworkChangeEventArgs(netId, NetworkChanges.ConnectivityChanged) { AdditionalInfo = flags });

            // Check network status every 30 seconds
            periodicIntervalCheckObservable = Observable.Timer(new TimeSpan(0, 0, 30)).Subscribe(_ => { OnNetworkChanged(new WSNetworkChangeEventArgs(Guid.Empty, NetworkChanges.DeferredEvent)); });

            // Observe the events that are fired above
            netChangeObservable = Observable.FromEventPattern<WSNetworkChangeEventArgs>(this, nameof(NetworkState.NetworkStateChanged))
                .Delay(x => Observable.Timer(TimeSpan.FromMilliseconds(x.EventArgs.DeferInterval)))
                .Select(x => { if (!Paused) { lock (this.eventLock) { this.reccentEvents.Add(x.EventArgs); } } return x; })
                .Throttle(TimeSpan.FromSeconds(4))
                .Subscribe(
                (_) =>
                {
                    if (Paused) return;

                    List<WSNetworkChangeEventArgs> _events;
                    _events = reccentEvents;
                    reccentEvents = new List<WSNetworkChangeEventArgs>();
                    eventQueue.Add(_events);
                });
        }

        ~NetworkState()
        {
            netManager = null;
            netChangeObservable.Dispose();
            periodicIntervalCheckObservable.Dispose();
        }

        #endregion // constructor


        #region methods

        public void UpdateWhitelist(List<string> Whitelist)
        {
            _ignoreAdapters = Whitelist;
            _checkNet = true;
        }

        /// <summary>
        /// Query NetworkListManager for connection status for specified adapter.
        /// </summary>
        /// <param name="AdapterId"></param>
        /// <returns></returns>
        [Obsolete("This isn't needed anymore. Stop using it.")]
        internal bool QueryNetworkAdapter(Guid AdapterId)
        {
            var nic = Nics.Where(x => x.Id == AdapterId).FirstOrDefault();
            if (nic == null) return false;

            try
            {
                var connection = netManager.GetNetworkConnection(AdapterId);
                if (connection == null)
                {
                    nic.ConnectionStatus = WifiSitterShared.ConnectionState.Unknown;
                }
                else
                {
                    if (connection.IsConnected) nic.ConnectionStatus = nic.ConnectionStatus | WifiSitterShared.ConnectionState.Connected;
                    if (connection.IsConnectedToInternet) nic.ConnectionStatus = nic.ConnectionStatus | WifiSitterShared.ConnectionState.InternetConnected;
                }
                return true;
            }
            catch (Exception e)
            {
                LOG.Log(LogLevel.Error, e);
            }
            return false;
        }

        /// <summary>
        /// Get network information and return list of TrackedNic objects.
        /// </summary>
        /// <returns></returns>
        internal List<TrackedNic> QueryNetworkAdapters()
        {
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

            // Enumerate network adapters and parse connection status
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

        /// <summary>
        /// Releases any IPv4 address associated with the adapter. Throws on error.
        /// </summary>
        /// <param name="Nic"></param>
        internal void ReleaseIp(TrackedNic Nic)
        {
            Debug.Assert(Nic.InterfaceIndex != default(uint));
            using (var ifInfo = GetInterfaceInfo())
            {
                var interface_map = ifInfo.Adapter.Where(x => x.Index == Nic.InterfaceIndex).FirstOrDefault();
                Win32Error err = IpReleaseAddress(ref interface_map);
                err.ThrowIfFailed();
            }
        }

        /// <summary>
        /// Disconnects from any currently connected WiFi network. Throws on error.
        /// </summary>
        /// <param name="Nic"></param>
        internal void WlanDisconnect(TrackedNic Nic)
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
        /// Attempts to connect to preferred networks in range in the preferred nets order. Failing that,
        /// attempt to reconnection to the connected wireless network. This is a best
        /// effort thing and will only log a failure. It's about the best we can do since the last network may not
        /// be in range and doesn't persist across service restarts.
        /// </summary>
        /// <param name="Nic"></param>
        internal void ConnectToPreferredOrLastNetwork(TrackedNic Nic)
        {
            var adapter = wclient.Interfaces.Where(x => x.InterfaceGuid == Nic.Id).FirstOrDefault();

            var available_nets = adapter.GetAvailableNetworkList(0);
            var wlan_profiles = adapter.GetProfiles();

            if (available_nets != null && wlan_profiles != null) {
                // Check for preferred networks that are in range
                foreach (var wp in wlan_profiles)
                {
                    var ap = available_nets.Where(x => x.profileName == wp.profileName).FirstOrDefault();
                    if (ap.Equals(default(WlanAvailableNetwork))) continue;  // Preferred network not in the list of available networks, skip it.


                    // We shouldn't automatically connect to open wifi or unnamed networks, that's just not safe.
                    if (String.IsNullOrEmpty(ap.profileName)) { LOG.Debug($"Skipping unnamed network"); continue; }
                    if (!ap.securityEnabled) { LOG.Debug($"Skipping insecure network: {ap.profileName}"); continue; }
                    if (ap.dot11DefaultAuthAlgorithm == Dot11AuthAlgorithm.IEEE80211_Open) { LOG.Debug($"Skipping open network: {ap.profileName}"); continue; }

                    // Preferred network available attempt connect. Return on success move to the next on failure.
                    if (adapter.ConnectSynchronously(WlanConnectionMode.Profile, ap.dot11BssType, ap.profileName, (20 * 1000)))
                    {
                        LOG.Info($"{Nic.Name} connected to preferred network: '{ap.profileName}'");
                        return;
                    }
                    else
                    {
                        LOG.Info($"{Nic.Name} connection to preferred network: '{ap.profileName}' failed or timed out");
                    }
                }
            }

            LOG.Log(LogLevel.Info, $"{Nic.Name} attempting connection to last netowrk: '{Nic.LastWirelessConnection.profileName}'");
            if (Nic.InterfaceType != NetworkInterfaceType.Wireless80211) return;  // Shouldn't happen but still..

            if (String.IsNullOrEmpty(Nic.LastWirelessConnection.profileName))
            {
                LOG.Debug("No previous connection profile logged. I can't reconnect to nothing...");
                return;
            }

            adapter.Connect(WlanConnectionMode.Profile, Dot11BssType.Any, Nic.LastWirelessConnection.profileName);
        }

        /// <summary>
        /// Attempts to set Wifi Profile configuration for an interface. This isn't strictly required to reconnect
        /// to a wireless network unless the network's configuration has changed since the last connection attempt.
        /// </summary>
        /// <param name="Nic"></param>
        internal void SetWirelessProfile(TrackedNic Nic)
        {
            var adapter = wclient.Interfaces.Where(x => x.InterfaceGuid == Nic.Id).FirstOrDefault();
            adapter.SetProfile(WlanProfileFlags.AllUser, adapter.GetProfileXml(Nic.LastWirelessConnection.profileName), false);
        }

        /// <summary>
        /// Enables disabled interface adapter.
        /// </summary>
        /// <param name="Nic"></param>
        internal void EnableAdapter(TrackedNic Nic)
        {
            // TODO reimplement this with the Vanara / NativeWlan interfaces.
            // They exist to eliminate the WMI dependency.
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_NetworkAdapter Where GUID=\"{{{ Nic.Id.ToString() }}}\""))
            {
                foreach (ManagementObject nic in searcher.Get())
                {   
                    nic.InvokeMethod("Enable", null);
                }
            }
        }

        /// <summary>
        /// Invokes worker task.
        /// </summary>
        public void StartWorker()
        {            
            if (workerTask == null) workerTask = new Task(() => SausageFactory(tokenSource.Token));
            workerTask.Start();
            LOG.Debug($"{System.Reflection.MethodBase.GetCurrentMethod().Name} returning.");
        }

        /// <summary>
        /// Stops worker task via cancellation token and waits for it to complete.
        /// </summary>
        public void StopWorker()
        {
            tokenSource.Cancel();
            eventQueue.Add(null);
            workerTask.Wait(3 * 1000);
            LOG.Debug($"{System.Reflection.MethodBase.GetCurrentMethod().Name} returning.");
        }

        /// <summary>
        /// Worker function that consumes event queue and manages state.
        /// </summary>
        /// <param name="Token"></param>
        private void SausageFactory(CancellationToken Token)
        {

            var reccentlyModifiedInterfaces = new ConcurrentBag<(Guid, DateTime)>();

            var header = new string[] { "Luid", "Name", "Id", "Index", "InterfaceType", "ConnectionStatus", "IsEnabled", "IsConnected", "IsInternetConnected", "LastReconnectAttempt" };
            var dt = new System.Data.DataTable();
            foreach (var t in header)
            {
                dt.Columns.Add(t);
            }

            // Defer network event to invoke initial status check
            this.OnNetworkChanged(new WSNetworkChangeEventArgs()
            {
                Id = Guid.Empty,
                ChangeType = NetworkChanges.DeferredEvent,
                DeferInterval = 2000
            });

            try
            {
                foreach (var eventBatch in eventQueue.GetConsumingEnumerable())
                {
                    if (Token.IsCancellationRequested) break;
                    if (eventBatch.Where(x => x != null).Count() < 1) continue;
                    if (_paused) continue;

                    var recent = DateTime.Now.AddSeconds(-5);
                    // Take any events where the correlating Id does not match that of a TrackedNic that has had recent actions taken
                    var realEvents = eventBatch.Where(e => ! ManagedNics.Where(x => (bool)(x.LastActionTaken?.Any(y => y.ChangeTime > recent))).Any(z => e.Id == z.Id ));

                    foreach (var e in eventBatch)
                    {
                        LOG.Log(LogLevel.Debug, $">> {e.EventTime.Ticks}  {e.Id}  {e.ChangeType.ToString()}  {(realEvents.Any(x => x.Id == e.Id) ? " : Recent Match" : String.Empty)}");
                    }

                    if (realEvents.Count() < 1) continue;

                    // Update connection status and wlan profile info
                    var _nic_list = this.QueryNetworkAdapters().Select(
                        _nic =>
                        {
                            if (Nics == null) return _nic;

                            var matching_nic = Nics?.Where(n => n.Id == _nic.Id)?.FirstOrDefault();

                            // No previous interface identified, skip it
                            if (matching_nic == null) return _nic;

                            // TODO Refactor this when we start using preferred network lists
                            if (!(matching_nic.LastWirelessConnection.Equals(default(WlanConnectionAttributes)) && _nic.Equals(default(WlanConnectionAttributes))))
                            {
                                _nic.LastWirelessConnection = matching_nic.LastWirelessConnection;
                            }
                            else if (!(_nic.Equals(default(WlanConnectionAttributes))))
                            {
                                _nic.LastWirelessConnection = matching_nic.LastWirelessConnection;
                            }

                            _nic.LastActionTaken = matching_nic.LastActionTaken.Where(x => x.ChangeTime > DateTime.Now.AddMinutes(-5)).ToList();
                            return _nic;
                        }).ToList();

                    this.Nics = _nic_list;

                    dt.Rows.Clear();

                    foreach (var x in this.ManagedNics)
                    {
                        dt.Rows.Add(new object[] { x.Luid, x.Name, x.Id, x.InterfaceIndex, x.InterfaceType, x.ConnectionStatus, x.IsEnabled, x.IsConnected, x.IsInternetConnected, x.LastActionTaken?.FirstOrDefault()?.ActionTaken.ToString() ?? "None" });
                    }

                    var table = ConsoleTableBuilder
                       .From(dt)
                       .WithColumn(header)
                       .WithFormat(ConsoleTableBuilderFormat.Default)
                       .Export();

                    LOG.Log(LogLevel.Debug, $"\n{table.ToString()}");

                    // TODO Track when actions were last taken to avoid nic flapping
                    if (this.IsEthernetInternetConnected && this.IsWirelessInternetConnected)
                    {
                        LOG.Log(LogLevel.Warn, "Both Wired and Wireless connections are internet routable, assume proper dual homing, kick wireless.");
                        var wnics = this.ManagedNics
                            .Where(x => x.InterfaceType == NetworkInterfaceType.Wireless80211 && x.IsInternetConnected);

                        foreach (var n in wnics)
                        {
                            try
                            {
                                LOG.Log(LogLevel.Info, $"Releasing IP on adapter: {n.Id}");
                                ReleaseIp(n);
                                WlanDisconnect(n);
                                n.LastActionTaken.Add(new NetworkStateChangeLogEntry(NetworkStateChangeAction.disconnect));
                            }
                            catch (Exception ex)
                            {
                                LOG.Log(LogLevel.Error, ex);
                            }
                        }
                    }
                    else if (!this.IsInternetConnected)
                    {
                        LOG.Log(LogLevel.Warn, "No internet connection available, light 'em up!");
                        var wnics = this.ManagedNics
                            .Where(x => x.InterfaceType == NetworkInterfaceType.Wireless80211 && (!x.IsConnected));

                        foreach (var n in wnics)
                        {
                            // Skip interface if a re-enable was attempted reccently
                            if (n.LastActionTaken.Any(x => (x.ActionTaken == NetworkStateChangeAction.enable)
                                && x.ChangeTime > DateTime.Now.AddSeconds(-10)))
                            {
                                LOG.Debug($"Attempted re-enable on that interface {n.Id} < 10 seconds ago, skipping.");
                                continue;
                            }
                            // If not enabled and didn't try to enable within the last minute, try to re-enable
                            else if (!n.IsEnabled && n.LastActionTaken.Any(x => (x.ActionTaken == NetworkStateChangeAction.enable)
                                && x.ChangeTime > DateTime.Now.AddSeconds(-60)))
                            {
                                try
                                {
                                    EnableAdapter(n);
                                    n.LastActionTaken.Add(new NetworkStateChangeLogEntry(NetworkStateChangeAction.enable));
                                    OnNetworkChanged(new WSNetworkChangeEventArgs() { Id = n.Id, ChangeType = NetworkChanges.DeferredEvent, DeferInterval = 15 * 1000 });
                                }
                                catch (Exception e) { LOG.Log(LogLevel.Error, e, $"Error enabling adapter: {n.Id}  {n.Description}"); }
                            }

                            // Skip interface if a re-connect was attempted reccently
                            if (n.LastActionTaken.Any(x => (x.ActionTaken == NetworkStateChangeAction.reconnect) 
                                && x.ChangeTime > DateTime.Now.AddSeconds(-20)))
                            {
                                LOG.Debug($"Attempted reconnect on that interface {n.Id} < 20 seconds ago, skipping.");
                                continue;
                            }

                            // This doesn't seem to be strictly neccessary all the time.
                            try { this.SetWirelessProfile(n); }
                            catch (Exception e) { LOG.Log(LogLevel.Error, e, $"Error setting wireless profile on interface: {n.Id}"); }

                            // Reconnect to known secure networks in range.
                            try
                            {   
                                ConnectToPreferredOrLastNetwork(n);
                                n.LastActionTaken.Add(new NetworkStateChangeLogEntry(NetworkStateChangeAction.reconnect));
                                OnNetworkChanged(new WSNetworkChangeEventArgs() { Id = n.Id, ChangeType = NetworkChanges.DeferredEvent, DeferInterval = 6 * 1000 });
                                OnNetworkChanged(new WSNetworkChangeEventArgs() { Id = n.Id, ChangeType = NetworkChanges.DeferredEvent, DeferInterval = 30 * 1000 });
                            }
                            catch(Exception e) { LOG.Log(LogLevel.Error, e, $"Error reconnecting adapter: {n.Id}  to network: {n.LastWirelessConnection.profileName}"); }

                            // Only keep actions less than 3 minutes old. We don't operate on info tha told
                            // and don't want to leak memory. If this is ever logged in the GUI it'll need
                            // be tracked on that side.
                            var reccent_actions = n.LastActionTaken.Where(x => x.ChangeTime > DateTime.Now.AddMinutes(-3))?.ToList();
                            n.LastActionTaken = reccent_actions ?? new List<NetworkStateChangeLogEntry>();
                        }
                    }
                    else
                    {
                        LOG.Info("We can get to the internet, nothing to see here.");
                    }
                }
            }
            finally
            {
                netChangeObservable.Dispose();
            }
        }

        #endregion // methods


        #region properties

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsEthernetInternetConnected {
            get {
                if (Nics == null) return false;
                return ManagedNics.Any(nic => nic.InterfaceType == NetworkInterfaceType.Ethernet && nic.IsInternetConnected);
            }
        }

        // * Note, only applies to adapters that aren't ignored by whitelist
        public bool IsWirelessInternetConnected {
            get {
                if (Nics == null) return false;
                return ManagedNics.Any(nic => nic.InterfaceType == NetworkInterfaceType.Wireless80211 && nic.IsInternetConnected);
            }
        }

        public bool IsInternetConnected {
            get => netManager.IsConnectedToInternet;
        }

        public IEnumerable<string> IgnoreAdapters {
            get {
                if (_ignoreAdapters == null) _ignoreAdapters = new List<string>();
                return _ignoreAdapters;
            }
        }

        public IEnumerable<TrackedNic> Nics {
            get {
                if (_nics == null) return new List<TrackedNic>();
                return _nics;
            }
            internal set { _nics = value; }
        }

        public IEnumerable<TrackedNic> ManagedNics {
            get {
                if (_nics == null) return new List<TrackedNic>();
                return _nics.Where(x => !_ignoreAdapters.Any(y => x.Description.StartsWith(y)));
            }
        }

        public SimpleNetworkState SimpleState { get => new SimpleNetworkState()
                {
                    CheckNet = this.CheckNet,
                    EthernetUp = this.IsEthernetInternetConnected,
                    InternetConnected = this.IsInternetConnected,
                    NetworkAvailable = this.NetworkAvailable,
                    IgnoreAdapters = this.IgnoreAdapters,
                    ProcessingState = this.ProcessingState,
                    Paused = this.Paused,
                    Nics = this.ManagedNics.Select(x => new SimpleNic(x)).ToList()
                };
        }

    public List<(Guid, bool)> OriginalNicState {
            get { return _originalNicState; }
        }

        public bool CheckNet {
            get { return _checkNet; }
            private set { _checkNet = value; }
        }


        public bool NetworkAvailable { get; private set; }


        public bool ProcessingState { get => workerTask.Status == TaskStatus.Running; }

        public bool Paused {
            get => _paused;
            set => _paused = value;
        }

        #endregion // properties


        #region events

        public event EventHandler<WSNetworkChangeEventArgs> NetworkStateChanged;

        public void OnNetworkChanged(WSNetworkChangeEventArgs e) => NetworkStateChanged?.Invoke(this, e);

        #endregion // events
    }


    public class WSNetworkChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Guid Id where event originated.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Invoking change time.
        /// </summary>
        public NetworkChanges ChangeType { get; set; }

        /// <summary>
        /// Time the event was fired.
        /// </summary>
        public DateTime EventTime { get; set; } = DateTime.Now;

        public dynamic AdditionalInfo { get; set; }

        /// <summary>
        /// How long in milliseconds from EventTime to wait before handling this event
        /// </summary>
        public int DeferInterval { get; set; } = 0;

        public WSNetworkChangeEventArgs() { }

        public WSNetworkChangeEventArgs(Guid id, NetworkChanges change)
        {
            Id = id;
            ChangeType = change;
        }
    }


}
