using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.ServiceProcess;
using WifiSitter;
using WifiSitter.Model;
using WifiSitterGui.Helpers;
using XDMessaging;

namespace WifiSitterGui.ViewModel
{
    public class WifiSitterAgentViewModel : MvvmObservable
    {
        #region fields

        private static MainWindowViewModel _windowVM;
        private RelayCommand _launchWindowCommand;
        private RelayCommand _takeFiveCommand;  // Asks service to pause for 5 minutes
        private static MainWindow _statusGui;
        private static WifiSitterIpc _wsIpc;
        private Action<object, XDMessageEventArgs> _handleMsgRcv;
        private static string _serviceChannel;
        private System.Timers.Timer _netstateCheckTimer;

        #endregion  // fields

        #region constructor

        public WifiSitterAgentViewModel() {
            Intitialize();
        }

        public WifiSitterAgentViewModel(MainWindowViewModel WindowVM) {
            _windowVM = WindowVM;
            Intitialize();
        }


        private void Intitialize() {
            
            // Intermittent network state polling
            _netstateCheckTimer = new System.Timers.Timer();
            _netstateCheckTimer.AutoReset = true;
            _netstateCheckTimer.Interval = 30 * 1000;  // 30 seconds
            _netstateCheckTimer.Elapsed += (o, e) => { RequestNetworkState(); };
            _netstateCheckTimer.Start();

            Trace.WriteLine(String.Format("WifiSitter service msg channel: {0}", ServiceChannelName));
        }

        #endregion  // constructor

        #region properties

        internal WifiSitterIpc WsIpc {
            get {
                if (_wsIpc == null) {
                    // Setup IPC message listener
                    _handleMsgRcv = new Action<object, XDMessageEventArgs>(wsIpc_MessageReceived);
                    _wsIpc = new WifiSitterIpc(_handleMsgRcv);
                }
                return _wsIpc;
            }
        }

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
                if (_serviceChannel == null) {
                    var serviceProc = Process.GetProcesses().Where(x => x.ProcessName.ToLower().StartsWith("wifisitter"))
                        .Where(x => !x.ProcessName.ToLower().Contains("gui")).ToArray();
                    if (serviceProc != null &&
                        serviceProc.Length > 0) {
                        _serviceChannel = String.Format("{0}-{1}", serviceProc[0].Id, serviceProc[0].ProcessName);
                    }
                }
                return _serviceChannel;
            }
        }


        #endregion  // properties

        #region methods

        private void RequestNetworkState () {
            if (!String.IsNullOrEmpty(ServiceChannelName)) {
                try {
                    Trace.WriteLine("Checking for network state.");
                    WsIpc.MsgBroadcaster.SendToChannel(ServiceChannelName, new WifiSitterIpcMessage("get_netstate", _wsIpc.MyChannelName, _wsIpc.MyChannelName).IpcMessageJsonString());
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
                        var request = new WifiSitterIpcMessage("take_five", _wsIpc.MyChannelName, _wsIpc.MyChannelName);
                        WsIpc.MsgBroadcaster.SendToChannel(_serviceChannel, request.IpcMessageJsonString());
                        // TODO need response validation mechanism
                    });
                }
                return _takeFiveCommand;
            }
        } 

        #endregion  // commands

        #region events
        
        internal void wsIpc_MessageReceived(object sender, XDMessageEventArgs e) {
            if (!e.DataGram.IsValid) {
                Trace.WriteLine("Invalid datagram received.");
                return;
            }

            WifiSitterIpcMessage _sr = null;
            try { _sr = Newtonsoft.Json.JsonConvert.DeserializeObject<WifiSitterIpcMessage>(e.DataGram.Message); }
            catch { Trace.WriteLine("Deserialize to ServiceRequest failed."); }

            if (_sr != null) {
                switch (_sr.Request) {
                    case "give_netstate":
                        try { WindowVM.NetState = Newtonsoft.Json.JsonConvert.DeserializeObject<SimpleNetworkState>(System.Text.Encoding.UTF8.GetString(_sr.Payload)); }
                        catch { WifiSitter.WifiSitter.LogLine("Failed to deserialize netstate, payload."); }
                        break;
                    case "taking_five":
                        Trace.WriteLine("Service paused.");
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
                Trace.WriteLine(e.DataGram.Message);
            }
        }

        #endregion  // events
    }
}
