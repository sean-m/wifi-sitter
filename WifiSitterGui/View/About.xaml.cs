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
    /// Interaction logic for About.xaml
    /// </summary>
    public partial class About : Window
    {
        public About() {
            InitializeComponent();

            Rtb_About.SetRtf(new Uri("pack://application:,,,/Resources/About.rtf"));
            Rtb_ReadMe.SetRtf(new Uri("pack://application:,,,/Resources/ReadMe.rtf"));
            Rtb_License.SetRtf(new Uri("pack://application:,,,/Resources/LICENSE.rtf"));
            Rtb_Troubleshooting.SetRtf(new Uri("pack://application:,,,/Resources/Troubleshooting.rtf"));

            Rtb_About.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Rtb_ReadMe.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Rtb_License.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Rtb_Troubleshooting.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
    }
}
