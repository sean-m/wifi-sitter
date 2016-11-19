﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;

// Project deps
using WifiSitter.Helpers;

// 3rd party deps
using XDMessaging;

namespace WifiSitter
{
    public class WifiSitter : AbstractService
    {
        #region fields

        internal volatile static NetworkState netstate;
        private const string _serviceName = "WifiSitter";
        private Guid _uninstGuid;
        private Thread _thread;
        private ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
        private static string[] _ignoreNics;
        private volatile bool _paused;
        private static WifiSitterIpc _wsIpc;
        private Action<object, XDMessageEventArgs> _handleMsgRecv;
        
        #endregion // fields


        #region constructor

        public WifiSitter () : base(_serviceName) {
            _paused = false;
            _ignoreNics = new string[] { };
            if (this.ServiceExecutionMode != ServiceExecutionMode.Console) {
                this.AutoLog = true;
                this.CanPauseAndContinue = true;
            }
        }


        /// <summary>
        /// Do initial nic discovery and netsh trickery
        /// </summary>
        private void Intialize() {
            try {
                Console.WindowWidth = 120;
            }
            catch {
                LogLine(ConsoleColor.Red, "Failed to resize console window.");
            }

            //Show Version
            Assembly asm = GetType().Assembly;
            Version v = asm.GetName().Version;
            LogLine("Version: {0}", v.ToString());


            // Setup IPC
            // TODO make this tunable, cmd argument?
            LogLine("Initializing IPC...");
            _handleMsgRecv = new Action<object, XDMessageEventArgs>(HandleMsgReceived);
            _wsIpc = new WifiSitterIpc(_handleMsgRecv);
            

            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            _ignoreNics = ReadNicWhitelist();
            if (_ignoreNics.Count() < 1) {
                WriteLog(LogType.info, "No network adapter whitelist configured.");
            }
            netstate = new NetworkState(DiscoverAllNetworkDevices(null, false), _ignoreNics);
            LogLine("Initialized...");
        }

        #endregion // constructor


        #region properties

        public override string DisplayName {
            get {
                return _serviceName;
            }
        }

        protected override Guid UninstallGuid {
            get {
                System.Guid.TryParse("23a42c57-a16c-4b93-a5cb-60cff20c1f7a", out _uninstGuid);
                return _uninstGuid;
            }
        }

        public override string ServiceDesc {
            get {
                return "Manages WiFi adapters based on wired ethernet connectivity.";
            }
        }

        #endregion // properties


        #region methods

        private string[] ReadNicWhitelist() {
            List<string> results = new List<string>();

            try {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(String.Format(@"SYSTEM\CurrentControlSet\services\{0}\NicWhiteList", ServiceName), false);
                if (key != null) {
                    var names = key.GetValueNames();
                    foreach (var n in names) {
                        results.Add(key.GetValue(n).ToString());
                    }
                }
            }
            catch (Exception e) {
                WriteLog(LogType.error, String.Concat("Failed reading NIC whitelist from registry. \n", e.Message));
            }

            return results.ToArray();
        }

        public static List<TrackedNic> DiscoverAllNetworkDevices(List<TrackedNic> CurrentAdapters=null, bool quiet=false) {
            if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering all devices.");

            var nics = (CurrentAdapters == null) ? NetworkState.QueryNetworkAdapters(_ignoreNics) : CurrentAdapters;

            List<TrackedNic> nicsPost;
            var netsh = NetshHelper.GetInterfaces();

            List<NetshInterface> notInNetstate = new List<NetshInterface>();

            // Only check disabled adapters we don't already know about
            foreach (var n in netsh) {
                if (!nics.Any(x => x.Name == n.InterfaceName)) {
                    notInNetstate.Add(n);
                }
            }


            if (notInNetstate.Count > 0) {
                if (!quiet) LogLine(ConsoleColor.Yellow, "Discovering disabled devices.");
                var disabledInterfaces = notInNetstate.Where(x => x.AdminState == "Disabled")
                                                      .Where(x => !nics.Any(y => y.Name == x.InterfaceName)) // Ignore nics we already know about
                                                      .ToArray();
                
                // Turn on disabled interfaces
                foreach (var nic in disabledInterfaces) {
                    if (!_ignoreNics.Any(x => nic.InterfaceName.StartsWith(x)))
                        NetshHelper.EnableInterface(nic.InterfaceName);
                }

                // Query for network interfaces again
                nicsPost = NetworkState.QueryNetworkAdapters(_ignoreNics);

                // Disable nics again
                foreach (var nic in disabledInterfaces) {
                    NetshHelper.DisableInterface(nic.InterfaceName);
                }
                
                nics?.AddRange(nicsPost.Where(x => !nics.Any(y => y.Name == x.Name)));

                // Update the state on SitterNic objects
                foreach (var n in nics) {
                    n.UpdateState(netsh?.Where(x => x.InterfaceName == n.Name).FirstOrDefault());
                }

                return nics;
            }

            // Detected no disabled nics, so update accordingly.
            foreach (var nic in nics) {
                nic.UpdateState(netsh?.Where(x => x.InterfaceName == nic.Nic.Name).FirstOrDefault());
            }

            // Detect nics that are no longer available
            if (netsh != null) {
                var missingNics = nics.Where(x => !netsh.Any(y => y.InterfaceName == x.Name));
                foreach (var n in missingNics) {
                    nics.Where(x => x.Name == n.Name).ToList().ForEach(x => {
                        x.IsConnected = false;
                        x.IsEnabled = false;
                    });
                }
            }

            return nics;
        }

        public static void LogLine(params string[] msg) {
            LogLine(ConsoleColor.White, msg);
        }

        public static void LogLine(ConsoleColor color, params string[] msg) {
            if (msg.Length == 0) return;
            string log = msg.Length > 0 ? String.Format(msg[0], msg.Skip(1).ToArray()) : msg[0];
            Console.Write(DateTime.Now.ToString());
            Console.ForegroundColor = color;
            Console.WriteLine("  {0}", log);
            Console.ResetColor();
        }
        
        public void WriteLog(LogType type, params string[] msg) {

            if (this.ServiceExecutionMode == ServiceExecutionMode.Console) {
                // Log to console
                ConsoleColor color = Console.ForegroundColor;
                switch (type) {
                    case LogType.error:
                        color = ConsoleColor.Red;
                        break;
                    case LogType.warn:
                        color = ConsoleColor.Yellow;
                        break;
                    case LogType.success:
                        color = ConsoleColor.Green;
                        break;
                    default:
                        // Do nothing
                        break;
                }

                LogLine(color, msg);
                return;
            }
            else {
                // Running as service
                // Log to Event Viewer
                int eventId = 1142;
                EventLogEntryType eventType = EventLogEntryType.Information;
                switch (type) {
                    case LogType.error:
                        eventType = EventLogEntryType.Error;
                        eventId = 1143;
                        break;
                    case LogType.warn:
                        eventType = EventLogEntryType.Warning;
                        eventId = 1144;
                        break;
                    case LogType.success:
                        eventType = EventLogEntryType.SuccessAudit;
                        eventId = 1145;
                        break;
                    default:
                        // Do nothing
                        break;
                }

                string message = msg.Length > 0 ? String.Format(msg[0], msg.Skip(1).ToArray()) : msg[0];

                EventLog.WriteEntry(message, eventType, eventId);
            }
        }

        private void WorkerThreadFunc() {
            
            while (!_shutdownEvent.WaitOne(0)) {

                if (_paused) {
                    Thread.Sleep(1000);
                    continue;
                }

                if (netstate.CheckNet) {

                    netstate.ProcessingState = true;

                    netstate.UpdateNics(DiscoverAllNetworkDevices(netstate.Nics));

                    var wifi = netstate.Nics.Where(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                                            .Where(x => x.IsConnected)
                                            .ToArray();

                    if (netstate.NetworkAvailable) { // Network available
                        if (netstate.EthernetUp) { // Ethernet is up
                            if (wifi != null) {
                                foreach (var adapter in wifi) {
                                    WriteLog (LogType.warn, "Disable adaptor: {0,18}  {1}", adapter.Name, adapter.Description);

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

                            WriteLog (LogType.warn, "Enable adaptor: {0,18}  {1}", nic.Name, nic.Description);

                            bool enableResult = nic.Enable();
                            if (!enableResult) LogLine(ConsoleColor.Red, "Failed to enable NIC {0}", nic.Name);
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
                    Console.WriteLine("{0,32} {1,48}  {2,16}  {3}  {4}\n", "Name", "Description", "Type", "Connected", "Enabled");
                    foreach (var adapter in netstate.Nics) {
                        Console.WriteLine("{0,32} {1,48}  {2,16}  {3,7}  {4,7}", adapter.Name,
                                                                                 adapter.Description,
                                                                                 adapter.Nic.NetworkInterfaceType,
                                                                                 adapter.IsConnected,
                                                                                 adapter.IsEnabled);
                    }
                    Console.WriteLine("\n");

                    netstate.StateChecked();
                }

                Thread.Sleep(500);
            }
        }

        private void ResetNicState (NetworkState netstate) {
            if (netstate == null)
                return;

            var taskList = new List<Task>();
            foreach (var n in netstate.OriginalNicState) {
                var id    = n[0];
                var state = n[1];
                TrackedNic now = netstate.Nics.Where(x => x.Id == id).FirstOrDefault();
                if (now != null) {
                    if (state.ToLower() != now.IsEnabled.ToString().ToLower()) {
                        if (state == true.ToString()) {
                            WriteLog(LogType.info, "Restoring adapter state, enabling adapter: {0} - {1}", now.Name, now.Description);
                            var enableTask = new Task(() => { now.Enable(); });
                            enableTask.Start();
                            taskList.Add(enableTask);
                        }
                        else {
                            WriteLog(LogType.info, "Restoring adapter state, disabling adapter: {0} - {1}", now.Name, now.Description);
                            var disableTask = new Task(() => { now.Disable(); });
                            disableTask.Start();
                            taskList.Add(disableTask); }
                    }
                }
            }
            try {
                Task.WaitAll(taskList.ToArray());
            }
            catch (Exception e) {
                WriteLog(LogType.error, "Exception when resetting nic state\n", e.InnerException.Message);
            }
        }


        private void HandleMsgReceived(object sender, XDMessageEventArgs e) {
            LogLine("Message received");
            if (!e.DataGram.IsValid) {
                Trace.WriteLine("Invalid datagram received.");
                return;
            }

            WifiSitterIpcMessage _msg = null;
            try { _msg = Newtonsoft.Json.JsonConvert.DeserializeObject<WifiSitterIpcMessage>(e.DataGram.Message); }
            catch { LogLine("Deserialize to WifiSitterIpcMessage failed."); }
            WifiSitterIpcMessage response;
            if (_msg != null) {
                switch (_msg.Request) {
                    case "get_netstate":
                        LogLine("Sending netstate to: {0}", _msg.Requestor);
                        if (_paused) netstate.UpdateNics(DiscoverAllNetworkDevices(netstate.Nics));
                        response = new WifiSitterIpcMessage("give_netstate", _wsIpc.MyChannelName, "", Newtonsoft.Json.JsonConvert.SerializeObject(new Model.SimpleNetworkState(netstate)));
                        _wsIpc.MsgBroadcaster.SendToChannel(_msg.Target, response.IpcMessageJsonString());
                        break;
                    case "take_five":
                        try {
                            LogLine("Taking 5 minute break and restoring interfaces.");
                            OnPause();
                            Task.Delay(5 * 60 * 1000).ContinueWith((task) => { OnContinue(); }, TaskScheduler.FromCurrentSynchronizationContext());
                            ResetNicState(netstate);
                            response = new WifiSitterIpcMessage("taking_five", _wsIpc.MyChannelName, "", "");
                            _wsIpc.MsgBroadcaster.SendToChannel(_msg.Target, response.IpcMessageJsonString());
                        }
                        catch { WriteLog(LogType.error, "Failed to enter paused state after 'take_five' request received."); }
                        break;
                    default:
                        break;
                }
            }
            else {
                Trace.WriteLine(String.Format("Message issue: {0}", e.DataGram.Message));
            }
        }
        
        #endregion // methods


        #region overrides

        protected override void OnStartImpl(string[] args) {
            try {
                if (ServiceExecutionMode != ServiceExecutionMode.Console &&
                    ServiceExecutionMode != ServiceExecutionMode.Service) return;

                Intialize();

                _thread = new Thread(WorkerThreadFunc);
                _thread.Name = "WifiSitter Main Loop";
                _thread.IsBackground = true;
                _thread.Start();
            }
            catch (Exception e) {
                WriteLog(LogType.error, e.Source, e.Message);
            }
            
        }

        protected override void OnStopImpl() {
            ResetNicState(netstate);
            _shutdownEvent.Set();
            if (!_thread.Join(3000)) {
                _thread.Abort();
            }
        }

        protected override void OnPause() {
            base.OnPause();
            this._paused = true;
        }

        protected override void OnContinue() {
            base.OnContinue();
            this._paused = false;
        }

        internal override void CreateRegKeys() {
            // HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\services\WifiSitter

            RegistryKey sitterConfigKey;
            try {
                sitterConfigKey = Registry.LocalMachine.CreateSubKey(String.Format(@"SYSTEM\CurrentControlSet\services\{0}\NicWhiteList", ServiceName));
                if (sitterConfigKey != null) {
                    sitterConfigKey.SetValue("0", "Microsoft Wi-Fi Direct", RegistryValueKind.String);
                    sitterConfigKey.SetValue("1", "VirtualBox Host", RegistryValueKind.String);
                    sitterConfigKey.SetValue("2", "VMware Network Adapter", RegistryValueKind.String);
                }
            }
            catch {
                WriteLog(LogType.error, "Could not create configuration registry values!");
            }
        }

        internal override void RemoveRegKeys() {

            try {
                Registry.LocalMachine.DeleteSubKeyTree(String.Format(@"SYSTEM\CurrentControlSet\services\{0}\NicWhiteList", ServiceName));
            }
            catch {
                WriteLog(LogType.error, "Could not remove configuration registry values!");
            }
        }

        #endregion // overrides
    }

    public enum LogType
    {
        info,
        warn,
        error,
        success,
    }
}
