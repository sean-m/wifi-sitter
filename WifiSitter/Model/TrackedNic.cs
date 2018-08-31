using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using WifiSitter.Helpers;
using static NativeWifi.Wlan;

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

        #endregion  // fields

        #region constructor

        public TrackedNic() {

        }

        public TrackedNic(NetworkInterface Nic) {
            this._nic = Nic;
            _isEnabled = false;
        }

        public TrackedNic(NetworkInterface Nic, bool IsEnabled) {
            _nic = Nic;
            _isEnabled = IsEnabled;
        }

        #endregion // constructor

        #region properties

        public NetworkInterface Nic {
            get { return _nic; }
            set { _nic = value; }
        }

        public bool IsEnabled {
            get { return _isEnabled; }
            set { _isEnabled = value; }
        }


        public bool IsConnected {
            get { return _isConnected; }
            set { _isConnected = value; }
        }


        public string Name {
            get { return Nic.Name; }
        }


        public string Description {
            get { return Nic.Description; }
        }


        public string Id {
            get { return Nic.Id; }
        }

        #endregion // properties

        #region methods

        public async void CheckYourself()
        {  
            // We don't care about interfaces that are self assigned
            if (!_nic.GetIPProperties().GetIPv4Properties().IsAutomaticPrivateAddressingActive
                && IsEnabled)
            {
                WifiSitter.LogLine(LogType.info, $"Checking myself: {Name}");

                // If it's a wireless adapter get status with wclient
                if (Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    var wclient = new NativeWifi.WlanClient();
                    var adapter = wclient.Interfaces.Where(x => Nic.Id.Contains(x.InterfaceGuid.ToString().ToUpper())).FirstOrDefault();
                    if (adapter == null) return;
                    if (adapter.InterfaceState == WlanInterfaceState.Connected)
                    {
                        IsConnected = true;
                        _lastConnection = adapter.CurrentConnection;  // save for later
                    }
                    else
                    {
                        IsConnected = false;
                    }
                }
                else
                {
                    IPAddress source = _nic.GetIPProperties().AnycastAddresses.FirstOrDefault()?.Address;
                    var gways = _nic.GetIPProperties().GatewayAddresses;

                    foreach (var ip in gways)
                    {
                        var destination = ip.Address;
                        var pingResult = await Task.Run(() => { return IcmpPing.Send(source, destination); });
                        if (pingResult.Status == IPStatus.Success)
                        {
                            WifiSitter.LogLine(ConsoleColor.Green, new string[] { "NIC: {0}  IP status: {1}", Nic.Name, pingResult.Status.ToString() });
                            this.IsConnected = true;
                        }
                        else
                        {
                            WifiSitter.LogLine(ConsoleColor.Red, new string[] { "NIC: {0}  IP status: {1}", Nic.Name, pingResult.Status.ToString() });
                            this.IsConnected = false;
                        }
                    }
                }
            }
            else
            {
                // If you're using a self assigned IP, you're not as connected as you think you are.
                this.IsConnected = false;
            }
        }

        public async void UpdateState(List<NetshInterface> NetshIfs) {
            this.UpdateState(NetshIfs.Where(x => x.InterfaceName == Nic.Name).FirstOrDefault());
        }

        public async void UpdateState(NetshInterface NetshIf) {
            if (NetshIf == null) return;

            if (Nic.Name == NetshIf.InterfaceName) {
                this._isEnabled = NetshIf.AdminState == "Enabled";
                this._isConnected = NetshIf.State == "Connected";
            }

            //Double Check
            if (IsConnected)
            {
                CheckYourself();
            }
        }

        public bool Disable() {
            // Release IP first and update NIC inforamtion so OperationalState reflects this
            this.ReleaseIp();

            // Disable interface
            int exitCode = EnableDisableInterface(false);

            var netsh = GetNetshInterface();

            if (netsh != null) {
                this.UpdateState(netsh);
            }
            else {
                this._isEnabled = false;
                this._isConnected = false;
            }

            return !IsEnabled;
        }

        public bool Enable() {
            int exitCode = EnableDisableInterface(true);

            var netsh = GetNetshInterface();

            if (netsh != null) {
                this.UpdateState(netsh);
            }
            else {
                this._isEnabled = false;
                this._isConnected = false;
            }

            return IsEnabled;
        }

        public void Disconnect() {
            if (Nic?.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) return;

            var wclient = new NativeWifi.WlanClient();
            var adapter = wclient.Interfaces.Where(x => Nic.Id.Contains(x.InterfaceGuid.ToString().ToUpper())).FirstOrDefault();
            if (adapter == null) return;

            // Store connection info and disconnect
            // * note: Connection info is stored as struct so incurs a copy
            _lastConnection = adapter.CurrentConnection;
            WifiSitter.LogLine(LogType.info, $"{Name}  disconnecting from: {_lastConnection.profileName}");
            adapter?.Disconnect();
        }

        public void ConnectToLastSsid() {
            if (Nic?.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) return;  // Shouldn't happen but still..

            if (!_lastConnection.Equals(default(WlanConnectionAttributes)))
            {
                WifiSitter.LogLine(LogType.info, $"{Name}  reconnecting to: {_lastConnection.profileName}");
                var wclient = new NativeWifi.WlanClient();
                var adapter = wclient.Interfaces.Where(x => Nic.Id.Contains(x.InterfaceGuid.ToString().ToUpper())).FirstOrDefault();
                adapter.SetProfile(WlanProfileFlags.AllUser, adapter.GetProfileXml(_lastConnection.profileName), true);
                adapter.ConnectSynchronously(WlanConnectionMode.Profile, Dot11BssType.Any, _lastConnection.profileName, 30);
            }
        }

        private int EnableDisableInterface(bool Enable) {
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

        private bool ReleaseIp() {
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

        private NetshInterface GetNetshInterface() {
            List<NetshInterface> _netsh;
            NetshInterface netsh = null;
            if ((_netsh = NetshHelper.GetInterfaces()) != null)
                netsh = _netsh.Where(x => x.InterfaceName == this.Name).FirstOrDefault();
            return netsh;
        }

        #endregion // methods
    }
}
