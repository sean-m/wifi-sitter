using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;

using WifiSitter;
using WifiSitter.Model;

namespace WifiSitterGui.ViewModel
{
    public class MainWindowViewModel : MvvmObservable
    {
        #region fields

        SimpleNetworkState _netState;
        private ServiceController _sc;

        #endregion  // fields


        #region constructor

        public MainWindowViewModel () {
            _netState = new SimpleNetworkState();
        }

        #endregion  // constructor


        #region properties
        
        public SimpleNetworkState NetState {
            get { return _netState; }
            set { _netState = value; OnPropertyChanged(null); }
        }

        
        public List<SimpleNic> Nics { get { return NetState.Nics; } }


        public string ServiceState {
            get {
                try { if (_sc == null) _sc = new ServiceController("WifiSitter"); }
                catch { return "No Service"; }

                _sc.Refresh();
                switch (_sc.Status) {
                    case ServiceControllerStatus.Running:
                        return "Running";
                    case ServiceControllerStatus.Stopped:
                        return "Stopped";
                    case ServiceControllerStatus.Paused:
                        return "Paused";
                    case ServiceControllerStatus.StopPending:
                        return "Stopping";
                    case ServiceControllerStatus.StartPending:
                        return "Starting";
                    default:
                        return "Status Changing";
                }
            }
        }

        #endregion  // properties


        #region methods
        #endregion  // methods


        #region eventhandlers
        #endregion  // methods
    }
}
