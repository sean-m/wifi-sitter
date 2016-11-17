using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WifiSitterGui.Helpers;

namespace WifiSitterGui.ViewModel
{
    public class WifiSitterAgentViewModel : MvvmObservable
    {
        #region fields

        private static MainWindowViewModel _windowVM;
        private RelayCommand _launchWindowCommand;
        private static MainWindow _statusGui;
        
        #endregion  // fields

        #region constructor

        public WifiSitterAgentViewModel() {

        }

        public WifiSitterAgentViewModel(MainWindowViewModel WindowVM) {
            _windowVM = WindowVM;
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

        #endregion  // properties

        #region methods
        #endregion  // methods

        #region commands

        public RelayCommand LaunchSettingsWindow {
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

        #endregion  // commands

        #region events
        #endregion  // events
    }
}
