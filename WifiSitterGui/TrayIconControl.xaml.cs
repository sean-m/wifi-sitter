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
using Prism.Events;

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

            this.Closing += (o, e) => { this.TaskBarIcon.Visibility = Visibility.Hidden; };

            _agentVM = new WifiSitterAgentViewModel(EventAggregator);
            DataContext = _agentVM;
        }

        ~TrayIconControl() {
            TaskBarIcon?.Dispose();
        }

        #endregion  // constructor


        #region properties

        private IEventAggregator _eventAggregator;
        internal IEventAggregator EventAggregator {
            get {
                if (_eventAggregator == null)
                    _eventAggregator = new EventAggregator();

                return _eventAggregator;
            }
        }

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
