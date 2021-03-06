﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// 3rd party deps
using NLog;
using NetMQ;
using NetMQ.Sockets;

// Project deps
using WifiSitter.Helpers;
using WifiSitterShared;

namespace WifiSitter
{
    public class WifiSitter : AbstractService
    {
        #region fields

        internal static volatile NetworkState netstate;
        private const string _serviceName = "WifiSitter";
        private Guid _uninstGuid;
        private Task _mainLoopTask;
        private Task _mqServerTask;
        private ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
        private static string _myChannel = String.Format("{0}-{1}", Process.GetCurrentProcess().Id, Process.GetCurrentProcess().ProcessName);
        private static Logger LOG = LogManager.GetCurrentClassLogger();

        #endregion // fields


        #region constructor

        public WifiSitter() : base(_serviceName)
        {
            if (this.ServiceExecutionMode != ServiceExecutionMode.Console)
            {
                this.AutoLog = true;
                this.CanPauseAndContinue = true;
            }
        }


        /// <summary>
        /// Do initial nic discovery and netsh trickery
        /// </summary>
        private void Intialize()
        {

            //Show Version
            Assembly asm = GetType().Assembly;
            Version v = asm.GetName().Version;
            LOG.Log(LogLevel.Info, "Version: {0}", v.ToString());


            // Check if there are any interfaces not detected by GetAllNetworkInterfaces()
            // That method will not show disabled interfaces
            var _ignoreNics = ReadNicWhitelist();
#if DEBUG
            _ignoreNics.Add("Microsoft Wi-Fi Direct");
            _ignoreNics.Add("VirtualBox Host");
            _ignoreNics.Add("VMware Network Adapter");
            _ignoreNics.Add("Hyper-V Virtual");
            _ignoreNics.Add("Microsoft Kernel");
            _ignoreNics.Add("Bluetooth Device");
            _ignoreNics.Add("Microsoft Teredo");
            _ignoreNics.Add("Microsoft IP-HTTPS");
            _ignoreNics.Add("Microsoft 6to4");
            _ignoreNics.Add("WAN Miniport");
            _ignoreNics = _ignoreNics.Distinct().ToList();
#endif
            if (_ignoreNics.Count() < 1)
            {
                LOG.Log(LogLevel.Warn, "No network adapter whitelist configured.");
            }
            netstate = new NetworkState(_ignoreNics);
            LOG.Log(LogLevel.Info, "Initialized basic state...");
        }


        ~WifiSitter()
        {
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

        private List<string> ReadNicWhitelist()
        {
            List<string> results = new List<string>();

            try
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(String.Format(@"SYSTEM\CurrentControlSet\services\{0}\NicWhiteList", ServiceName), false);
                if (key != null)
                {
                    var names = key.GetValueNames();
                    foreach (var n in names)
                    {
                        results.Add(key.GetValue(n).ToString());
                    }
                }
            }
            catch (Exception e)
            {
                LOG.Log(LogLevel.Error, $"Failed reading NIC whitelist from registry. \n{e.Message}");
            }

            return results;
        }

        /// <summary>
        /// Main application loop of sorts.
        /// </summary>
        private void WorkerThreadFunc()
        {
            netstate.StartWorker();
            _shutdownEvent.WaitOne();
            netstate.StopWorker();

            LOG.Debug($"{System.Reflection.MethodBase.GetCurrentMethod().Name} returning.");
        }


        /// <summary>
        /// IPC handling loop
        /// </summary>
        private void ZeroMQRouterRun()
        {
            // TODO handle port bind failure, increment port and try again, quit after 3 tries
            int port = 37247;
            int tries = 0;
            string connString = String.Format("@tcp://127.0.0.1:{0}", port);

            var server = new RouterSocket(connString);
            server.Options.Identity = Encoding.UTF8.GetBytes(_myChannel);

            // TODO refactor into event -> queue based
            while (!_shutdownEvent.WaitOne(0))
            {

                var clientMessage = server.ReceiveMultipartMessage();
                var clientAddress = clientMessage[0];

                if (clientMessage.FrameCount > 2)
                {

                    WifiSitterIpcMessage _msg = null;
                    string response = String.Empty;
                    var msgString = String.Concat(clientMessage.Skip(2).ToList().Select(x => x.ConvertToString()));
                    try { _msg = Newtonsoft.Json.JsonConvert.DeserializeObject<WifiSitterIpcMessage>(msgString); }
                    catch
                    {
                        LOG.Log(LogLevel.Error, "Deserialize to WifiSitterIpcMessage failed.");
                        // TODO respond with failure
                    }

                    if (_msg != null)
                    {
                        LOG.Log(LogLevel.Debug, "Received netmq message: {0}", _msg.Request);
                        switch (_msg.Request)
                        {
                            case "get_netstate":
                                LOG.Log(LogLevel.Debug, "Sending netstate to: {0}", clientAddress.ConvertToString());

                                // form response
                                response = new WifiSitterIpcMessage("give_netstate",
                                                                    server.Options.Identity.ToString(),
                                                                    Newtonsoft.Json.JsonConvert.SerializeObject(netstate.SimpleState)).ToJsonString();
                                break;
                            case "take_five":
                                try
                                {
                                    if (netstate.Paused)
                                    {
                                        response = new WifiSitterIpcMessage("taking_five",
                                                                            server.Options.Identity.ToString(),
                                                                            "already_paused").ToJsonString();
                                    }
                                    else
                                    {
                                        int minutes = 5;
#if DEBUG
                                        minutes = 1;  // I'm impatient while debugging
#endif

                                        LOG.Log(LogLevel.Info, "Taking {0} minute break and restoring interfaces to initial state.", minutes.ToString());

                                        OnPause();

                                        Task.Delay(minutes * 60 * 1000).ContinueWith((task) =>
                                        {
                                            LOG.Log(LogLevel.Info, "Break's over! Not gettin paid to just stand around.");
                                            netstate.OnNetworkChanged(new WSNetworkChangeEventArgs(Guid.Empty, NetworkChanges.DeferredEvent));   // Main loop should check state again when resuming from paused state
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
                                catch { LOG.Log(LogLevel.Error, "Failed to enter paused state after 'take_five' request received."); }
                                break;
                            case "reload_whitelist":
                                var list = ReadNicWhitelist();
                                netstate.UpdateWhitelist(list);
                                // Respond with updated network state
                                response = new WifiSitterIpcMessage("give_netstate",
                                                                    server.Options.Identity.ToString(),
                                                                    Newtonsoft.Json.JsonConvert.SerializeObject(netstate.SimpleState)).ToJsonString();
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
                    else
                    {
                        Trace.WriteLine(String.Format("Message issue: {0}", clientMessage.ToString()));
                    }
                }
            }
            LOG.Debug($"{System.Reflection.MethodBase.GetCurrentMethod().Name} returning.");
        }

        #endregion // methods


        #region overrides

        protected override void OnStartImpl(string[] args)
        {
            try
            {
                if (ServiceExecutionMode != ServiceExecutionMode.Console &&
                    ServiceExecutionMode != ServiceExecutionMode.Service) return;

                Intialize();

                var syncContext = (SynchronizationContext.Current != null)
                    ? TaskScheduler.FromCurrentSynchronizationContext()
                    : TaskScheduler.Current;

                // Setup background thread for running main loop
                LOG.Log(LogLevel.Info, "Spawning main thread...");
                _mainLoopTask = new Task(WorkerThreadFunc);
                _mainLoopTask.ContinueWith((worker) =>
                {
                    if (worker.IsFaulted)
                    {
                        LOG.Log(LogLevel.Error,
                            "Error in main main worker:\n{0}",
                            String.Join("\n", worker?.Exception?.InnerExceptions?.Select(
                                x => String.Format("{0} : {1}", x.TargetSite, x.Message))) ?? "Cannot get exception.");
                        Stop();
                    }
                }, syncContext);

                try
                {
                    _mainLoopTask.Start();
                }
                catch (Exception e)
                {
                    LOG.Log(LogLevel.Error, "Exception in main task:\n{0}", _mainLoopTask.Exception.Message);
                }


                // Setup 0mq message router task
                if (Properties.Settings.Default.enable_ipc)
                {
                    LOG.Log(LogLevel.Info, "Initializing IPC worker thread...");
                    _mqServerTask = new Task(ZeroMQRouterRun);
                    _mqServerTask.ContinueWith((worker) =>
                    {
                        if (worker.IsFaulted)
                        {
                            LOG.Log(LogLevel.Error, "Error in main 0mq router:\n\t{1} : {0}",
                                String.Join("\n", worker?.Exception?.InnerExceptions?.Select(
                                    x => String.Format("{0} : {1}", x.TargetSite, x.Message))) ?? "Cannot get exception.");
                        }
                    }, syncContext);
                    _mqServerTask.Start();
                }
                else { LOG.Log(LogLevel.Warn, "IPC not initialized. May not communicate with GUI agent."); }

            }
            catch (Exception e)
            {
                LOG.Log(LogLevel.Error, e);
            }
            LOG.Debug($"{System.Reflection.MethodBase.GetCurrentMethod().Name} returning.");
        }

        protected override void OnStartCommandLine()
        {
            Console.WriteLine("Service is running...  Press ENTER to quit.");
            Console.ReadLine();
            LOG.Debug($"{System.Reflection.MethodBase.GetCurrentMethod().Name} returning.");
        }

        protected override void OnStopImpl()
        {
            LOG.Log(LogLevel.Debug, "Stopping now...");
            _shutdownEvent.Set();
        }

        protected override void OnPause()
        {
            base.OnPause();
            netstate.Paused = true;
        }

        protected override void OnContinue()
        {
            base.OnContinue();
            netstate.Paused = false;
            netstate.OnNetworkChanged(new WSNetworkChangeEventArgs(Guid.Empty, NetworkChanges.DeferredEvent));  // Could have been paused for a while so kick off another status check
        }

        internal override void CreateRegKeys()
        {
            // HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\services\WifiSitter

            RegistryKey sitterConfigKey;
            try
            {
                sitterConfigKey = Registry.LocalMachine.CreateSubKey(String.Format(@"SYSTEM\CurrentControlSet\services\{0}\NicWhiteList", ServiceName));
                if (sitterConfigKey != null)
                {
                    sitterConfigKey.SetValue("0", "Microsoft Wi-Fi Direct", RegistryValueKind.String);
                    sitterConfigKey.SetValue("1", "VirtualBox Host", RegistryValueKind.String);
                    sitterConfigKey.SetValue("2", "VMware Network Adapter", RegistryValueKind.String);
                    sitterConfigKey.SetValue("3", "Hyper-V Virtual", RegistryValueKind.String);
                    sitterConfigKey.SetValue("4", "Microsoft Kernel", RegistryValueKind.String);
                    sitterConfigKey.SetValue("5", "Bluetooth Device", RegistryValueKind.String);
                    sitterConfigKey.SetValue("6", "Microsoft Teredo", RegistryValueKind.String);
                    sitterConfigKey.SetValue("7", "Microsoft IP-HTTPS", RegistryValueKind.String);
                    sitterConfigKey.SetValue("8", "Microsoft 6to4", RegistryValueKind.String);
                    sitterConfigKey.SetValue("9", "WAN Miniport", RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                LOG.Log(LogLevel.Error, ex);
            }
        }

        internal override void RemoveRegKeys()
        {

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(String.Format(@"SYSTEM\CurrentControlSet\services\{0}\NicWhiteList", ServiceName));
            }
            catch (Exception ex)
            {
                LOG.Log(LogLevel.Error, ex);
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
