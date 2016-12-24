using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;

// Project deps
using WifiSitter.Helpers;

// 3rd party deps
using NetMQ;
using NetMQ.Sockets;

namespace WifiSitter
{
    public class WifiSitter : AbstractService
    {
        #region fields

        internal volatile static NetworkState netstate;
        private const string _serviceName = "WifiSitter";
        private Guid _uninstGuid;
        private Thread _mainLoopThread;
        private Thread _mqServerThread;
        private ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
        private volatile bool _paused;
        private SynchronizationContext _sync;
        private static string _myChannel = String.Format("{0}-{1}", Process.GetCurrentProcess().Id, Process.GetCurrentProcess().ProcessName);

        #endregion // fields


        #region constructor

        public WifiSitter () : base(_serviceName) {
            _paused = false;
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
            

            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            var _ignoreNics = ReadNicWhitelist();
            if (_ignoreNics.Count() < 1) {
                WriteLog(LogType.info, "No network adapter whitelist configured.");
            }
            netstate = new NetworkState();
            netstate.UpdateWhitelist(_ignoreNics);
            netstate.UpdateNics(DiscoverAllNetworkDevices(null, false));
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


            List<TrackedNic> nics;
            if (CurrentAdapters == null) {
                string[] whiteList;
                if (netstate != null) {
                    whiteList = netstate.IgnoreAdapters ?? new string[] { };
                }
                else {
                    whiteList = new string[] { };
                }

                nics = NetworkState.QueryNetworkAdapters(whiteList); }
            else { nics = CurrentAdapters; }

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
                    if (!netstate.IgnoreAdapters.Any(x => nic.InterfaceName.StartsWith(x)))
                        NetshHelper.EnableInterface(nic.InterfaceName);
                }

                // Query for network interfaces again
                nicsPost = NetworkState.QueryNetworkAdapters(netstate.IgnoreAdapters);

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
                        if (netstate.IsEthernetUp) { // Ethernet is up
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

        private void WakeWifi(NetworkState netstate) {

            List<Task> taskList = new List<Task>();

            foreach (var n in netstate.Nics) {
                if (n.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                    && !n.IsEnabled) {
                    var enableTask = new Task(() => { n.Enable(); });
                    enableTask.Start();
                    taskList.Add(enableTask);
                }
            }

            try {
                Task.WaitAll(taskList.ToArray());
            }
            catch (Exception e) {
                WriteLog(LogType.error, "Exception when waking wifi,\n", e.InnerException.Message);
            }
        }

        private void ZeroMQRouterRun() {
            // TODO handle port bind failure, increment port and try again, quit after 3 tries
            int port = 37247;
            int tries = 0;
            string connString = String.Format("@tcp://127.0.0.1:{0}", port);

            var server = new RouterSocket(connString);
            server.Options.Identity = Encoding.UTF8.GetBytes(_myChannel);

            while (!_shutdownEvent.WaitOne(0)) {

                var clientMessage = server.ReceiveMultipartMessage();
                var clientAddress = clientMessage[0];

                if (clientMessage.FrameCount > 2) {

                    WifiSitterIpcMessage _msg = null;
                    string response = String.Empty;
                    var msgString = String.Concat(clientMessage.Skip(2).ToList().Select(x => x.ConvertToString()));
                    try { _msg = Newtonsoft.Json.JsonConvert.DeserializeObject<WifiSitterIpcMessage>(msgString); }
                    catch {
                        LogLine("Deserialize to WifiSitterIpcMessage failed.");
                        // TODO respond with failure
                    }

                    if (_msg != null) {
                        LogLine("Received netmq message: {0}", _msg.Request);
                        switch (_msg.Request) {
                            case "get_netstate":
                                LogLine("Sending netstate to: {0}", clientAddress.ConvertToString());
                                if (_paused && netstate.CheckNet) {
                                    netstate.UpdateNics(DiscoverAllNetworkDevices(netstate.Nics));
                                    netstate.StateChecked();
                                }
                                // form response
                                response = new WifiSitterIpcMessage("give_netstate",
                                                                    server.Options.Identity.ToString(),
                                                                    Newtonsoft.Json.JsonConvert.SerializeObject(new Model.SimpleNetworkState(netstate))).ToJsonString();
                                break;
                            case "take_five":
                                try {
                                    if (_paused) {
                                        response = new WifiSitterIpcMessage("taking_five",
                                                                            server.Options.Identity.ToString(),
                                                                            "already_paused").ToJsonString();
                                    }
                                    else {
                                        int minutes = 5;
#if DEBUG
                                        minutes = 1;  // I'm impatient while debugging
#endif

                                        WriteLog(LogType.info, "Taking {0} minute break and restoring interfaces to initial state.", minutes.ToString());

                                        OnPause();
                                        WakeWifi(netstate);

                                        Task.Delay(minutes * 60 * 1000).ContinueWith((task) => {
                                            WriteLog(LogType.info, "Break elapsed. Resuming operation.");
                                            netstate.ShouldCheckState();   // Main loop should check state again when resuming from paused state
                                            OnContinue();
                                            // prefixing t_ to differentiate from outer scope
                                            string t_response = new WifiSitterIpcMessage("taking_five",
                                                                                server.Options.Identity.ToString(),
                                                                                "resuming").ToJsonString();
                                            // Send response
                                            var t_clientAddress = clientAddress;
                                            var t_responseMessage = new NetMQMessage();
                                            t_responseMessage.Append(t_clientAddress);
                                            t_responseMessage.AppendEmptyFrame();
                                            t_responseMessage.Append(t_response);
                                            server.SendMultipartMessage(t_responseMessage);
                                        });
                                        
                                        response = new WifiSitterIpcMessage("taking_five",
                                                                            server.Options.Identity.ToString(),
                                                                            "pausing").ToJsonString();
                                    }
                                }
                                catch { WriteLog(LogType.error, "Failed to enter paused state after 'take_five' request received."); }
                                break;
                            case "reload_whitelist":
                                var list = ReadNicWhitelist();
                                netstate.UpdateWhitelist(list);
                                // Respond with updated network state
                                response = new WifiSitterIpcMessage("give_netstate",
                                                                    server.Options.Identity.ToString(),
                                                                    Newtonsoft.Json.JsonConvert.SerializeObject(new Model.SimpleNetworkState(netstate))).ToJsonString();
                                break;
                            default:
                                break;
                        }

                        // Send response
                        var responseMessage = new NetMQMessage();
                        responseMessage.Append(clientAddress);
                        responseMessage.AppendEmptyFrame();
                        responseMessage.Append(response);
                        server.SendMultipartMessage(responseMessage);
                    }
                    else {
                        Trace.WriteLine(String.Format("Message issue: {0}", clientMessage.ToString()));
                    }
                }
            }
        }

        #endregion // methods


        #region overrides

        protected override void OnStartImpl(string[] args) {
            try {
                if (ServiceExecutionMode != ServiceExecutionMode.Console &&
                    ServiceExecutionMode != ServiceExecutionMode.Service) return;

                Intialize();

                // Setup background thread for running main loop
                _mainLoopThread = new Thread(WorkerThreadFunc);
                _mainLoopThread.Name = "WifiSitter Main Loop";
                _mainLoopThread.IsBackground = true;
                _mainLoopThread.Start();



                // Setup IPC, 0mq messaging thread
                // TODO make this tunable, cmd argument?
                LogLine("Initializing IPC...");
                
                _mqServerThread = new Thread(ZeroMQRouterRun);
                _mqServerThread.Name = "0MQ Server Thread";
                _mqServerThread.IsBackground = true;
                _mqServerThread.Start();
            }
            catch (Exception e) {
                WriteLog(LogType.error, e.Source + " {0}", e.Message);
            }
            
        }

        protected override void OnStopImpl() {
            ResetNicState(netstate);
            _shutdownEvent.Set();
            if (!_mainLoopThread.Join(3000)) {
                _mainLoopThread.Abort();
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
