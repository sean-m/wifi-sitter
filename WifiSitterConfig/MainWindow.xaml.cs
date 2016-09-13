﻿using System;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using WifiSitterToolbox.ViewModel;

namespace WifiSitterToolbox
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainWindowViewModel ctx;
        private About _aboutWindow;

        public MainWindow() {
            InitializeComponent();

            // This ViewModel binding is only done in code behind for the main window.
            ctx = new MainWindowViewModel();
            this.DataContext = ctx;
        }

        private void About_Button_Click(object sender, RoutedEventArgs e) {
            if (_aboutWindow == null) {
                _aboutWindow = new About();
                _aboutWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _aboutWindow.Show();
                _aboutWindow.Closed += (o, evt) => { _aboutWindow = null; };
            }
            else {
                try { _aboutWindow.Activate(); }
                catch { /* This can fail in strange edge cases and it doesn't really matter */}
            }
        }
    }
}
