using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Vanara.PInvoke.IpHlpApi;

namespace WifiSitterShared
{

    public class IfRow
    {
        public NET_LUID InterfaceLuid { get; set; }
        public dynamic InterfaceIndex { get; set; }
        public dynamic InterfaceGuid { get; set; }
        public dynamic Alias { get; set; }
        public dynamic Description { get; set; }
        public dynamic PhysicalAddress { get; set; }
        public dynamic Type { get; set; }
        public dynamic TunnelType { get; set; }
        public dynamic MediaType { get; set; }
        public dynamic PhysicalMediumType { get; set; }
        public dynamic InterfaceAndOperStatusFlags { get; set; }
        public IF_OPER_STATUS OperStatus { get; set; }
        public NET_IF_ADMIN_STATUS AdminStatus { get; set; }
        public NET_IF_MEDIA_CONNECT_STATE MediaConnectState { get; set; }
        public Guid NetworkGuid { get; set; }
        public NET_IF_CONNECTION_TYPE ConnectionType { get; set; }
        public ConnectionState ConnectionStatus { get; set; } = 0;

        public bool IsConnected { get => ConnectionStatus.HasFlag(ConnectionState.Connected) || ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }

        public bool IsInternetConnected { get => ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }
    }

    public enum NetworkChanges
    {
        Added,
        Deleted,
        PropertyChanged,
        ConnectivityChanged
    }

    public enum ConnectionState
    {
        Unknown = 0,
        Connected = 1,
        InternetConnected = 2,
    }

}
