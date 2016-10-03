using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

using WifiSitter.Helpers;

namespace WifiSitter
{
    /// <summary>
    /// Object that contains information from NetworkInterface objects
    /// as well as netsh output (Admin State: Enabled/Disabled).
    /// </summary>
    public class TrackedNic
    {
        private NetworkInterface _nic;
        private bool _isEnabled;
        private bool _isConnected;

        #region constructor
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
        }

        public bool IsEnabled {
            get { return _isEnabled; }
            set { _isEnabled = value; }
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


        public bool IsConnected {
            get { return _isConnected; }
            set { _isConnected = value; }
        }

        #endregion // properties


        #region methods

        public void UpdateState(List<NetshInterface> NetshIfs) {
            this.UpdateState(NetshIfs.Where(x => x.InterfaceName == Nic.Name).FirstOrDefault());
        }

        public void UpdateState(NetshInterface NetshIf) {
            if (NetshIf == null) return;

            if (Nic.Name == NetshIf.InterfaceName) {
                this._isEnabled = NetshIf.AdminState == "Enabled";
                this._isConnected = NetshIf.State == "Connected";
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
