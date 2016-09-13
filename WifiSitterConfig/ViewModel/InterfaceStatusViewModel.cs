using Microsoft.Win32;
using System;
using System.Collections.Generic;
using NativeWifi;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WifiSitterToolbox.ViewModel 
{
    class InterfaceStatusViewModel : MvvmObservable
    {
        #region fields
        private static string[] _ignoreNics;
        private static WifiSitter.NetworkState _netState;
        private static WlanClient _wclient;
        private static List<string> _statusLog = new List<string>();
        #endregion // fields

        #region constructor

        public InterfaceStatusViewModel () {
            _ignoreNics = ReadNicWhitelist();
            _netState = new WifiSitter.NetworkState(WifiSitter.NetshHelper.DiscoverAllNetworkDevices(null, _ignoreNics, true), _ignoreNics);
            _wclient = new NativeWifi.WlanClient();
            foreach (var i in _wclient.Interfaces) {
                WriteStatusLog(String.Format("Found Wifi Interface: {0}, {1}, {2}, {3}", i.InterfaceName, i.InterfaceDescription, i.InterfaceState, i.InterfaceGuid));
                i.WlanNotification += Wifi_WlanNotification;
                i.WlanConnectionNotification += Wifi_WlanConnectionNotification;
                if (i.InterfaceState != Wlan.WlanInterfaceState.NotReady) i.Scan();
            }
        }

        private void Wifi_WlanConnectionNotification(NativeWifi.Wlan.WlanNotificationData notifyData, NativeWifi.Wlan.WlanConnectionNotificationData connNotifyData) {
            throw new NotImplementedException();
        }

        private void Wifi_WlanNotification(NativeWifi.Wlan.WlanNotificationData notifyData) {
            string logLine = String.Format("{0}  {1}", notifyData.interfaceGuid.ToString(), notifyData.NotificationCode.ToString());
            WriteStatusLog(logLine);

            if (notifyData.notificationSource == Wlan.WlanNotificationSource.ACM) {

                if ((Wlan.WlanNotificationCodeAcm)(notifyData.NotificationCode) == Wlan.WlanNotificationCodeAcm.NetworkAvailable) {
                    var d = notifyData;
                    var i = _wclient.Interfaces.Where(x => x.InterfaceGuid == notifyData.interfaceGuid).FirstOrDefault();
                    
                }
            }

        }

        #endregion // constructor

        #region properties

        public List<string> StatusLog {
            get { return _statusLog; }
        }

        #endregion // properties

        #region methods

        private string[] ReadNicWhitelist() {
            List<string> results = new List<string>();

            try {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(String.Format(@"SYSTEM\CurrentControlSet\services\WifiSitter\NicWhiteList"), false);
                if (key != null) {
                    var names = key.GetValueNames();
                    foreach (var n in names) {
                        results.Add(key.GetValue(n).ToString());
                    }
                }
            }
            catch {
                //TODO reimplement this in the gui: WriteLog(LogType.error, String.Concat("Failed reading NIC whitelist from registry. \n", e.Message));
            }

            return results.ToArray();
        }


        private void WriteStatusLog (string msg) {
            _statusLog.Add(String.Format("{0}  {1}", DateTime.Now, msg));
            this.OnPropertyChanged("StatusLog");
        }

        #endregion // methods

        #region commands
        #endregion // commands

        #region events
        #endregion // events
    }
}
