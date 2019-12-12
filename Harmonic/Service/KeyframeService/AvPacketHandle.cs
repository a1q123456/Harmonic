using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonic.Service.KeyframeService
{
    public sealed unsafe class AVPacketHandle
    {
        private AVPacket* _pPacket = null;

        internal AVPacket* Packet
        {
            get
            {
                return _pPacket;
            }
        }

        public ReadOnlySpan<byte> Data
        {
            get
            {
                return MemoryMarshal.CreateReadOnlySpan(ref *_pPacket->data, _pPacket->size);
            }
        }

        ~AVPacketHandle()
        {
            fixed (AVPacket** ppPacket = &_pPacket)
            {
                ffmpeg.av_packet_free(ppPacket);
            }
        }
    }
}
