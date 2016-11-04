﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Reflection;

using WifiSitter.Helpers;
using System.Threading.Tasks;

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

            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            _ignoreNics = ReadNicWhitelist();
            if (_ignoreNics.Count() < 1) {
                WriteLog(LogType.info, "No network adapter whitelist configured.");
            }
            netstate = new NetworkState(DiscoverAllNetworkDevices(null,false), _ignoreNics);
            LogLine("Initialized...");
        }

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

                Thread.Sleep(1000);
            }
        }

        private void ResetNicState (NetworkState netstate) {
            var taskList = new List<Task>();
            foreach (var n in netstate.OriginalNicState) {
                var id   = n[0];
                var stat = n[1];
                TrackedNic now = netstate.Nics.Where(x => x.Id == id).FirstOrDefault();
                if (now != null) {
                    if (stat.ToLower() != now.IsEnabled.ToString().ToLower()) {
                        if (stat == true.ToString()) {
                            var enableTask = new Task(() => { now.Enable(); });
                            enableTask.Start();
                            taskList.Add(enableTask);
                        }
                        else {
                            var disableTask = new Task(() => { now.Disable(); });
                            disableTask.Start();
                            taskList.Add(disableTask); }
                    }
                }
            }
            try {
                Task.WaitAll(taskList.ToArray());
            }
            catch {
                // TODO log the failure as quick as possible, this only happens when process is closing
            }
        }

        #endregion // methods


        #region overrides

        protected override void OnStartImpl(string[] args) {

            if (args == null) return;
            if (args[0].ToLower() == "/install" ||
                args[0].ToLower() == "/uninstall") return;

            Intialize();

            _thread = new Thread(WorkerThreadFunc);
            _thread.Name = "WifiSitter Main Loop";
            _thread.IsBackground = true;
            _thread.Start();
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
