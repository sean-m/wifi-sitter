using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WifiSitterConfig.ViewModel
{
    class MainWindowViewModel : MvvmObservable
    {
        #region fields

        private InterfaceStatusViewModel _statusView;
        private ConfigViewModel _configView;

        #endregion // fields

        #region constructor

        public MainWindowViewModel () {

        }

        #endregion // constructor

        #region properties

        public InterfaceStatusViewModel StatusViewModel {
            get {
                if (_statusView == null) {
                    _statusView = new InterfaceStatusViewModel();
                }
                return _statusView;
            }
        }


        public ConfigViewModel ConfigViewModel {
            get {
                if (_configView == null) {
                    _configView = new ConfigViewModel();
                }
                return _configView;
            }
        }

        #endregion // properties

        #region methods
        #endregion // methods

        #region commands
        #endregion // commands

        #region events
        #endregion // events
    }
}
