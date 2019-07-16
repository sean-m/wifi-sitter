using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.ServiceProcess;
using System.Net.NetworkInformation;

// internal usings
using WifiSitter;
using WifiSitter.Model;
using WifiSitterGui.Helpers;
using WifiSitterGui.ViewModel.Events;

// 3rd party usings
using NLog;
using NetMQ;
using NetMQ.Sockets;
using Prism.Events;

namespace WifiSitterGui.ViewModel
{
    public class WifiSitterAgentViewModel : MvvmObservable
    {
        #region fields

        private static MainWindowViewModel _windowVM;
        private RelayCommand _launchWindowCommand;
        private RelayCommand _takeFiveCommand;  // Asks service to pause for 5 minutes
        private RelayCommand _reloadWhitelistCommand;  // Asks service to reload the nic whitelist
        private static MainWindow _statusGui;
        private static string _serviceChannel;
        private System.Timers.Timer _netstateCheckTimer;
        private static string _myChannel = String.Format("{0}-{1}", Process.GetCurrentProcess().Id, Process.GetCurrentProcess().ProcessName);
        private static DealerSocket _mqClient;
        private static NetMQPoller _poller;
        private IEventAggregator _eventAggregator;
        private NLog.Logger LOG = NLog.LogManager.GetCurrentClassLogger();

        #endregion  // fields

        #region constructor

        public WifiSitterAgentViewModel() {
        }


        public WifiSitterAgentViewModel(IEventAggregator eventtAggregator) {
            _eventAggregator = eventtAggregator;
            _eventAggregator?.GetEvent<ReloadWhitelistEvent>().Subscribe(() => { RequestReloadWhitelist(); });

            _windowVM = WindowVM;

            Intitialize();

#if DEBUG
            this.LaunchSettingsWindow.Execute(null);
#endif
        }


        private void Intitialize() {
            // Get NetState
            RequestNetworkState();

            // Intermittent network state polling
            _netstateCheckTimer = new System.Timers.Timer() {
                AutoReset = true,
                Interval = 30 * 1000  // 30 seconds
            };
            _netstateCheckTimer.Elapsed += (o, e) => { RequestNetworkState(); };
            _netstateCheckTimer.Start();

            // Connection state changed event handler setup
            NetworkChange.NetworkAddressChanged += (o, e) => { RequestNetworkState(3 * 1000); RequestNetworkState(5 * 1000); };

            Trace.WriteLine(String.Format("WifiSitter service msg channel: {0}", ServiceChannelName));
        }
        
        ~WifiSitterAgentViewModel() {
            _poller?.StopAsync();
            _poller?.Dispose();
        }
        
        #endregion  // constructor

        #region properties

        public MainWindowViewModel WindowVM {
            get { if (_windowVM == null) {
                    _windowVM = new MainWindowViewModel(_eventAggregator);
                }
                return _windowVM;
            }
            private set { _windowVM = value; OnPropertyChanged("WindowVM"); }
        }

        
        internal string ServiceChannelName {
            get {
                var serviceProc = Process.GetProcesses().Where(x => x.ProcessName.ToLower().StartsWith("wifisitter"))
                    .Where(x => !x.ProcessName.ToLower().Contains("gui")).ToArray();
                if (serviceProc != null &&
                    serviceProc.Length > 0) {
                    _serviceChannel = String.Format("{0}-{1}", serviceProc[0].Id, serviceProc[0].ProcessName);
                }
                
                return _serviceChannel;
            }
        }

        #endregion  // properties

        #region methods

        public void RequestNetworkState(int Delay) {
            Task.Delay(Delay).ContinueWith((task) => { RequestNetworkState(); }, TaskScheduler.FromCurrentSynchronizationContext());
        }


        System.Timers.Timer _netstateTimer;
        public void RequestNetworkState () {
            
            // Setup timer to throttle requests
            if (_netstateTimer == null) {
                _netstateTimer = new System.Timers.Timer();
                _netstateTimer.Interval = 3000;
                _netstateTimer.AutoReset = false;
            }

            if (_netstateTimer.Enabled) {
                Trace.WriteLine("Requested network state less than 2 seconds ago, skipping.");
                return;
            }
            else {
                _netstateTimer.Start();
            }


            if (!String.IsNullOrEmpty(ServiceChannelName)) {
                try {
                    Trace.WriteLine("Checking for network state.");
                    string request = new WifiSitterIpcMessage("get_netstate", _myChannel).ToJsonString();
                    bool success = SendMessageToService(request);
                    if (!success) Trace.WriteLine("Failed to send request network state.");
                }
                catch (Exception e) {
                    Trace.WriteLine(e.Message);
                }
            }
        }


        public void RequestReloadWhitelist() {
            if (!String.IsNullOrEmpty(ServiceChannelName)) {
                try {
                    Trace.WriteLine("Checking for network state.");
                    string request = new WifiSitterIpcMessage("reload_whitelist", _myChannel).ToJsonString();
                    bool success = SendMessageToService(request);
                    if (!success) Trace.WriteLine("Failed to send request reload_whitelist.");
                }
                catch (Exception e) {
                    Trace.WriteLine(e.Message);
                }
            }
        }


        private bool SendMessageToService(string msg) {

            // Initialize messaging componenets if needed.
            int port = 37247;
            string connString = String.Format("tcp://127.0.0.1:{0}", port);

            if (_mqClient == null) {
                _mqClient = new DealerSocket();
                _mqClient.Options.Identity = Encoding.UTF8.GetBytes(_myChannel);
                _mqClient.Connect(connString);
                _mqClient.ReceiveReady += _mqClient_ReceiveReady;
            }

            if (_poller == null) {
                _poller = new NetMQPoller {
                    _mqClient
                };
            }

            if (!_poller.IsRunning) {
                Trace.WriteLine("Reinitializing poller.");
                _poller = new NetMQPoller();
                _poller.Add(_mqClient);
                _poller.RunAsync();
            }


            // Send message
            var reqMessage = new NetMQMessage();
            reqMessage.Append(_mqClient.Options.Identity);
            reqMessage.AppendEmptyFrame();
            reqMessage.Append(msg);
            return  _mqClient.TrySendMultipartMessage(reqMessage);
        }

        #endregion  // methods

        #region commands

        public ICommand LaunchSettingsWindow {
            get {
                if (_launchWindowCommand == null) {
                    _launchWindowCommand = new RelayCommand(() => {
                        if (_statusGui == null) {
                            _statusGui = new MainWindow() {
                                DataContext = WindowVM
                            };
                            _statusGui.Closed += (s, e) => { _statusGui = null; };
                            _statusGui.Show();
                        }
                        else {
                            _statusGui.WindowState = WindowState.Normal;
                            _statusGui.Activate();
                        }
                    });
                }
                return _launchWindowCommand;
            }
        }


        public ICommand SendReloadWhitelistRequest {
            get {
                if (_reloadWhitelistCommand == null) {
                    _reloadWhitelistCommand = new RelayCommand(() => {
                        var request = new WifiSitterIpcMessage("reload_whitelist", _myChannel).ToJsonString();
                        bool success = SendMessageToService(request);
                        if (!success) Trace.WriteLine("Failed to send reload_whitelist");
                        // TODO need response validation mechanism
                    });
                }
                return _reloadWhitelistCommand;
            }
        }


        public ICommand SendTakeFiveRequest {
            get {
                if (_takeFiveCommand == null) {
                    _takeFiveCommand = new RelayCommand(() => {
                        var request = new WifiSitterIpcMessage("take_five", _myChannel).ToJsonString();
                        bool success = SendMessageToService(request);
                        if (!success) Trace.WriteLine("Failed to send take_five");
                        // TODO need response validation mechanism
                    });
                }
                return _takeFiveCommand;
            }
        }

        #endregion  // commands

        #region events

        private void _mqClient_ReceiveReady(object sender, NetMQSocketEventArgs e) {
            
            Trace.WriteLine(">> Response received.");

            _eventAggregator.GetEvent<ReceivedFromServer>().Publish(DateTime.Now);

            WifiSitterIpcMessage _sr = null;

            var msg = e.Socket.ReceiveMultipartMessage();
            if (msg.FrameCount >= 2) {
                var msgString = String.Concat(msg.Where(x => x.BufferSize > 0).Select(x => x.ConvertToString()));
                try { _sr = Newtonsoft.Json.JsonConvert.DeserializeObject<WifiSitterIpcMessage>(msgString); }
                catch {
                    Trace.WriteLine("Deserialize to WifiSitterIpcMessage failed.");
                    // TODO respond with failure
                }
            }
                
            if (_sr != null) {
                switch (_sr.Request) {
                    case "give_netstate":
                        try {
                            _netstateTimer.Stop(); _netstateTimer.Start();
                            WindowVM.NetState = Newtonsoft.Json.JsonConvert.DeserializeObject<SimpleNetworkState>(Encoding.UTF8.GetString(_sr.Payload)); }
                        catch { LOG.Log(LogLevel.Error, "Failed to deserialize netstate, payload."); }
                        break;
                    case "taking_five":
                        Trace.WriteLine(String.Format("Responded 'taking_five' : {0}", Encoding.UTF8.GetString(_sr.Payload)));
                        break;
                    case "service_status":
                        // TODO issue service status update
                        break;
                    default:
                        Trace.WriteLine(String.Format("Unknown request type: {0} from {1}", _sr?.Request, _sr?.Requestor));
                        break;
                }
            }
            else {
                Trace.WriteLine("Server response is null.");
            }
        }
        
        #endregion  // events
    }
}
