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

// 3rd party usings
using NetMQ;
using NetMQ.Sockets;

namespace WifiSitterGui.ViewModel
{
    public class WifiSitterAgentViewModel : MvvmObservable
    {
        #region fields

        private static MainWindowViewModel _windowVM;
        private RelayCommand _launchWindowCommand;
        private RelayCommand _takeFiveCommand;  // Asks service to pause for 5 minutes
        private static MainWindow _statusGui;
        private static string _serviceChannel;
        private System.Timers.Timer _netstateCheckTimer;
        private static string _myChannel = String.Format("{0}-{1}", Process.GetCurrentProcess().Id, Process.GetCurrentProcess().ProcessName);
        private static DealerSocket _mqClient;

        #endregion  // fields

        #region constructor

        public WifiSitterAgentViewModel() {
        }


        public WifiSitterAgentViewModel(MainWindowViewModel WindowVM) {
            _windowVM = WindowVM;
            Intitialize();
        }


        private void Intitialize() {
            int port = 37247;
            string connString = String.Format("tcp://127.0.0.1:{0}", port);

            _mqClient = new DealerSocket();
            _mqClient.Options.Identity = Encoding.UTF8.GetBytes(_myChannel);
            _mqClient.Connect(connString);

            // Get NetState
            RequestNetworkState();

            // Intermittent network state polling
            _netstateCheckTimer = new System.Timers.Timer();
            _netstateCheckTimer.AutoReset = true;
            _netstateCheckTimer.Interval = 30 * 1000;  // 30 seconds
            _netstateCheckTimer.Elapsed += (o, e) => { RequestNetworkState(); };
            _netstateCheckTimer.Start();

            // Connection state changed event handler setup
            NetworkChange.NetworkAvailabilityChanged += (o, e) => { RequestNetworkState(3 * 1000); };
            NetworkChange.NetworkAddressChanged += (o, e) => { RequestNetworkState(3 * 1000); };

            Trace.WriteLine(String.Format("WifiSitter service msg channel: {0}", ServiceChannelName));
        }

        #endregion  // constructor

        #region properties
        
        public MainWindowViewModel WindowVM {
            get { if (_windowVM == null) {
                    _windowVM = new MainWindowViewModel();
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
        

        public void RequestNetworkState () {
            if (!String.IsNullOrEmpty(ServiceChannelName)) {
                try {
                    Trace.WriteLine("Checking for network state.");
                    string request = new WifiSitterIpcMessage("get_netstate", _myChannel).ToJsonString();
                    var reqMessage = new NetMQMessage();
                    reqMessage.Append(_mqClient.Options.Identity);
                    reqMessage.AppendEmptyFrame();
                    reqMessage.Append(request);
                    bool success = _mqClient.TrySendMultipartMessage(reqMessage);
                    if (!success) Trace.WriteLine("Failed to send get_networkstate");
                }
                catch (Exception e) {
                    Trace.WriteLine(e.Message);
                }
            }
        }

        #endregion  // methods

        #region commands

        public ICommand LaunchSettingsWindow {
            get {
                if (_launchWindowCommand == null) {
                    _launchWindowCommand = new RelayCommand(() => {
                        if (_statusGui == null) {
                            _statusGui = new MainWindow();
                            _statusGui.DataContext = WindowVM;
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


        public ICommand SendTakeFiveRequest {
            get {
                if (_takeFiveCommand == null) {
                    _takeFiveCommand = new RelayCommand(() => {
                        var request = new WifiSitterIpcMessage("take_five", _myChannel).ToJsonString();
                        var reqMessage = new NetMQMessage();
                        reqMessage.Append(_mqClient.Options.Identity);
                        reqMessage.AppendEmptyFrame();
                        reqMessage.Append(request);
                        bool success = _mqClient.TrySendMultipartMessage(reqMessage);
                        if (!success) Trace.WriteLine("Failed to send take_five");
                        // TODO need response validation mechanism
                    });
                }
                return _takeFiveCommand;
            }
        } 

        #endregion  // commands

        #region events
        
        internal void wsIpc_MessageReceived() {
            //if (!e.DataGram.IsValid) {
            //    Trace.WriteLine("Invalid datagram received.");
            //    return;
            //}

            //WifiSitterIpcMessage _sr = null;
            //try { _sr = Newtonsoft.Json.JsonConvert.DeserializeObject<WifiSitterIpcMessage>(e.DataGram.Message); }
            //catch { Trace.WriteLine("Deserialize to ServiceRequest failed."); }

            //if (_sr != null) {
            //    switch (_sr.Request) {
            //        case "give_netstate":
            //            try { WindowVM.NetState = Newtonsoft.Json.JsonConvert.DeserializeObject<SimpleNetworkState>(Encoding.UTF8.GetString(_sr.Payload)); }
            //            catch { WifiSitter.WifiSitter.LogLine("Failed to deserialize netstate, payload."); }
            //            break;
            //        case "taking_five":
            //            Trace.WriteLine(String.Format("Responded 'taking_five' : {0}", Encoding.UTF8.GetString(_sr.Payload)));
            //            break;
            //        case "service_status":
            //            // TODO issue service status update
            //            break;
            //        default:
            //            Trace.WriteLine(String.Format("Unknown request type: {0} from {1}", _sr?.Request, _sr?.Requestor));
            //            break;
            //    }
            //}
            //else {
            //    Trace.WriteLine(e.DataGram.Message);
            //}
        }

        #endregion  // events
    }
}
