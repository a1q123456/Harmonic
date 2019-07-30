using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Harmonic.NetWorking
{
    static class StreamHelper
    {
        public static byte[] ReadBytes(this Stream stream, int count)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var result = new byte[count];
            var bytesRead = 0;
            while (count > 0)
            {
                var n = stream.Read(result, bytesRead, count);
                if (n == 0)
                    break;
                bytesRead += n;
                count -= n;
            }

            if (bytesRead != result.Length)
            {
                throw new EndOfStreamException();
            }

            return result;
        }

        public static async Task ReadBytesAsync(this Stream stream, Memory<byte> buffer, CancellationToken ct = default)
        {
            int count = buffer.Length;
            var offset = 0;
            while (count != 0)
            {
                var n = await stream.ReadAsync(buffer.Slice(offset, count), ct);
                ct.ThrowIfCancellationRequested();
                if (n == 0)
                {
                    break;
                }
                offset += n;
                count -= n;
            }

            if (offset != buffer.Length)
            {
                throw new EndOfStreamException();
            }
        }
    }
}
