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
    [TestClass]
    public class TestAmf3Writer
    {
        [TestMethod]
        public void TestDouble()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();
            var random = new Random();

            for (int i = 0; i < 1000; i++)
            {
                var value = random.NextDouble();

                writer.WriteBytes(value);
                var buffer = new byte[writer.MessageLength];
                writer.GetMessage(buffer);
                reader.TryGetDouble(buffer, out var readValue, out var consumed);
                Assert.AreEqual(readValue, value);
                Assert.AreEqual(consumed, buffer.Length);
            }
        }

        [TestMethod]
        public void TestInt()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();
            var backend = new byte[5];

            for (int i = 0; i <= Amf3Writer.U29MAX; i += 0xFF)
            {
                var value = (uint)i;
                writer.WriteBytes(value);
                var buffer = backend.AsSpan(0, writer.MessageLength);
                buffer.Clear();
                writer.GetMessage(buffer);
                Assert.IsTrue(reader.TryGetUInt29(buffer, out var readValue, out var consumed));
                Assert.AreEqual(readValue, value);
                Assert.AreEqual(consumed, buffer.Length);
            }
        }

        [TestMethod]
        public void TestBoolean()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            writer.WriteBytes(true);
            var buffer = new byte[writer.MessageLength]; ;
            writer.GetMessage(buffer);
            Assert.IsTrue(reader.TryGetBoolean(buffer, out var readVal, out var consumed));
            Assert.AreEqual(buffer.Length, consumed);
            Assert.IsTrue(readVal);

            writer.WriteBytes(false);
            writer.GetMessage(buffer);
            Assert.IsTrue(reader.TryGetBoolean(buffer, out readVal, out consumed));
            Assert.AreEqual(buffer.Length, consumed);
            Assert.IsFalse(readVal);
        }

        [TestMethod]
        public void TestNull()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            writer.WriteBytes((object)null);
            var buffer = new byte[writer.MessageLength];

            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetNull(buffer, out var readVal, out var consumed));
            Assert.IsNull(readVal);
            Assert.AreEqual(buffer.Length, consumed);

        }

        [TestMethod]
        public void TestUndefined()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            writer.WriteBytes(new Harmonic.Networking.Amf.Common.Undefined());
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetUndefined(buffer, out var readVal, out var consumed));
            Assert.IsNotNull(readVal);
            Assert.AreEqual(buffer.Length, consumed);

        }

        [TestMethod]
        public void TestArray()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            var arr = new Amf3Array();

            arr["a"] = (uint)1;
            arr["b"] = 2.1;
            arr["d"] = null;

            arr.DensePart.Add(1);
            arr.DensePart.Add(1.2);

            writer.WriteBytes(arr);

            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetArray(buffer, out var readVal, out var consumed));
            Assert.AreEqual(arr["a"], readVal["a"]);
            Assert.AreEqual(arr["b"], readVal["b"]);
            Assert.AreEqual(arr["d"], readVal["d"]);
            Assert.AreEqual(1.0, readVal[0]);
            Assert.AreEqual(buffer.Length, consumed);
        }

        [TestMethod]
        public void TestByteArray()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            var arr = new byte[] { 1, 2, 3 };
            writer.WriteBytes(arr);

            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetByteArray(buffer, out var readVal, out var consumed));
            Assert.IsTrue(arr.SequenceEqual(readVal));
            Assert.AreEqual(buffer.Length, consumed);

        }

        [TestMethod]
        public void TestDate()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            var date = DateTime.Now;
            writer.WriteBytes(date);

            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetDate(buffer, out var readVal, out var consumed));
            Assert.AreEqual(date.Year, readVal.Year);
            Assert.AreEqual(date.Month, readVal.Month);
            Assert.AreEqual(date.Day, readVal.Day);
            Assert.AreEqual(date.Hour, readVal.Hour);
            Assert.AreEqual(date.Minute, readVal.Minute);
            Assert.AreEqual(date.Second, readVal.Second);
            Assert.AreEqual(date.Millisecond, readVal.Millisecond);
        }

        [TestMethod]
        public void TestDictionary()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            var dict = new Amf3Dictionary<object, object>();
            dict.Add("ss", 1.0);
            dict.Add("sd", new Vector<int>() { 1, 2 });
            dict.Add(new Vector<int>() { 1, 2 }, "sd");

            writer.WriteBytes(dict);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetDictionary(buffer, out var readVal, out var consumed));
            Assert.AreEqual(dict["ss"], readVal["ss"]);
            Assert.AreEqual(dict["sd"], readVal["sd"]);
            Assert.AreEqual(dict[new Vector<int>() { 1, 2 }], readVal[new Vector<int>() { 1, 2 }]);
            Assert.AreEqual(buffer.Length, consumed);
        }

        [TestMethod]
        public void TestIExternalizable()
        {
            var reader = new Amf3Reader();
            var writer = new Amf3Writer();

            reader.RegisterExternalizable<iexternalizable>();

            var ext = new iexternalizable()
            {
                v1 = 0.1,
                v2 = 1
            };

            writer.WriteBytes(ext);
            var buffer = new byte[writer.MessageLength];
            writer.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetObject(buffer, out var readVal, out var consumed));
            var val = (iexternalizable)readVal;

            Assert.AreEqual(val.v1, ext.v1);
            Assert.AreEqual(val.v2, ext.v2);
            Assert.AreEqual(buffer.Length, consumed);


        }

    }
}
