using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

using WifiSitter;
using WifiSitter.Helpers;

using NLog;

using static NativeWifi.Wlan;
using static Vanara.PInvoke.IpHlpApi;

namespace WifiSitterShared
{
    /// <summary>
    /// Object that contains information from NetworkInterface objects
    /// as well as netsh output (Admin State: Enabled/Disabled).
    /// </summary>
    public class TrackedNic
    {
        #region fields

        private Logger LOG = LogManager.GetCurrentClassLogger();

        #endregion  // fields

        #region constructor

        public TrackedNic()
        {

        }

        public TrackedNic(IfRow Nic)
        {   
            Luid = Nic.InterfaceLuid;
            Name = Nic.Alias;
            Description = Nic.Description;
            Id = Nic.InterfaceGuid;
            InterfaceType = (Nic.Type == IFTYPE.IF_TYPE_ETHERNET_CSMACD) ? NetworkInterfaceType.Ethernet
                : (Nic.Type == IFTYPE.IF_TYPE_SOFTWARE_LOOPBACK) ? NetworkInterfaceType.Loopback
                : (Nic.Type == IFTYPE.IF_TYPE_IEEE80211) ? NetworkInterfaceType.Wireless80211 : NetworkInterfaceType.Unknown;
            IsEnabled = Nic.OperStatus == IF_OPER_STATUS.IfOperStatusUp;
            ConnectionStatus = Nic.ConnectionStatus;
            InterfaceIndex = Nic.InterfaceIndex;
        }

        #endregion // constructor

        #region properties


        public NET_LUID Luid { get; private set; }

        public bool IsEnabled { get; set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public Guid Id { get; private set; }

        public uint InterfaceIndex { get; private set; }

        public NetworkInterfaceType InterfaceType { get; private set; }

        public ConnectionState ConnectionStatus { get; set; }

        public bool IsConnected { get => ConnectionStatus.HasFlag(ConnectionState.Connected) || ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }

        public bool IsInternetConnected { get => ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }

        public WlanConnectionAttributes LastWirelessConnection { get; set; }

        public List<NetworkStateChangeLogEntry> LastActionTaken { get; set; } = new List<NetworkStateChangeLogEntry>();

        #endregion // properties
    }


    public class NetworkStateChangeLogEntry
    {
        public DateTime ChangeTime { get; set; } = DateTime.Now;

        public NetworkStateChangeAction ActionTaken { get; private set; }

        public NetworkStateChangeLogEntry(NetworkStateChangeAction action)
        {
            ActionTaken = action;
        }
    }

    public enum NetworkStateChangeAction
    {
        disconnect,
        reconnect
    }
}
