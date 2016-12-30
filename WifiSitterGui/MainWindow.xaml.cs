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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WifiSitterGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Window _about;

        public MainWindow() {
            InitializeComponent();
        }
        
        private void Btn_About_Click(object sender, RoutedEventArgs e) {
            if (_about == null) {
                _about = new WifiSitterGui.View.About();
                _about.Closed += (s, args) => { _about = null; };
                _about.Show();
            }
            else {
                _about.WindowState = WindowState.Normal;
                _about.Activate();
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            var vis = e.OriginalSource as Visual;
            if (vis == null) return;
            if (!vis.IsDescendantOf(WhitelistExpander)) WhitelistExpander.IsExpanded = false;
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.F1:
                    if (_about == null) {
                        _about = new WifiSitterGui.View.About();
                        _about.Closed += (s, args) => { _about = null; };
                        _about.Show();
                    }
                    else {
                        _about.WindowState = WindowState.Normal;
                        _about.Activate();
                    }
                    break;
            }
        }
    }
}
