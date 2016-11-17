using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WifiSitter
{
    [Serializable]
    public class WifiSitterIpcMessage
    {
        public string Request { get; set; }
        public string Requestor { get; set;  }
        public string Target { get; set; }
        public string Payload { get; set; }

        public WifiSitterIpcMessage(string Verb, string WhosAsking, string WhereTo, string SendingWhat = "") {
            Request = Verb;
            Requestor = WhosAsking;
            Target = WhereTo;
            Payload = SendingWhat;
        }
    }

    public static class WifiSitterExtensions
    {
        public static string IpcMessageJsonString(this WifiSitterIpcMessage message) {
            string result;
            result = Newtonsoft.Json.JsonConvert.SerializeObject(message);
            return result;
        }
    }
}
