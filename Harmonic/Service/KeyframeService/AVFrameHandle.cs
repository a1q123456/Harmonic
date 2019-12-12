using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Service.KeyframeService
{
    public sealed unsafe class AVFrameHandle: IDisposable
    {
        private readonly AVFrame* _pFrame = null;

        public AVFrameHandle()
        {
            _pFrame = ffmpeg.av_frame_alloc();
        }

        internal AVFrame* Frame
        {
            get
            {
                return _pFrame;
            }
        }

        public void Dispose()
        {
            fixed (AVFrame** ppFrame = &_pFrame)
            {
                ffmpeg.av_frame_free(ppFrame);
            }
        }

    }
}
