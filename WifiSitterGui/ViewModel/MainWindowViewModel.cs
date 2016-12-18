﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Timers;

using WifiSitter;
using WifiSitter.Model;

namespace WifiSitterGui.ViewModel
{
    public class MainWindowViewModel : MvvmObservable
    {
        #region fields

        SimpleNetworkState _netState;
        private ServiceController _sc;
        private Timer _refreshProperties;
        private WifiSitterAgentViewModel _parent;

        #endregion  // fields


        #region constructor

        public MainWindowViewModel() { }

        public MainWindowViewModel (WifiSitterAgentViewModel Parent) {
            _parent = Parent;

            _refreshProperties = new Timer();
            _refreshProperties.Interval = 1000 * 5;
            _refreshProperties.AutoReset = true;
            _refreshProperties.Elapsed += (o, e) => {
                this.OnPropertyChanged("ServiceState");
            };
            _refreshProperties.Start();
        }
        
        #endregion  // constructor


        #region properties
        
        public SimpleNetworkState NetState {
            get { return _netState; }
            set { _netState = value; OnPropertyChanged(null); }
        }

        
        public List<SimpleNic> WiredNics {
            get {
                return NetState.Nics.Where(x => x.InterfaceType == "Ethernet").Where(x => !NetState.IgnoreAdapters.Any(y => x.Description.StartsWith(y))).ToList();
            }
        }


        public List<SimpleNic> WirelessNics {
            get {
                return NetState.Nics.Where(x => x.InterfaceType != "Ethernet").Where(x => !NetState.IgnoreAdapters.Any(y => x.Description.StartsWith(y))).ToList();
            }
        }


        public List<NetworkInterface> IgnoredNics {
            get {
                if (NetState == null) return null;
                var n = NetworkInterface.GetAllNetworkInterfaces();

                return n.Where(x => NetState.IgnoreAdapters.Any(y => x.Description.StartsWith(y))).ToList();
            }
        }
        

        public List<string> Whitelist {
            get {
                if (NetState == null) return null;
                return NetState.IgnoreAdapters;
            }
        }


        public string ServiceState {
            get {
                ServiceControllerStatus status;
                try { if (_sc == null) _sc = new ServiceController("WifiSitter"); status = _sc.Status; }
                catch { return "No Service"; }

                _sc.Refresh();
                switch (status) {
                    case ServiceControllerStatus.Running:
                        return "Running";
                    case ServiceControllerStatus.Stopped:
                        return "Stopped";
                    case ServiceControllerStatus.Paused:
                        return "Paused";
                    case ServiceControllerStatus.StopPending:
                        return "Stopping";
                    case ServiceControllerStatus.StartPending:
                        return "Starting";
                    default:
                        return "Status Changing";
                }
            }
        }

        #endregion  // properties


        #region methods
        #endregion  // methods


        #region eventhandlers
        #endregion  // methods
    }
}
