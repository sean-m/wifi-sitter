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

        #endregion  // fields


        #region constructor

        public TrayIconControl() {
            InitializeComponent();

            _windowVm = new MainWindowViewModel();
            ShowStatusSettingsWindow();
        }


        ~TrayIconControl() {
            
        }

        #endregion  // constructor

        #region properties
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

        #endregion  // eventhandlers
    }
}
