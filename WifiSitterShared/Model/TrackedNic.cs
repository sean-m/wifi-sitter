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

namespace WifiSitter
{
    /// <summary>
    /// Object that contains information from NetworkInterface objects
    /// as well as netsh output (Admin State: Enabled/Disabled).
    /// </summary>
    public class TrackedNic
    {
        #region fields

        private NetworkInterface _nic;
        private bool _isEnabled;
        private bool _isConnected;
        private NativeWifi.Wlan.WlanConnectionAttributes _lastConnection;
        private Logger LOG = NLog.LogManager.GetCurrentClassLogger();

        #endregion  // fields

        #region constructor

        public TrackedNic()
        {

        }

        internal TrackedNic(IfRow Nic)
        {   
            Luid = Nic.InterfaceLuid;
            Name = Nic.Alias;
            Description = Nic.Description;
            Id = Nic.InterfaceGuid.ToString();
            InterfaceType = (Nic.Type == IFTYPE.IF_TYPE_ETHERNET_CSMACD) ? NetworkInterfaceType.Ethernet
                : (Nic.Type == IFTYPE.IF_TYPE_SOFTWARE_LOOPBACK) ? NetworkInterfaceType.Loopback
                : (Nic.Type == IFTYPE.IF_TYPE_IEEE80211) ? NetworkInterfaceType.Wireless80211 : NetworkInterfaceType.Unknown;
            IsEnabled = Nic.OperStatus == IF_OPER_STATUS.IfOperStatusUp;
            IsConnected = Nic.MediaConnectState.HasFlag(NET_IF_MEDIA_CONNECT_STATE.MediaConnectStateConnected);
        }

        #endregion // constructor

        #region properties


        public NET_LUID Luid { get; private set; }

        public bool IsEnabled { get; set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public Guid Id { get; private set; }

        public NetworkInterfaceType InterfaceType { get; private set; }

        public ConnectionState ConnectionStatus { get; set; }

        public bool IsConnected { get => ConnectionStatus.HasFlag(ConnectionState.Connected) || ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }

        public bool IsInternetConnected { get => ConnectionStatus.HasFlag(ConnectionState.InternetConnected); }

        #endregion // properties

        #region methods

        public async void CheckYourself()
        {
            //TODO make this work again
            LOG.Log(LogLevel.Warn, $"Can't check myself {Name} : {Id}");
            return;

            //// We don't care about interfaces that are self assigned
            //if (!_nic.GetIPProperties().GetIPv4Properties().IsAutomaticPrivateAddressingActive
            //    && IsEnabled)
            //{
            //    LOG.Log(LogLevel.Info, $"Checking myself: {Name}");

            //    // If it's a wireless adapter get status with wclient
            //    if (InterfaceType == NetworkInterfaceType.Wireless80211)
            //    {
            //        var wclient = new NativeWifi.WlanClient();
            //        var adapter = wclient.Interfaces.Where(x => Id.Contains(x.InterfaceGuid.ToString().ToUpper())).FirstOrDefault();
            //        if (adapter == null) return;
            //        if (adapter.InterfaceState == WlanInterfaceState.Connected)
            //        {
            //            IsConnected = true;
            //            _lastConnection = adapter.CurrentConnection;  // save for later
            //        }
            //        else
            //        {
            //            IsConnected = false;
            //        }
            //    }
            //    else
            //    {
            //        IPAddress source = _nic.GetIPProperties().AnycastAddresses.FirstOrDefault()?.Address;
            //        var gways = _nic.GetIPProperties().GatewayAddresses;

            //        foreach (var ip in gways)
            //        {
            //            var destination = ip.Address;
            //            //TODO fix all this 
            //            //var pingResult = await Task.Run(() => { return IcmpPing.Send(source, destination); });
            //            //if (pingResult.Status == IPStatus.Success)
            //            //{
            //            //    WifiSitter.LogLine(ConsoleColor.Green, new string[] { "NIC: {0}  IP status: {1}", Nic.Name, pingResult.Status.ToString() });
            //            //    this.IsConnected = true;
            //            //}
            //            //else
            //            //{
            //            //    WifiSitter.LogLine(ConsoleColor.Red, new string[] { "NIC: {0}  IP status: {1}", Nic.Name, pingResult.Status.ToString() });
            //            //    this.IsConnected = false;
            //            //}
            //        }
            //    }
            //}
            //else
            //{
            //    // If you're using a self assigned IP, you're not as connected as you think you are.
            //    this.IsConnected = false;
            //}
        }

        public bool Disable()
        {
            // Release IP first and update NIC inforamtion so OperationalState reflects this
            throw new NotImplementedException("Disabling NIC not implemented!");
            // TODO release IP without ipconfig subprocess
            this.ReleaseIp();

            return false;
        }

        public bool Enable()
        {
            throw new NotImplementedException("Disabling NIC not implemented!");

            return false;
        }

        public void Disconnect()
        {
            if (InterfaceType != NetworkInterfaceType.Wireless80211) return;

            var wclient = new NativeWifi.WlanClient();
            var adapter = wclient.Interfaces.Where(x => Id.ToString().Contains(x.InterfaceGuid.ToString().ToUpper())).FirstOrDefault();
            if (adapter == null) return;

            // Store connection info and disconnect
            // * note: Connection info is stored as struct so incurs a copy
            _lastConnection = adapter.CurrentConnection;
            LOG.Log(LogLevel.Info, $"{Name}  disconnecting from: {_lastConnection.profileName}");
            adapter?.Disconnect();
        }

        public void ConnectToLastSsid()
        {
            if (InterfaceType != NetworkInterfaceType.Wireless80211) return;  // Shouldn't happen but still..

            if (!_lastConnection.Equals(default(WlanConnectionAttributes)))
            {
                LOG.Log(LogLevel.Info, $"{Name}  reconnecting to: {_lastConnection.profileName}");
                var wclient = new NativeWifi.WlanClient();
                var adapter = wclient.Interfaces.Where(x => Id.Equals(x.InterfaceGuid)).FirstOrDefault();
                adapter.SetProfile(WlanProfileFlags.AllUser, adapter.GetProfileXml(_lastConnection.profileName), true);
                adapter.ConnectSynchronously(WlanConnectionMode.Profile, Dot11BssType.Any, _lastConnection.profileName, 30);
            }
        }

        private int EnableDisableInterface(bool Enable)
        {
            string state = Enable ? "ENABLED" : "DISABLED";

            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "netsh.exe";
            proc.StartInfo.Arguments = String.Format("interface set interface name=\"{0}\" admin={1}", this.Name, state);
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            while (!proc.HasExited) { System.Threading.Thread.Sleep(100); }

            return proc.ExitCode;
        }

        private bool ReleaseIp()
        {
            //ipconfig /release "Ethernet"

            if (String.IsNullOrEmpty(this.Name)) { throw new ArgumentException("InterfaceName cannot be null or empty"); }

            List<NetshInterface> results = new List<NetshInterface>();
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "ipconfig.exe";
            proc.StartInfo.Arguments = String.Format("/release \"{0}\"", this.Name);
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();

            while (!proc.HasExited) { System.Threading.Thread.Sleep(100); }

            return proc.ExitCode == 0;
        }

        private NetshInterface GetNetshInterface()
        {
            List<NetshInterface> _netsh;
            NetshInterface netsh = null;
            if ((_netsh = NetshHelper.GetInterfaces()) != null)
                netsh = _netsh.Where(x => x.InterfaceName == this.Name).FirstOrDefault();
            return netsh;
        }

        #endregion // methods
    }
}
