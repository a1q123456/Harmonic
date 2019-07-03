using Harmonic.Networking.Amf.Attributes;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Amf.Data;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace UnitTest
{
    [TestClass]
    public class TestAmf0Writer
    {
        [TestMethod]
        public void TestNumber()
        {
            var random = new Random();
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            for (int i = 0; i < 1000; i++)
            {
                var num = random.NextDouble() * 10 - 5;
                writer.WriteBytes(num);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetNumber(buffer, out var readValue, out var consumed);
                Assert.AreEqual(num, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
            for (int i = 0; i < 1000; i++)
            {
                var num = random.Next(-10, 10);
                writer.WriteBytes(num);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetNumber(buffer, out var readValue, out var consumed);
                Assert.AreEqual(num, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
            for (int i = 0; i < 1000; i++)
            {
                var num = (float)random.Next(-10, 10);
                writer.WriteBytes(num);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetNumber(buffer, out var readValue, out var consumed);
                Assert.AreEqual(num, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
            for (int i = 0; i < 1000; i++)
            {
                var num = (long)random.Next(-10, 10);
                writer.WriteBytes(num);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetNumber(buffer, out var readValue, out var consumed);
                Assert.AreEqual(num, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
            for (int i = 0; i < 1000; i++)
            {
                var num = (ulong)random.Next(-10, 10);
                writer.WriteBytes(num);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetNumber(buffer, out var readValue, out var consumed);
                Assert.AreEqual(num, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
            for (int i = 0; i < 1000; i++)
            {
                var num = (ushort)random.Next(-10, 10);
                writer.WriteBytes(num);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetNumber(buffer, out var readValue, out var consumed);
                Assert.AreEqual(num, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
            for (int i = 0; i < 1000; i++)
            {
                var num = (short)random.Next(-10, 10);
                writer.WriteBytes(num);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetNumber(buffer, out var readValue, out var consumed);
                Assert.AreEqual(num, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
        }

        [TestMethod]
        public void TestString()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            for (int i = 0; i < 1000; i++)
            {
                var val = Guid.NewGuid().ToString();
                writer.WriteBytes(val);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetString(buffer, out var readValue, out var consumed);
                Assert.AreEqual(val, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
        }

        [TestMethod]
        public void TestLongString()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            for (int i = 0; i < 1000; i++)
            {
                var val = string.Concat(Enumerable.Repeat(Guid.NewGuid().ToString(), 2000));
                writer.WriteBytes(val);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);

                reader.TryGetLongString(buffer, out var readValue, out var consumed);
                Assert.AreEqual(val, readValue);
                Assert.AreEqual(buffer.Length, consumed);
            }
        }

        [TestMethod]
        public void TestDate()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            var date = DateTime.Now;

            writer.WriteBytes(date);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetDate(buffer, out var val, out var consumed));
            Assert.AreEqual(val.Date, date.Date);
            Assert.AreEqual(val.Hour, date.Hour);
            Assert.AreEqual(val.Minute, date.Minute);
            Assert.AreEqual(val.Second, date.Second);
            Assert.AreEqual(val.Millisecond, date.Millisecond);
            Assert.AreEqual(val.Kind, date.Kind);
            Assert.AreEqual(consumed, buffer.Length);
        }

        [TestMethod]
        public void TestBoolean()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            writer.WriteBytes(true);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);
            
            Assert.IsTrue(reader.TryGetBoolean(buffer, out var val, out var consumed));
            Assert.IsTrue(val);
            Assert.AreEqual(consumed, buffer.Length);

            writer.WriteBytes(false);
            buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetBoolean(buffer, out val, out consumed));
            Assert.IsFalse(val);
            Assert.AreEqual(consumed, buffer.Length);
        }

        [TestMethod]
        public void TestArray()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            var array = new List<object>()
            {
                1, 3.0, "string", new DateTime(2019, 2, 11), false, new List<object>() { null, 3, "string2", "string2" }
            };

            writer.WriteBytes(array);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetStrictArray(buffer, out var val, out var consumed));
            Assert.IsTrue((double)val[0] == 1.0);
            Assert.IsTrue((double)val[1] == 3.0);
            Assert.IsTrue((string)val[2] == "string");
            Assert.IsTrue((DateTime)val[3] == new DateTime(2019, 2, 11));
            Assert.IsTrue((bool)val[4] == false);
            var e5 = (List<object>)val[5];

            Assert.IsTrue(e5[0] == null);
            Assert.IsTrue((double)e5[1] == 3.0);
            Assert.IsTrue((string)e5[2] == "string2");
            Assert.IsTrue((string)e5[3] == "string2");
        }

        [TestMethod]
        public void TestEcmaArray()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            var array = new Dictionary<string, object>()
            {
                ["1"] = 1.0,
                ["2"] = 2.0,
                ["3"] = "a",
                ["4"] = "a" 
            };

            writer.WriteBytes(array);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);
            // EcmaMarker:byte + ElementCount: uint + 
            // StringLength: ushort + StringContent: byte * 1 + NumberMarker: byte + Number: double + 
            // StringLength: ushort + StringContent: byte * 1 + NumberMarker: byte + Number: double 
            // StringLength: ushort + StringContent: byte * 1 + StringMarker: byte + StringLength: ushort + StringContent: byte * 1 + 
            // StringLength: ushort + StringContent: byte * 1 + ReferenceMarker: byte + ReferenceIndex: ushort +
            // StringLength: ushort + StringConent: byte * 0 + ObjectEndMarker: byte
            Assert.AreEqual(buffer.Length,  45);
            Assert.IsTrue(reader.TryGetEcmaArray(buffer, out var readData, out var consumed));

            Assert.IsTrue(readData.SequenceEqual(array));
        }

        [TestMethod]
        public void TestNull()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            writer.WriteNullBytes();
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetNull(buffer, out var nullObj, out var consunmed));
            Assert.IsNull(nullObj);
            Assert.AreEqual(consunmed, buffer.Length);
            
        }

        [TestMethod]
        public void TestUndefined()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            writer.WriteBytes(new Undefined());
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetUndefined(buffer, out var ud, out var consunmed));
            Assert.IsNotNull(ud);
            Assert.AreEqual(consunmed, buffer.Length);

        }

        [TestMethod]
        public void TestXml()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            var xml = new XmlDocument();
            var elem = xml.CreateElement("price");
            xml.AppendChild(elem);
            writer.WriteBytes(xml);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);
            
            Assert.IsTrue(reader.TryGetXmlDocument(buffer, out var ud, out var consunmed));
            Assert.IsNotNull(ud);
            Assert.AreNotEqual(ud.GetElementsByTagName("price").Count, 0);
            Assert.AreEqual(consunmed, buffer.Length);

        }

        [TestMethod]
        public void TestObject()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();

            object nullVal = null;
            var refVal = new List<object>() { 1, 2, "test" };

            var obj = new
            {
                c = 1.0,
                test = false,
                test2 = nullVal,
                test3 = new Undefined(),
                test4 = refVal,
                test5 = "test",
                test6 = refVal
            };

            writer.WriteBytes(obj);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            // test reference table is working
            Assert.AreEqual(buffer.Length, 97);
            Assert.IsTrue(reader.TryGetObject(buffer, out var readObj, out var consumed));
            Assert.AreEqual(consumed, buffer.Length);
        }

        [TypedObject(Name = "Another.Name")]
        class TypedClass : IDynamicObject
        {
            [ClassField]
            public double c { get; set; }
            [ClassField]
            public bool test { get; set; }
            [ClassField]
            public object test2 { get; set; }
            [ClassField]
            public Undefined test3 { get; set; }
            [ClassField]
            public List<object> test4 { get; set; }
            [ClassField]
            public string test5 { get; set; }
            [ClassField]
            public List<object> test6 { get; set; }

            private Dictionary<string, object> _dynamicFields = new Dictionary<string, object>();

            public IReadOnlyDictionary<string, object> DynamicFields => _dynamicFields;

            public void AddDynamic(string key, object data)
            {
                _dynamicFields.Add(key, data);
            }
        }

        [TestMethod]
        public void TestTypedObject()
        {
            var writer = new Amf0Writer();
            var reader = new Amf0Reader();
            reader.RegisterType<TypedClass>("Another.Name");

            object nullVal = null;
            var refVal = new List<object>() { 1, 2, "test" };

            var obj = new TypedClass()
            {
                c = 1.0,
                test = false,
                test2 = nullVal,
                test3 = new Undefined(),
                test4 = refVal,
                test5 = "test",
                test6 = refVal
            };

            writer.WriteTypedBytes(obj);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);
            
            Assert.IsTrue(reader.TryGetTypedObject(buffer, out var readObj, out var consumed));
            Assert.AreEqual(consumed, buffer.Length);
        }

    }
}
