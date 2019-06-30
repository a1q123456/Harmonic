using Harmonic.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;
using System.Linq;

namespace UnitTest
{
    [TestClass]
    public class TestUnlimitedBuffer
    {
        [TestMethod]
        public void TestBufferSegmentSize()
        {
            var random = new Random();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new UnlimitedBuffer(0));

            for (int i = 0; i < 100000; i++)
            {
                var size = random.Next(1, 3000);
                var len1 = random.Next(0, 3000);
                var len2 = random.Next(0, 3000);
                var buffer = new UnlimitedBuffer(size);

                var bytes1 = new byte[len1];
                var bytes2 = new byte[len2];
                random.NextBytes(bytes1);
                random.NextBytes(bytes2);
                buffer.WriteToBuffer(bytes1);
                buffer.WriteToBuffer(bytes2);

                var length = buffer.BufferLength;

                Assert.AreEqual(length, len1 + len2);

                var outBuffer = ArrayPool<byte>.Shared.Rent(length);
                buffer.TakeOutMemory(outBuffer);
                Assert.IsTrue(outBuffer.AsSpan(0, len1).SequenceEqual(bytes1));
                Assert.IsTrue(outBuffer.AsSpan(len1, len2).SequenceEqual(bytes2));
            }
        }

        [TestMethod]
        public void TestBufferLengthDifferentBufferSegmentSize()
        {
            var random = new Random();

            for (int i = 0; i < 1000; i++)
            {
                var len1 = random.Next(0, 3000);
                var len2 = random.Next(0, 3000);
                var bytes1 = new byte[len1];
                var bytes2 = new byte[len2];

                random.NextBytes(bytes1);
                random.NextBytes(bytes2);

                var buffer = new UnlimitedBuffer(random.Next(10, 3000));
                buffer.WriteToBuffer(bytes1);
                buffer.WriteToBuffer(bytes2);
                Assert.AreEqual(len1 + len2, buffer.BufferLength);
            }
        }

        [TestMethod]
        public void TestBufferLength()
        {
            var random = new Random();

            for (int i = 0; i < 1000; i++)
            {
                var len1 = random.Next(0, 3000);
                var len2 = random.Next(0, 3000);
                var bytes1 = new byte[len1];
                var bytes2 = new byte[len2];

                random.NextBytes(bytes1);
                random.NextBytes(bytes2);

                var buffer = new UnlimitedBuffer();
                buffer.WriteToBuffer(bytes1);
                buffer.WriteToBuffer(bytes2);
                Assert.AreEqual(len1 + len2, buffer.BufferLength);
            }
        }

        [TestMethod]
        public void TestWriteToBuffer4DifferentBufferSegmentSize()
        {
            for (int i = 0; i < 10000; i++)
            {
                var random = new Random();
                var buffer = new UnlimitedBuffer(random.Next(1, 3000));
                var bytes1 = new byte[1024];
                var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
                random.NextBytes(bytes1);
                buffer.WriteToBuffer(bytes1);
                buffer.WriteToBuffer(data);

                var length = buffer.BufferLength;
                var outBuffer = ArrayPool<byte>.Shared.Rent(length);
                buffer.TakeOutMemory(outBuffer);
                Assert.IsTrue(outBuffer.AsSpan(0, 1024).SequenceEqual(bytes1));
                Assert.AreEqual(outBuffer[1024], data);
            }
        }

        [TestMethod]
        public void TestWriteToBuffer3DifferentBufferSegmentSize()
        {
            for (int i = 0; i < 10000; i++)
            {
                var random = new Random();
                var buffer = new UnlimitedBuffer(random.Next(1, 3000));
                var bytes1 = new byte[3];
                var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
                random.NextBytes(bytes1);
                buffer.WriteToBuffer(bytes1);
                buffer.WriteToBuffer(data);

                var length = buffer.BufferLength;
                var outBuffer = ArrayPool<byte>.Shared.Rent(length);
                buffer.TakeOutMemory(outBuffer);
                Assert.IsTrue(outBuffer.AsSpan(0, 3).SequenceEqual(bytes1));
                Assert.AreEqual(outBuffer[3], data);
            }
        }

        [TestMethod]
        public void TestWriteToBuffer4()
        {
            var buffer = new UnlimitedBuffer();
            var random = new Random();
            var bytes1 = new byte[1024];
            var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
            random.NextBytes(bytes1);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(data);

            var length = buffer.BufferLength;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            Assert.IsTrue(outBuffer.AsSpan(0, 1024).SequenceEqual(bytes1));
            Assert.AreEqual(outBuffer[1024], data);
        }

        [TestMethod]
        public void TestWriteToBuffer3()
        {
            var buffer = new UnlimitedBuffer();
            var random = new Random();
            var bytes1 = new byte[3];
            var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
            random.NextBytes(bytes1);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(data);

            var length = buffer.BufferLength;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            Assert.IsTrue(outBuffer.AsSpan(0, 3).SequenceEqual(bytes1));
            Assert.AreEqual(outBuffer[3], data);
        }

        [TestMethod]
        public void TestWriteToBuffer1DifferentBufferSegmentSize()
        {
            for (int i = 0; i < 10000; i++)
            {
                var random = new Random();
                var buffer = new UnlimitedBuffer(random.Next(1, 3000));
                var bytes1 = new byte[3];
                var bytes2 = new byte[7];
                random.NextBytes(bytes1);
                random.NextBytes(bytes2);
                buffer.WriteToBuffer(bytes1);
                buffer.WriteToBuffer(bytes2);

                var length = buffer.BufferLength;
                var outBuffer = ArrayPool<byte>.Shared.Rent(length);
                buffer.TakeOutMemory(outBuffer);
                Assert.IsTrue(outBuffer.AsSpan(0, 3).SequenceEqual(bytes1));
                Assert.IsTrue(outBuffer.AsSpan(3, 7).SequenceEqual(bytes2));
            }
        }

        [TestMethod]
        public void TestWriteToBuffer1()
        {
            var buffer = new UnlimitedBuffer();
            var random = new Random();
            var bytes1 = new byte[3];
            var bytes2 = new byte[7];
            random.NextBytes(bytes1);
            random.NextBytes(bytes2);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(bytes2);

            var length = buffer.BufferLength;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            Assert.IsTrue(outBuffer.AsSpan(0, 3).SequenceEqual(bytes1));
            Assert.IsTrue(outBuffer.AsSpan(3, 7).SequenceEqual(bytes2));
        }

        [TestMethod]
        public void TestWriteToBuffer2()
        {
            var buffer = new UnlimitedBuffer();
            var random = new Random();
            var bytes1 = new byte[4000];
            var bytes2 = new byte[3001];
            random.NextBytes(bytes1);
            random.NextBytes(bytes2);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(bytes2);

            var length = buffer.BufferLength;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            var seq = outBuffer.AsSpan(0, 4000);
            var seq2 = outBuffer.AsSpan(4000, 3001).ToArray();
            Assert.IsTrue(seq.SequenceEqual(bytes1));
            Assert.IsTrue(seq2.SequenceEqual(bytes2));
            outBuffer.AsSpan().Clear();
            buffer.TakeOutMemory(outBuffer);
            Assert.IsFalse(outBuffer.Any(b => b != 0));
        }

        [TestMethod]
        public void TestClearAndCopyToDifferentBufferSegmentSize()
        {
            var random = new Random();
            var buffer = new UnlimitedBuffer(random.Next(1, 3000));
            var bytes1 = new byte[4000];
            random.NextBytes(bytes1);
            buffer.WriteToBuffer(bytes1);

            var length = buffer.BufferLength;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            outBuffer.AsSpan().Clear();
            buffer.TakeOutMemory(outBuffer);
            Assert.IsFalse(outBuffer.Any(b => b != 0));
        }

        [TestMethod]
        public void TestClearAndCopyTo()
        {
            var buffer = new UnlimitedBuffer();
            var random = new Random();
            var bytes1 = new byte[4000];
            random.NextBytes(bytes1);
            buffer.WriteToBuffer(bytes1);

            var length = buffer.BufferLength;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            outBuffer.AsSpan().Clear();
            buffer.TakeOutMemory(outBuffer);
            Assert.IsFalse(outBuffer.Any(b => b != 0));
        }
    }
}
