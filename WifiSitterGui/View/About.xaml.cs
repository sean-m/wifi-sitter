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

            Rtb_About.SetRtf(Properties.Resources.About);
            Rtb_ReadMe.SetRtf(Properties.Resources.ReadMe);
            Rtb_License.SetRtf(Properties.Resources.LICENSE);
            Rtb_Troubleshooting.SetRtf(Properties.Resources.Troubleshooting);
        }
    }
}
