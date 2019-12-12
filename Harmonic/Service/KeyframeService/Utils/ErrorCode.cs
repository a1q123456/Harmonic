using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonic.Service.KeyframeService.Utils
{
    internal static class ErrorCode
    {
        public static int ThrowExceptionIfError(this int error)
        {
            return ThrowIfFailed(error);
        }

        public static unsafe string av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }

        public static int ThrowIfFailed(int error)
        {
            if (error < 0) throw new ApplicationException(av_strerror(error));
            return error;
        }
    }
}
