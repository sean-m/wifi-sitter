using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WifiSitterToolbox.ViewModel 
{
    class InterfaceStatusViewModel : MvvmObservable
    {
        #region fields
        private static WifiSitter.NetworkState _netState;
        #endregion // fields

        #region constructor

        public InterfaceStatusViewModel () {
            _netState = new WifiSitter.NetworkState(WifiSitter.NetshHelper.DiscoverAllNetworkDevices(), ReadNicWhitelist());
        }

        #endregion // constructor

        #region properties
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
            catch (Exception e) {
                //TODO reimplement this in the gui: WriteLog(LogType.error, String.Concat("Failed reading NIC whitelist from registry. \n", e.Message));
            }

            return results.ToArray();
        }

        #endregion // methods

        #region commands
        #endregion // commands

        #region events
        #endregion // events
    }
}
