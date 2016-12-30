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
        private View.HelpBubbleWindow bubbleWindow;

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
                    ShowHelp();
                    break;
            }
        }


        private void ShowHelp() {
            if (bubbleWindow == null) {
                bubbleWindow = new View.HelpBubbleWindow();
                bubbleWindow.Closed += (o, e) => { bubbleWindow = null; };

                if (Left < bubbleWindow.Width) Left = bubbleWindow.Width + 10;
                
                var statusPoint = StatusLabels.TransformToAncestor(this)
                                  .Transform(new Point(0, 0));
                var wiredPoint = WiredInterfaceList.TransformToAncestor(this)
                                      .Transform(new Point(0, 0));
                var wirelessPoint = WirelessInterfaceList.TransformToAncestor(this)
                                      .Transform(new Point(0, 0));
                var whitelistListPoint = WhitelistedInterfaceList.TransformToAncestor(this)
                                      .Transform(new Point(0, 0));
                var whitelistExpanderPoint = WhitelistExpander.TransformToAncestor(this)
                                  .Transform(new Point(0, 0));

                var stdMargin = new Thickness(0, 4, 0, 4);

                // Status Help
                var statusHelp = new View.HelpBubble();
                statusHelp.TriangleVirt(VerticalAlignment.Bottom);
                statusHelp.HorizontalAlignment = HorizontalAlignment.Right;
                statusHelp.Margin = stdMargin;
                statusHelp.Height = wiredPoint.Y - 4;
                bubbleWindow.AddContentControl(statusHelp);
                Canvas.SetTop(statusHelp, statusPoint.Y - 16);

                // Wired Interface Help
                var wiredHelp = new View.HelpBubble();
                wiredHelp.HorizontalAlignment = HorizontalAlignment.Right;
                wiredHelp.Margin = stdMargin;
                wiredHelp.Height = wirelessPoint.Y - wiredPoint.Y - 4;
                bubbleWindow.AddContentControl(wiredHelp);
                Canvas.SetTop(wiredHelp, wiredPoint.Y + 10);

                // Wireless Interface Help
                var wirelessHelp = new View.HelpBubble();
                wirelessHelp.HorizontalAlignment = HorizontalAlignment.Right;
                wirelessHelp.Margin = stdMargin;
                wirelessHelp.Height = whitelistListPoint.Y - wirelessPoint.Y - 4;
                bubbleWindow.AddContentControl(wirelessHelp);
                Canvas.SetTop(wirelessHelp, wirelessPoint.Y + 10);

                // Whitelist Interface Help
                var whitelistListsHelp = new View.HelpBubble();
                whitelistListsHelp.HorizontalAlignment = HorizontalAlignment.Right;
                whitelistListsHelp.Margin = stdMargin;
                whitelistListsHelp.Height = whitelistExpanderPoint.Y - whitelistListPoint.Y - 10;
                bubbleWindow.AddContentControl(whitelistListsHelp);
                Canvas.SetTop(whitelistListsHelp, whitelistListPoint.Y + 10);

                // Whitelist Expander Help
                var whitelistHelp = new View.HelpBubble();
                whitelistHelp.HorizontalAlignment = HorizontalAlignment.Right;
                whitelistHelp.Margin = stdMargin;


                bubbleWindow.Height = Height + 48;
                bubbleWindow.Top = Top + 15;
                bubbleWindow.Left = Left - bubbleWindow.Width + 10;
                bubbleWindow.Show();
            }
            else {
                bubbleWindow.Close();
                bubbleWindow = null;
            }
            
        }
    }
}
