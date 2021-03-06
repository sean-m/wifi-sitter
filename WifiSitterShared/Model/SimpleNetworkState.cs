﻿using System;
using System.Collections.Generic;

using WifiSitterShared;

namespace WifiSitter.Model
{
    [Serializable]
    public class SimpleNetworkState
    {
        public bool EthernetUp { get; set; }
        public bool InternetConnected { get; set; }
        public bool CheckNet { get; set; }
        public IEnumerable<SimpleNic> Nics { get; set; }
        public bool NetworkAvailable { get; set; }
        public bool ProcessingState { get; set; }
        public bool Paused { get; set; }
        public IEnumerable<string> IgnoreAdapters { get; set; }

        public SimpleNetworkState() { }
    }

    [Serializable]
    public class SimpleNic {
        public bool IsEnabled { get; set; }
        public bool IsConnected { get; set; }
        public bool IsInternetConnected { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Id { get; set; }
        public string InterfaceType { get; set; }

        public SimpleNic() { }
        
        public SimpleNic(TrackedNic nic) {
            IsEnabled = nic.IsEnabled;
            IsConnected = nic.IsConnected;
            IsInternetConnected = nic.IsInternetConnected;
            Name = nic.Name;
            Description = nic.Description;
            Id = nic.Id.ToString();
            InterfaceType = nic.InterfaceType.ToString();
        }
    }
}
