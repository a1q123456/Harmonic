using Harmonic.NetWorking.Amf.Attributes;
using Harmonic.NetWorking.Amf.Data;
using Harmonic.NetWorking.Amf.Serialization.Amf0;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnitTest
{
    [TestClass]
    public class TestAmf0Reader
    {
        [TestMethod]
        public void TestReadNumber()
        {
            var reader = new Amf0Reader();

            var files = Directory.GetFiles("../../../../samples/amf0/number");

            foreach (var file in files)
            {
                var value = double.Parse(Path.GetFileNameWithoutExtension(file));
                using (var f = new FileStream(file, FileMode.Open))
                {
                    var data = new byte[f.Length];
                    f.Read(data);
                    Assert.IsTrue(reader.TryGetNumber(data, out var dataRead, out var consumed));
                    Assert.AreEqual(dataRead, value);
                    Assert.AreEqual(consumed, f.Length);
                }
            }
        }

        [TestMethod]
        public void TestReadString()
        {
            var reader = new Amf0Reader();

            var files = Directory.GetFiles("../../../../samples/amf0/string");

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
            var reader = new Amf0Reader();

            var files = Directory.GetFiles("../../../../samples/amf0/boolean");

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
        public void TestReadArray()
        {
            var reader = new Amf0Reader();


            using (var f = new FileStream("../../../../samples/amf0/misc/array.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);
                var arrayData = new List<object> { 1.0d, 2.0d, 3.0d, 4.0d, "a", "asdf", "eee" };
                Assert.IsTrue(reader.TryGetStrictArray(data, out var dataRead, out var consumed));
                Assert.IsTrue(arrayData.SequenceEqual(dataRead));
                Assert.AreEqual(consumed, data.Length);
            }
        }

        [TestMethod]
        public void TestReadDate()
        {
            var reader = new Amf0Reader();


            using (var f = new FileStream("../../../../samples/amf0/misc/date.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetDate(data, out var dataRead, out var consumed));
                Assert.AreEqual(dataRead.Year, 2019);
                Assert.AreEqual(dataRead.Month, 2);
                Assert.AreEqual(dataRead.Day, 11);
                Assert.AreEqual(consumed, data.Length);
            }
        }

        [TestMethod]
        public void TestReadLongString()
        {
            var reader = new Amf0Reader();


            using (var f = new FileStream("../../../../samples/amf0/misc/longstring.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetLongString(data, out var dataRead, out var consumed));
                Assert.AreEqual(string.Concat(Enumerable.Repeat("abc", 32767)), dataRead);
                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadNull()
        {
            var reader = new Amf0Reader();


            using (var f = new FileStream("../../../../samples/amf0/misc/null.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetNull(data, out var dataRead, out var consumed));
                Assert.AreEqual(null, dataRead);
                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadNull2()
        {
            var reader = new Amf0Reader();


            using (var f = new FileStream("../../../../samples/amf0/misc/null.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetObject(data, out var dataRead, out var consumed));
                Assert.AreEqual(null, dataRead);
                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadXml()
        {
            var reader = new Amf0Reader();


            using (var f = new FileStream("../../../../samples/amf0/misc/xml.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetXmlDocument(data, out var dataRead, out var consumed));
                Assert.AreNotEqual(dataRead.GetElementsByTagName("a").Count, 0);
                Assert.AreNotEqual(dataRead.GetElementsByTagName("b").Count, 0);
                Assert.IsNotNull(dataRead.GetElementsByTagName("b")[0].Attributes["value"], "1");
                Assert.AreEqual(consumed, f.Length);
            }
        }

        [TestMethod]
        public void TestReadUndefined()
        {
            var reader = new Amf0Reader();


            using (var f = new FileStream("../../../../samples/amf0/misc/undefined.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetUndefined(data, out var dataRead, out var consumed));
                Assert.AreEqual(consumed, f.Length);
            }
        }
        
        [TestMethod]
        public void TestReadEcmaArray()
        {
            var reader = new Amf0Reader();

            // pyamf has a bug about element count of ecma array
            // https://github.com/hydralabs/pyamf/blob/master/pyamf/amf0.py#L567
            reader.StrictMode = false;

            using (var f = new FileStream("../../../../samples/amf0/misc/ecmaarray.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetEcmaArray(data, out var dataRead, out var consumed));
                Assert.IsTrue(dataRead.SequenceEqual(new Dictionary<string, object>() { ["a"] = 1.0d, ["b"] = "a", ["c"] = "a" }));
                Assert.AreEqual(consumed, data.Length);
            }
        }

        [TestMethod]
        public void TestReadObject()
        {
            var reader = new Amf0Reader();

            // pyamf has a bug about element count of ecma array
            // https://github.com/hydralabs/pyamf/blob/master/pyamf/amf0.py#L567
            reader.StrictMode = false;

            using (var f = new FileStream("../../../../samples/amf0/misc/object.amf0", FileMode.Open))
            {
                var data = new byte[f.Length];
                f.Read(data);

                Assert.IsTrue(reader.TryGetObject(data, out var dataRead, out var consumed));
                Assert.IsTrue(dataRead.Fields.SequenceEqual(new Dictionary<string, object>() { ["a"] = "b", ["c"] = 1.0 }));
                Assert.AreEqual(consumed, data.Length);
            }
        }
        
        [TestMethod]
        public void TestPacket()
        {
            var reader = new Amf0Reader();
            reader.RegisterType<RemotingMessage>();
            reader.StrictMode = false;
            using (var file = new FileStream("../../../../samples/amf0/misc/packet.amf0", FileMode.Open))
            {
                var data = new byte[file.Length];
                file.Read(data);
                Assert.IsTrue(reader.TryGetPacket(data, out var headers, out var messages, out var consumed));
                Assert.AreEqual(consumed, file.Length);
            }
        }

    }
}
