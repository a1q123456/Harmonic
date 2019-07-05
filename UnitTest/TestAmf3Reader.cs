using Harmonic.Buffers;
using Harmonic.Networking.Amf.Attributes;
using Harmonic.Networking.Amf.Data;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Harmonic.Networking.Amf.Serialization.Amf3;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnitTest
{

    [TypedObject(Name = "TestClass")]
    public class TestCls : IDynamicObject, IEquatable<TestCls>
    {
        [ClassField(Name = "t1")]
        public double T1 { get; set; }
        [ClassField(Name = "t2")]
        public string T2 { get; set; }
        [ClassField(Name = "t3")]
        public string T3 { get; set; }
        [ClassField]
        public Vector<int> t4 { get; set; }

        private Dictionary<string, object> _dynamicFields = new Dictionary<string, object>();

        public IReadOnlyDictionary<string, object> DynamicFields => _dynamicFields;

        public void AddDynamic(string key, object data)
        {
            _dynamicFields.Add(key, data);
        }

        public bool Equals(TestCls other)
        {
            return T1 == other.T1 && T2 == other.T2 && T3 == other.T3 && (t4 != null ? t4.Equals(other.t4) : t4 == other.t4) && _dynamicFields.SequenceEqual(other._dynamicFields);
        }

        public override bool Equals(object obj)
        {
            if (obj is TestCls to)
            {
                return Equals(to);
            }
            return base.Equals(obj);
        }
    }

    public class iexternalizable : IExternalizable
    {
        public double v1;
        public int v2;

        public bool TryEncodeData(UnlimitedBuffer buffer)
        {
            var b1 = BitConverter.GetBytes(v1);
            var b2 = BitConverter.GetBytes(v2);
            buffer.WriteToBuffer(b1);
            buffer.WriteToBuffer(b2);
            return true;
        }

        public bool TryDecodeData(Span<byte> buffer, out int consumed)
        {
            v1 = BitConverter.ToDouble(buffer);
            v2 = BitConverter.ToInt32(buffer.Slice(sizeof(double)));
            consumed = sizeof(double) + sizeof(int);
            return true;
        }
    }

    [TypedObject(Name = "flex.messaging.messages.RemotingMessage")]
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
        public void TestReadNumber()
        {
            var reader = new Amf3Reader();

            var files = Directory.GetFiles("../../../../samples/amf3/number");

            foreach (var file in files)
            {
                var value = double.Parse(Path.GetFileNameWithoutExtension(file));
                using (var f = new FileStream(file, FileMode.Open))
                {
                    var data = new byte[f.Length];
                    f.Read(data);
                    Assert.IsTrue(reader.TryGetDouble(data, out var dataRead, out var consumed));
                    Assert.AreEqual(dataRead, value);
                    Assert.AreEqual(consumed, f.Length);
                }
            }
        }

        [TestMethod]
        public void TestReadInteger()
        {
            var reader = new Amf3Reader();

            var files = Directory.GetFiles("../../../../samples/amf3/intenger");

            foreach (var file in files)
            {
                var value = uint.Parse(Path.GetFileNameWithoutExtension(file));
                using (var f = new FileStream(file, FileMode.Open))
                {
                    var data = new byte[f.Length];
                    f.Read(data);
                    Assert.IsTrue(reader.TryGetUInt29(data, out var dataRead, out var consumed));
                    Assert.AreEqual(dataRead, value);
                    Assert.AreEqual(consumed, f.Length);
                }
            }
        }

        [TestMethod]
        public void TestReadString()
        {
            var reader = new Amf3Reader();

            var files = Directory.GetFiles("../../../../samples/amf3/string");

            foreach (var file in files)
            {
                var value = Path.GetFileNameWithoutExtension(file);
                using (var f = new FileStream(file, FileMode.Open))
                {
                    var data = new byte[f.Length];
                    f.Read(data);
                    Assert.IsTrue(reader.TryGetString(data, out var dataRead, out var consumed));
                    Assert.AreEqual(dataRead, value);
                    Assert.AreEqual(consumed, f.Length);
                }
            }
        }

        [TestMethod]
        public void TestReadBoolean()
        {
            var reader = new Amf3Reader();

            var files = Directory.GetFiles("../../../../samples/amf3/boolean");

            foreach (var file in files)
            {
                var value = bool.Parse(Path.GetFileNameWithoutExtension(file));
                using (var f = new FileStream(file, FileMode.Open))
                {
                    var data = new byte[f.Length];
                    f.Read(data);
                    Assert.IsTrue(reader.TryGetBoolean(data, out var dataRead, out var consumed));
                    Assert.AreEqual(dataRead, value);
                    Assert.AreEqual(consumed, f.Length);
                }
            }
        }

        [TestMethod]
        public void TestReadPacket1()
        {
            var reader = new Amf0Reader();
            reader.RegisterType<RemotingMessage>();
            reader.StrictMode = false;
            using (var file = new FileStream("../../../../samples/amf3/misc/packet.amf3", FileMode.Open))
            {
                var data = new byte[file.Length];
                file.Read(data);
                Assert.IsTrue(reader.TryGetPacket(data, out var headers, out var messages, out var consumed));
                Assert.AreEqual(consumed, file.Length);
            }
        }


        [TestMethod]
        public void TestUndefined()
        {
            var reader = new Amf3Reader();
            reader.RegisterTypedObject<RemotingMessage>();

            using (var file = new FileStream("../../../../samples/amf3/misc/undefined.amf3", FileMode.Open))
            {
                var data = new byte[file.Length];
                file.Read(data);
                Assert.IsTrue(reader.TryGetUndefined(data, out var value, out var consumed));
                Assert.AreEqual(consumed, file.Length);
            }
        }

        [TestMethod]
        public void TestNull()
        {
            var reader = new Amf3Reader();
            reader.RegisterTypedObject<RemotingMessage>();

            using (var file = new FileStream("../../../../samples/amf3/misc/null.amf3", FileMode.Open))
            {
                var data = new byte[file.Length];
                file.Read(data);
                Assert.IsTrue(reader.TryGetNull(data, out var value, out var consumed));
                Assert.AreEqual(consumed, file.Length);
            }
        }

        [TestMethod]
        public void TestDate()
        {
            var reader = new Amf3Reader();
            reader.RegisterTypedObject<RemotingMessage>();

            using (var file = new FileStream("../../../../samples/amf3/misc/date.amf3", FileMode.Open))
            {
                var data = new byte[file.Length];
                file.Read(data);
                Assert.IsTrue(reader.TryGetDate(data, out var dataRead, out var consumed));
                Assert.AreEqual(dataRead.Year, 2019);
                Assert.AreEqual(dataRead.Month, 2);
                Assert.AreEqual(dataRead.Day, 11);
                Assert.AreEqual(consumed, file.Length);
            }
        }

        [TestMethod]
        public void TestReadObject()
        {
            var reader = new Amf3Reader();

            using (var file = new FileStream("../../../../samples/amf3/misc/object.amf3", FileMode.Open))
            {
                var data = new byte[file.Length];
                file.Read(data);

                Assert.IsTrue(reader.TryGetObject(data, out var dataRead, out var consumed));
                var obj = (Amf3Object)dataRead;
                Assert.IsTrue(obj.Fields.SequenceEqual(new Dictionary<string, object>() { ["t1"] = 1.0, ["t2"] = "aaa", ["t3"] = "aac" }));
                Assert.AreEqual(obj.DynamicFields["td"], "aacf");
                var td2 = (Dictionary<object, object>)obj.DynamicFields["td2"];

                var keyList = td2.Keys.ToList();
                var key0 = (string)keyList[0];
                var key1 = (double)keyList[1];
                var key2 = (Vector<double>)keyList[2];
                var key3 = (Vector<uint>)keyList[3];

                var v0 = (double)td2[key0];
                var v1 = (Vector<int>)td2[key1];
                var v2 = (Vector<int>)td2[key2];
                var v3 = (Vector<int>)td2[key3];

                Assert.AreEqual(key0, "test");
                Assert.AreEqual(key1, 3);
                Assert.AreEqual(key2[0], 3.0);
                Assert.AreEqual(key2[1], 4.0);
                Assert.AreEqual(key2[2], 5.0);
                Assert.AreEqual(key3[0], (uint)32);
                Assert.AreEqual(key3[1], (uint)43);
                Assert.AreEqual(key3[2], (uint)54);
                Assert.AreEqual(v0, 1);
                Assert.AreEqual(v1[0], 2);
                Assert.AreEqual(v1[1], 3);
                Assert.AreEqual(v1[2], 4);
                Assert.AreEqual(v2[0], 2);
                Assert.AreEqual(v2[1], 3);
                Assert.AreEqual(v2[2], 4);
                Assert.AreEqual(v3[0], 2);
                Assert.AreEqual(v3[1], 3);
                Assert.AreEqual(v3[2], 4);


                Assert.AreEqual(consumed, file.Length);
            }
        }

        [TestMethod]
        public void TestReadXml()
        {
            var reader = new Amf3Reader();


            using (var f = new FileStream("../../../../samples/amf3/misc/xml.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetXml(data, out var dataRead, out var consumed));
                Assert.AreNotEqual(dataRead.GetElementsByTagName("a").Count, 0);
                Assert.AreNotEqual(dataRead.GetElementsByTagName("b").Count, 0);
                Assert.IsNotNull(dataRead.GetElementsByTagName("b")[0].Attributes["value"], "1");
                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadXmlDocument()
        {
            var reader = new Amf3Reader();


            using (var f = new FileStream("../../../../samples/amf3/misc/xml_document.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetXmlDocument(data, out var dataRead, out var consumed));
                Assert.AreNotEqual(dataRead.GetElementsByTagName("a").Count, 0);
                Assert.AreEqual(dataRead.GetElementsByTagName("a")[0].Attributes["value"].Value, "1");
                Assert.AreNotEqual(dataRead.GetElementsByTagName("b").Count, 0);
                Assert.AreEqual(dataRead.GetElementsByTagName("b")[0].FirstChild.Value, "2");
                Assert.AreEqual(consumed, f.Length);
            }
        }


        [TestMethod]
        public void TestReadArray()
        {
            var reader = new Amf3Reader();


            using (var f = new FileStream("../../../../samples/amf3/misc/array.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetArray(data, out var dataRead, out var consumed));
                Assert.AreEqual(dataRead[0], 1.0);
                Assert.AreEqual(dataRead[1], "aa");
                var v = (Vector<int>)dataRead["aa"];
                Assert.AreEqual(v[0], 1);
                Assert.AreEqual(v[1], 2);
                Assert.AreEqual(v[2], 3);
                Assert.IsInstanceOfType(dataRead["bb"], typeof(Dictionary<object, object>));

                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadDictionary()
        {
            var reader = new Amf3Reader();


            using (var f = new FileStream("../../../../samples/amf3/misc/dictionary.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetDictionary(data, out var dataRead, out var consumed));
                var keys = dataRead.Keys.ToList();
                var k0 = keys[0];
                var k2 = (Amf3Object)keys[1];
                var k1 = keys[2];

                var v0 = dataRead[k0];
                var v1 = (Vector<int>)dataRead[k1];
                var v2 = (Vector<int>)dataRead[k2];

                Assert.AreEqual(k0, "test");
                Assert.AreEqual(k1, 3.0);

                Assert.AreEqual(k2.Fields["t1"], 1.0);
                Assert.AreEqual(k2.Fields["t2"], "aaa");
                Assert.AreEqual(k2.Fields["t3"], "aac");

                Assert.AreEqual(v0, 1.0);
                Assert.AreEqual(v1[0], 2);
                Assert.AreEqual(v1[1], 3);
                Assert.AreEqual(v1[2], 4);

                Assert.AreEqual(v2[0], 2);
                Assert.AreEqual(v2[1], 3);
                Assert.AreEqual(v2[2], 4);

                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadVectorInt()
        {
            var reader = new Amf3Reader();


            using (var f = new FileStream("../../../../samples/amf3/misc/vector_int.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetVectorInt(data, out var dataRead, out var consumed));
                Assert.AreEqual(dataRead[0], 1);
                Assert.AreEqual(dataRead[1], 2);
                Assert.AreEqual(dataRead[2], 3);

                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadVectorUint()
        {
            var reader = new Amf3Reader();


            using (var f = new FileStream("../../../../samples/amf3/misc/vector_uint.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetVectorUint(data, out var dataRead, out var consumed));
                Assert.AreEqual(dataRead[0], (uint)1);
                Assert.AreEqual(dataRead[1], (uint)2);
                Assert.AreEqual(dataRead[2], (uint)3);

                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadVectorDouble()
        {
            var reader = new Amf3Reader();


            using (var f = new FileStream("../../../../samples/amf3/misc/vector_double.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetVectorDouble(data, out var dataRead, out var consumed));
                Assert.AreEqual(dataRead[0], (double)1);
                Assert.AreEqual(dataRead[1], (double)2);
                Assert.AreEqual(dataRead[2], (double)3);

                Assert.AreEqual(consumed, f.Length);
            }
        }


        [TestMethod]
        public void TestReadVectorTyped()
        {
            var reader = new Amf3Reader();
            reader.RegisterTypedObject<TestCls>();

            using (var f = new FileStream("../../../../samples/amf3/misc/vector_typted_object.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetVectorObject(data, out var dataRead, out var consumed));

                var v = (Vector<TestCls>)dataRead;

                var t = v[0];
                Assert.AreEqual(t.T1, 1.0);
                Assert.AreEqual(t.T2, "aaa");
                Assert.AreEqual(t.T3, "aac");
                Assert.AreEqual(t.t4[0], 1);
                Assert.AreEqual(t.t4[1], 2);
                Assert.AreEqual(t.t4[2], 3);

                var t2 = v[1];
                Assert.AreEqual(t2.T1, 1.0);
                Assert.AreEqual(t2.T2, "aaa");
                Assert.AreEqual(t2.T3, "aac");
                Assert.AreEqual(t2.t4[0], 1);
                Assert.AreEqual(t2.t4[1], 2);
                Assert.AreEqual(t2.t4[2], 3);
                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadVectorAny()
        {
            var reader = new Amf3Reader();

            using (var f = new FileStream("../../../../samples/amf3/misc/vector_any_object.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetVectorObject(data, out var dataRead, out var consumed));

                var v = (Vector<object>)dataRead;
                var obj = (Amf3Object)v[0];

                Assert.AreEqual(obj.Fields["t1"], 1.0);
                Assert.AreEqual(obj.Fields["t2"], "aaa");
                Assert.AreEqual(obj.Fields["t3"], "aac");

                Assert.AreEqual(v[1], 2.0);
                Assert.AreEqual(consumed, data.Length);
            }
        }

        [TestMethod]
        public void TestReadByteArray()
        {
            var reader = new Amf3Reader();

            using (var f = new FileStream("../../../../samples/amf3/misc/bytearray.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetByteArray(data, out var dataRead, out var consumed));

                Assert.AreEqual(dataRead[0], (byte)1);
                Assert.AreEqual(dataRead[1], (byte)2);
                Assert.AreEqual(dataRead[2], (byte)3);

                Assert.AreEqual(consumed, data.Length);
            }
        }

        [TestMethod]
        public void TestReadExternalizable()
        {
            var reader = new Amf3Reader();
            reader.RegisterExternalizable<iexternalizable>();
            using (var f = new FileStream("../../../../samples/amf3/misc/externalizable.amf3", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetObject(data, out var dataRead, out var consumed));
                var ie = (iexternalizable)dataRead;
                Assert.AreEqual(ie.v1, 3.14);
                Assert.AreEqual(ie.v2, 333);

                Assert.AreEqual(consumed, data.Length);
            }
        }

    }
}
