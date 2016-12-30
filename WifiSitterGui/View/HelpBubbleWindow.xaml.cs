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

namespace WifiSitterGui.View
{
    /// <summary>
    /// Interaction logic for HelpBubbleWindow.xaml
    /// </summary>
    public partial class HelpBubbleWindow : Window
    {
        public HelpBubbleWindow() {
            InitializeComponent();
        }


        public void AddContentControl(UserControl control) {
            MainCanvas.Children.Add(control);
        }

        private void Window_KeyUp(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape ||
                e.Key == Key.F1) this.Close();
        }
    }
}
