using Harmonic.Networking.Amf.Attributes;
using Harmonic.Networking.Amf.Data;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UnitTest
{
    public class RemotingMessage : IDynamicObject
    {
        private Dictionary<string, object> _dynamicFields = new Dictionary<string, object>();
        
        [ClassField(Name = "body")]
        public object Body { get; set; }
        [ClassField(Name = "clientId")]
        public object ClientId { get; set; }
        [ClassField(Name = "destination")]
        public object Destination { get; set; }
        [ClassField(Name = "headers")]
        public object Headers { get; set; }
        [ClassField(Name = "messageId")]
        public object MessageId { get; set; }
        [ClassField(Name = "operation")]
        public object Operation { get; set; }
        [ClassField(Name = "source")]
        public object Source { get; set; }
        [ClassField(Name = "timeToLive")]
        public object TimeToLive { get; set; }
        [ClassField(Name = "timestamp")]
        public object Timestamp { get; set; }

        public IReadOnlyDictionary<string, object> DynamicFields { get => _dynamicFields; }

        public void AddDynamic(string key, object data)
        {
            _dynamicFields.Add(key, data);
        }
    }

    [TestClass]
    public class TestAmf3Reader
    {
        [TestMethod]
        public void TestReadPacket1()
        {
            var reader = new Amf0Reader();
            reader.RegisterType<RemotingMessage>("flex.messaging.messages.RemotingMessage");
            reader.StrictMode = false;
            using (var file = new FileStream("test.amf3", FileMode.Open))
            {
                var data = new byte[file.Length];
                file.Read(data);
                Assert.IsTrue(reader.TryGetPacket(data, out var headers, out var messages, out var consumed)) ;
                Assert.AreEqual(consumed, file.Length);
            }
        }
    }
}
