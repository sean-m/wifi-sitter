using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WifiSitter;
using WifiSitterShared;

namespace WifiSitter.Model
{
    [Serializable]
    public class SimpleNetworkState
    {
        public bool EthernetUp { get; set; }
        public bool CheckNet { get; set; }
        public List<SimpleNic> Nics { get; set; }
        public bool NetworkAvailable { get; set; }
        public bool ProcessingState { get; set; }
        public List<string> IgnoreAdapters { get; set; }

        public SimpleNetworkState() { }
    }

    [Serializable]
    public class SimpleNic {
        public bool IsEnabled { get; set; }
        public bool IsConnected { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Id { get; set; }
        public string InterfaceType { get; set; }

        public SimpleNic() { }
        
        public SimpleNic(TrackedNic nic) {
            IsEnabled = nic.IsEnabled;
            IsConnected = nic.IsConnected;
            Name = nic.Name;
            Description = nic.Description;
            Id = nic.Id.ToString();
            InterfaceType = nic.InterfaceType.ToString();
        }
    }
}
