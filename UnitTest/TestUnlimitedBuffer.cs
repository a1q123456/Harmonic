using Harmonic.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTest;

[TestClass]
public class TestUnlimitedBuffer
{
    [TestMethod]
    public void TestBufferSegmentSize()
    {
        var random = new Random();

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ByteBuffer(0));

        for (int i = 0; i < 10000; i++)
        {
            var size = random.Next(1, 500);
            var len1 = random.Next(0, 100);
            var len2 = random.Next(0, 200);
            var buffer = new ByteBuffer(size);

            var bytes1 = new byte[len1];
            var bytes2 = new byte[len2];
            random.NextBytes(bytes1);
            random.NextBytes(bytes2);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(bytes2);

            var length = buffer.Length;

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

            var buffer = new ByteBuffer(random.Next(10, 3000));
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(bytes2);
            Assert.AreEqual(len1 + len2, buffer.Length);
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

            var buffer = new ByteBuffer();
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(bytes2);
            Assert.AreEqual(len1 + len2, buffer.Length);
        }
    }

    [TestMethod]
    public void TestWriteToBuffer4DifferentBufferSegmentSize()
    {
        for (int i = 0; i < 10000; i++)
        {
            var random = new Random();
            var buffer = new ByteBuffer(random.Next(1, 3000));
            var bytes1 = new byte[1024];
            var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
            random.NextBytes(bytes1);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(data);

            var length = buffer.Length;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            Assert.IsTrue(outBuffer.AsSpan(0, 1024).SequenceEqual(bytes1));
            Assert.AreEqual(outBuffer[1024], data);
        }
    }

    [TestMethod]
    public void TestWriteToBuffer5DifferentBufferSegmentSize()
    {
        for (int i = 0; i < 10000; i++)
        {
            var random = new Random();
            var buffer = new ByteBuffer(512);
            var bytes1 = new byte[4096];
            var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
            random.NextBytes(bytes1);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(data);

            var length = buffer.Length;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            var test1 = new byte[2];
            var test2 = new byte[3];
            var test3 = new byte[512];
            var test4 = new byte[1024];
            buffer.TakeOutMemory(test1.AsSpan());
            buffer.TakeOutMemory(test2.AsSpan());
            buffer.TakeOutMemory(test3);
            buffer.TakeOutMemory(test4);
            buffer.TakeOutMemory(outBuffer);
            Assert.IsTrue(test1.AsSpan().SequenceEqual(bytes1.AsSpan(0, 2)));
            Assert.IsTrue(test2.AsSpan().SequenceEqual(bytes1.AsSpan(2, 3)));
            Assert.IsTrue(test3.AsSpan().SequenceEqual(bytes1.AsSpan(5, 512)));
            Assert.IsTrue(test4.AsSpan().SequenceEqual(bytes1.AsSpan(517, 1024)));
            Assert.IsTrue(outBuffer.AsSpan(0, 4096 - 5 - 512 - 1024).SequenceEqual(bytes1.AsSpan(517 + 1024)));
        }
    }

    [TestMethod]
    public void TestParalleWriteAndRead()
    {
        var buffer = new ByteBuffer(512, 35767);
        var th1 = new Thread(() =>
        {
            byte i = 0;
            while (true)
            {
                var arr = new byte[Random.Shared.Next(256, 512)];
                for (var j = 0; j < arr.Length; j++)
                {
                    arr[j] = i;
                    i++;

                    if (i > 100)
                    {
                        i = 0;
                    }
                }
                buffer.WriteToBuffer(arr);
            }
        });
        th1.IsBackground = true;
        th1.Start();

        var th2 = new Thread(() =>
        {
            while (true)
            {
                var arr = new byte[Random.Shared.Next(129, 136)];
                if (buffer.Length >= arr.Length)
                {
                    buffer.TakeOutMemory(arr);

                    for (int i = 1; i < arr.Length; i++)
                    {
                        Assert.IsTrue(arr[i] - arr[i - 1] == 1 || arr[i - 1] - arr[i] == 100);
                    }
                }
            }
        });
        th2.IsBackground = true;
        th2.Start();

        Thread.Sleep(TimeSpan.FromSeconds(30));

    }

    [TestMethod]
    public void TestAsyncWriteAndRead()
    {
        var buffer = new ByteBuffer(512, 35767);
        short c = 0;
        Func<Task> th1 = async () =>
        {
            byte i = 0;
            while (c < short.MaxValue)
            {
                var arr = new byte[new Random().Next(256, 512)];
                for (var j = 0; j < arr.Length; j++)
                {
                    arr[j] = i;
                    i++;

                    if (i > 100)
                    {
                        i = 0;
                    }
                }
                await buffer.WriteToBufferAsync(arr);
                c++;
            }
        };

        Func<Task> th2 = async () =>
        {
            while (c < short.MaxValue)
            {
                var arr = new byte[new Random().Next(129, 136)];
                if (buffer.Length >= arr.Length)
                {
                    await buffer.TakeOutMemoryAsync(arr);

                    for (int i = 1; i < arr.Length; i++)
                    {
                        Assert.IsTrue(arr[i] - arr[i - 1] == 1 || arr[i - 1] - arr[i] == 100);
                    }
                }
            }
        };

        var t = th1();
        th2();
        t.Wait();
    }

    [TestMethod]
    public void TestWriteToBuffer3DifferentBufferSegmentSize()
    {
        for (int i = 0; i < 10000; i++)
        {
            var random = new Random();
            var buffer = new ByteBuffer(random.Next(1, 3000));
            var bytes1 = new byte[3];
            var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
            random.NextBytes(bytes1);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(data);

            var length = buffer.Length;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            Assert.IsTrue(outBuffer.AsSpan(0, 3).SequenceEqual(bytes1));
            Assert.AreEqual(outBuffer[3], data);
        }
    }

    [TestMethod]
    public void TestWriteToBuffer4()
    {
        var buffer = new ByteBuffer();
        var random = new Random();
        var bytes1 = new byte[1024];
        var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
        random.NextBytes(bytes1);
        buffer.WriteToBuffer(bytes1);
        buffer.WriteToBuffer(data);

        var length = buffer.Length;
        var outBuffer = ArrayPool<byte>.Shared.Rent(length);
        buffer.TakeOutMemory(outBuffer);
        Assert.IsTrue(outBuffer.AsSpan(0, 1024).SequenceEqual(bytes1));
        Assert.AreEqual(outBuffer[1024], data);
    }

    [TestMethod]
    public void TestWriteToBuffer3()
    {
        var buffer = new ByteBuffer();
        var random = new Random();
        var bytes1 = new byte[3];
        var data = (byte)random.Next(byte.MinValue, byte.MaxValue);
        random.NextBytes(bytes1);
        buffer.WriteToBuffer(bytes1);
        buffer.WriteToBuffer(data);

        var length = buffer.Length;
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
            var buffer = new ByteBuffer(random.Next(1, 3000));
            var bytes1 = new byte[3];
            var bytes2 = new byte[7];
            random.NextBytes(bytes1);
            random.NextBytes(bytes2);
            buffer.WriteToBuffer(bytes1);
            buffer.WriteToBuffer(bytes2);

            var length = buffer.Length;
            var outBuffer = ArrayPool<byte>.Shared.Rent(length);
            buffer.TakeOutMemory(outBuffer);
            Assert.IsTrue(outBuffer.AsSpan(0, 3).SequenceEqual(bytes1));
            Assert.IsTrue(outBuffer.AsSpan(3, 7).SequenceEqual(bytes2));
        }
    }

    [TestMethod]
    public void TestWriteToBuffer1()
    {
        var buffer = new ByteBuffer();
        var random = new Random();
        var bytes1 = new byte[3];
        var bytes2 = new byte[7];
        random.NextBytes(bytes1);
        random.NextBytes(bytes2);
        buffer.WriteToBuffer(bytes1);
        buffer.WriteToBuffer(bytes2);

        var length = buffer.Length;
        var outBuffer = ArrayPool<byte>.Shared.Rent(length);
        buffer.TakeOutMemory(outBuffer);
        Assert.IsTrue(outBuffer.AsSpan(0, 3).SequenceEqual(bytes1));
        Assert.IsTrue(outBuffer.AsSpan(3, 7).SequenceEqual(bytes2));
    }

    [TestMethod]
    public void TestWriteToBuffer2()
    {
        var buffer = new ByteBuffer();
        var random = new Random();
        var bytes1 = new byte[4000];
        var bytes2 = new byte[3001];
        random.NextBytes(bytes1);
        random.NextBytes(bytes2);
        buffer.WriteToBuffer(bytes1);
        buffer.WriteToBuffer(bytes2);

        var length = buffer.Length;
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
        var buffer = new ByteBuffer(random.Next(1, 3000));
        var bytes1 = new byte[4000];
        random.NextBytes(bytes1);
        buffer.WriteToBuffer(bytes1);

        var length = buffer.Length;
        var outBuffer = ArrayPool<byte>.Shared.Rent(length);
        buffer.TakeOutMemory(outBuffer);
        outBuffer.AsSpan().Clear();
        buffer.TakeOutMemory(outBuffer);
        Assert.IsFalse(outBuffer.Any(b => b != 0));
    }

    [TestMethod]
    public void TestClearAndCopyTo()
    {
        var buffer = new ByteBuffer();
        var random = new Random();
        var bytes1 = new byte[4000];
        random.NextBytes(bytes1);
        buffer.WriteToBuffer(bytes1);

        var length = buffer.Length;
        var outBuffer = ArrayPool<byte>.Shared.Rent(length);
        buffer.TakeOutMemory(outBuffer);
        outBuffer.AsSpan().Clear();
        buffer.TakeOutMemory(outBuffer);
        Assert.IsFalse(outBuffer.Any(b => b != 0));
    }
}