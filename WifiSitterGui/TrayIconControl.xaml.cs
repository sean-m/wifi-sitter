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

namespace WifiSitterGui
{
    /// <summary>
    /// Interaction logic for TrayIconControl.xaml
    /// </summary>
    public partial class TrayIconControl : Window
    {
        #region fields
        
        private static WifiSitterAgentViewModel _agentVM;

        #endregion  // fields


        #region constructor

        public TrayIconControl() {
            InitializeComponent();

            _agentVM = new WifiSitterAgentViewModel(new MainWindowViewModel());
            DataContext = _agentVM;
        }

        ~TrayIconControl() {
            if (TaskBarIcon != null) TaskBarIcon.Visibility = Visibility.Hidden;
            TaskBarIcon?.Dispose();
        }

        #endregion  // constructor


        #region properties


        #endregion  // properties


        #region methods

        #endregion  // methods


        #region eventhandlers

        private void ContextMenu_Quit(object sender, RoutedEventArgs e) {
            Task.Run(() => { Environment.Exit(0); });
        }
        
        #endregion  // eventhandlers
    }
}
