using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private MainWindowViewModel _windowVm;

        public TrayIconControl() {
            InitializeComponent();

            _windowVm = new MainWindowViewModel();

            var statusGui = new MainWindow();
            statusGui.DataContext = _windowVm;
            statusGui.Show();
        }
    }
}
