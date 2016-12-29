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

using WifiSitterGui.Helpers;

namespace WifiSitterGui.View
{
    /// <summary>
    /// Interaction logic for HelpWindow.xaml
    /// </summary>
    public partial class HelpWindow : Window
    {
        public HelpWindow() {
            InitializeComponent();
        }

        public HelpWindow(Uri RtfContent) {
            InitializeComponent();
            
            var rtb = new RichTextBox();
            rtb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            rtb.Margin = new Thickness(10);
            rtb.SetRtf(RtfContent);
            rtb.ScrollToHome();
        }
    }
}
