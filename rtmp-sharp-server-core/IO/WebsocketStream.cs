using Fleck;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RtmpSharp.IO
{
    class WebsocketStream : Stream
    {
        public override bool CanRead { get; } = true;

        public override bool CanSeek { get; } = false;

        public override bool CanWrite { get; } = true;

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }

            set
            {
                throw new NotSupportedException();
            }
        }
        public bool NoDelay { get; set; } = true;

        private MemoryStream writeBuffer = new MemoryStream();
        private MemoryStream readBuffer = new MemoryStream();
        public delegate Task SendBytesDelegate(byte[] data);
        public delegate byte[] OnBinaryDelegate();
        private SendBytesDelegate sendBytes;

        public bool DataAvailable {
            get
            {
                return readBuffer.Position < readBuffer.Length;
            }
        }

        public WebsocketStream(IWebSocketConnection connection)
        {
            sendBytes = connection.Send;
            readBuffer.Capacity = 4096 * 1024;
            connection.OnBinary += b => readBuffer.Write(b, 0, b.Length);
        }

        public override void Flush()
        {
            sendBytes(writeBuffer.ToArray());
            writeBuffer.SetLength(0);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ret = readBuffer.Read(buffer, offset, count);
            readBuffer.SetLength(readBuffer.Length - count);
            
            return ret;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            writeBuffer.Write(buffer, offset, count);
            // throw new NotImplementedException();
            if (NoDelay) Flush();
        }
    }
}
