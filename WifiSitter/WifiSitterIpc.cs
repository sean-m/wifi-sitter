using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Diagnostics;

namespace WifiSitter
{
    public class WifiSitterIpc
    {
        private static string _myChannel = String.Format("{0}-{1}", Process.GetCurrentProcess().Id, Process.GetCurrentProcess().ProcessName);
        public MessageListener MsgListener { get; private set; }
        
        public MessageBroadcaster MsgBroadcaster { get; private set; }

        public string MyChannelName { get { return _myChannel; } }

        public WifiSitterIpc (Action<object, MessageEventArgs> MessageReceivedHandler) {
            MsgListener = new MessageListener();
            try {
                WifiSitter.LogLine("Registering listener channel: {0}", _myChannel);
                MsgListener.RegisterChannel(_myChannel);
            }
            catch {
                WifiSitter.LogLine("Failed to register IPC listener channel {0}", _myChannel);
            }
            MsgListener.MessageReceived += (o,e) => { MessageReceivedHandler(o, e); };

            MsgBroadcaster = new MessageBroadcaster();
        }
    }
}
