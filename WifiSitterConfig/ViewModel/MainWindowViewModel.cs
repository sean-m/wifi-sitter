using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using WifiSitterToolbox.Helper;

namespace WifiSitterToolbox.ViewModel
{
    class MainWindowViewModel : MvvmObservable
    {
        #region fields

        private InterfaceStatusViewModel _statusView;
        private ConfigViewModel _configView;
        private RelayCommand _showAbout;
        private static About _aboutWindow;

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

        public ICommand ShowAbout {
            get {
                if (_showAbout == null) {
                    _showAbout = new WifiSitterToolbox.Helper.RelayCommand(
                        () => {
                            if (_aboutWindow == null) {
                                _aboutWindow = new About();
                                _aboutWindow.Show();
                                _aboutWindow.Closed += (o, e) => { _aboutWindow = null; };
                            }
                            else {
                                try { _aboutWindow.Activate(); }
                                catch { /* This can fail in strange edge cases and it doesn't really matter */}
                            }
                        });
                }

                return _showAbout;
            }
        }

        #endregion // commands

        #region events
        #endregion // events
    }
}
