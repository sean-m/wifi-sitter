using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Diagnostics;

using XDMessaging;

namespace WifiSitter
{
    public class WifiSitterIpc
    {
        private static string _myChannel = String.Format("{0}-{1}", Process.GetCurrentProcess().Id, Process.GetCurrentProcess().ProcessName);

        private XDMessagingClient _msgClient = new XDMessagingClient();

        public IXDListener MsgListener { get; private set; }
        
        public IXDBroadcaster MsgBroadcaster { get; private set; }

        public string MyChannelName { get { return _myChannel; } }

        public WifiSitterIpc(Action<object, XDMessageEventArgs> MessageReceivedHandler) {
            
            try {
                WifiSitter.LogLine("Registering listener channel: {0}", _myChannel);
                MsgListener = _msgClient.Listeners.GetListenerForMode(XDTransportMode.Compatibility);
                MsgListener.RegisterChannel(_myChannel);
            }
            catch {
                WifiSitter.LogLine("Failed to register IPC listener channel {0}", _myChannel);
            }
            MsgListener.MessageReceived += (o, e) => { MessageReceivedHandler(o, e); };

            MsgBroadcaster = _msgClient.Broadcasters.GetBroadcasterForMode(XDTransportMode.Compatibility);
        }
    }
}
