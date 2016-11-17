using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Diagnostics;

using WifiSitterGui.ViewModel;
using WifiSitter;
using WifiSitter.Model;

using XDMessaging;

namespace WifiSitterGui
{
    /// <summary>
    /// Interaction logic for TrayIconControl.xaml
    /// </summary>
    public partial class TrayIconControl : Window
    {
        #region fields

        private static MainWindowViewModel _windowVm;
        private static MainWindow _statusGui;
        private static WifiSitter.WifiSitterIpc _wsIpc;
        private static string _serviceChannel;
        private Action<object, XDMessageEventArgs> _handleMsgRcv;

        #endregion  // fields


        #region constructor

        public TrayIconControl() {
            InitializeComponent();
            
            _windowVm = new ViewModel.MainWindowViewModel();
            //ShowStatusSettingsWindow();

            // Setup IPC message listener
            _handleMsgRcv = new Action<object, XDMessageEventArgs>(wsIpc_MessageReceived);
            _wsIpc = new WifiSitter.WifiSitterIpc( _handleMsgRcv );
            
            Trace.WriteLine(String.Format("WifiSitter service msg channel: {0}", ServiceChannelName));

            if (! String.IsNullOrEmpty(ServiceChannelName)) {
                try {
                    _wsIpc.MsgBroadcaster.SendToChannel(ServiceChannelName, new WifiSitterIpcMessage("get_netstate", _wsIpc.MyChannelName, _wsIpc.MyChannelName).IpcMessageJsonString());
                }
                catch (Exception e) {
                    Trace.WriteLine(e.Message);
                }
            }

        }

        ~TrayIconControl() {
            
        }

        #endregion  // constructor


        #region properties

        internal string ServiceChannelName {
            get {
                if (_serviceChannel == null) GetServiceChannelName();
                return _serviceChannel;
            }
        }

        private void GetServiceChannelName() {
            var serviceProc = Process.GetProcesses().Where(x => x.ProcessName.ToLower().StartsWith("wifisitter"))
                .Where(x => !x.ProcessName.ToLower().Contains("gui")).ToArray();
            if (serviceProc != null && 
                serviceProc.Length > 0) {
                _serviceChannel = String.Format("{0}-{1}", serviceProc[0].Id, serviceProc[0].ProcessName);
            }
        }

        #endregion  // properties


        #region methods

        void ShowStatusSettingsWindow() {
            _statusGui = new MainWindow();
            _statusGui.DataContext = _windowVm;
            _statusGui.Closed += (s, e) => {
                this.Dispatcher.Invoke(new Action(() => { _statusGui = null; }));
            };
            _statusGui.Show();
        }

        #endregion  // methods


        #region eventhandlers
        
        private void ContextMenu_StatusSettings(object sender, RoutedEventArgs e) {
            if (_statusGui == null) {
                ShowStatusSettingsWindow();
            }
            else {
                _statusGui.WindowState = WindowState.Normal;
                _statusGui.Activate();
            }
        }


        private void ContextMenu_Quit(object sender, RoutedEventArgs e) {
            _statusGui?.Close();            
            Environment.Exit(0);
        }


        internal void wsIpc_MessageReceived(object sender, XDMessageEventArgs e) {
            if (!e.DataGram.IsValid) {
                Trace.WriteLine("Invalid datagram received.");
                return;
            }

            WifiSitterIpcMessage _sr = null;
            try { _sr = Newtonsoft.Json.JsonConvert.DeserializeObject<WifiSitterIpcMessage>(e.DataGram.Message); }
            catch { Trace.WriteLine("Deserialize to ServiceRequest failed."); }

            if (_sr != null) {
                if (_sr.Request == "give_netstate") {
                    try {
                        _windowVm.NetState = Newtonsoft.Json.JsonConvert.DeserializeObject<SimpleNetworkState>(System.Text.Encoding.UTF8.GetString(_sr.Payload));
                    }
                    catch { WifiSitter.WifiSitter.LogLine("Failed to deserialize netstate, payload."); }
                }
            }
            else {
                Trace.WriteLine(e.DataGram.Message);
            }
        }


        #endregion  // eventhandlers
    }
}
