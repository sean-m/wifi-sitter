using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace WifiSitter
{
    [Serializable]
    public class WifiSitterIpcMessage
    {
        public string Request { get; set; }
        public string Requestor { get; set;  }
        public string Target { get; set; }
        public byte[] Payload { get; set; }
        public Type PayloadType { get; set; }

        public WifiSitterIpcMessage() { }

        public WifiSitterIpcMessage(string Verb, string WhosAsking, string WhereTo, string SendingWhat = "") {
            Request = Verb;
            Requestor = WhosAsking;
            Target = WhereTo;
            Payload = System.Text.Encoding.UTF8.GetBytes(SendingWhat);
            PayloadType = typeof(string);
        }

        public WifiSitterIpcMessage(string Verb, string WhosAsking, string WhereTo, object SendingWhat, Type type) {
            Request = Verb;
            Requestor = WhosAsking;
            Target = WhereTo;
            Payload = SendingWhat.ObjectToByteArray();
            PayloadType = SendingWhat.GetType();
        }
    }

    public static class WifiSitterExtensions
    {
        public static string IpcMessageJsonString(this WifiSitterIpcMessage message) {
            string result;
            result = Newtonsoft.Json.JsonConvert.SerializeObject(message);
            return result;
        }

        public static byte[] ObjectToByteArray(this object obj) {
            BinaryFormatter bf = new BinaryFormatter();
            using (var ms = new MemoryStream()) {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        public static Object ByteArrayToObject(this byte[] arrBytes) {
            using (var memStream = new MemoryStream()) {
                var binForm = new BinaryFormatter();
                memStream.Write(arrBytes, 0, arrBytes.Length);
                memStream.Seek(0, SeekOrigin.Begin);
                var obj = binForm.Deserialize(memStream);
                return obj;
            }
        }
    }
}
