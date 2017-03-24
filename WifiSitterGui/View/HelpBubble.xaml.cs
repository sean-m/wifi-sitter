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
    /// Interaction logic for HelpBubble.xaml
    /// </summary>
    public partial class HelpBubble : UserControl
    {
        public HelpBubble() {
            InitializeComponent();
        }

        public void TriangleVirt(VerticalAlignment Pos) {
            Arrow.VerticalAlignment = Pos;            
        }

        public HelpBubble(string msg) {
            InitializeComponent();

            this.helpText.Text = msg;
        }
    }
    
}
