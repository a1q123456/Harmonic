using FFmpeg.AutoGen;
using Harmonic;
using Harmonic.Networking.Flv.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Service.KeyframeService.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Harmonic.Service.KeyframeService
{
    public sealed unsafe class VideoDecoder
    {
        private readonly AVCodecContext* _pDecoderCodecContext = null;
        private readonly AVCodecContext* _pEncoderCodecContext = null;
        private readonly FFmpeg.AutoGen.AVPacket* _pPacket = null;
        private int _canGenerateKeyFrame = 0;

        public string CodecName { get; }
        public Size FrameSize { get; }

        private static AVCodecID GetCodecId(Dictionary<string, object> metaData)
        {
            var codecId = (CodecId)(double)metaData["videocodecid"];
            switch (codecId)
            {
                case CodecId.Avc:
                    return AVCodecID.AV_CODEC_ID_H264;
                case CodecId.H263:
                    return AVCodecID.AV_CODEC_ID_H263;
                case CodecId.Vp6:
                    return AVCodecID.AV_CODEC_ID_VP6;
                case CodecId.Vp6WithAlpha:
                    return AVCodecID.AV_CODEC_ID_VP6A;
                case CodecId.ScreenVideo:
                case CodecId.ScreenVideo2:
                    throw new NotSupportedException(Resource.flv_screen_video_is_not_supported);
            }
            throw new NotSupportedException(Resource.not_supported_codec);
        }

        public VideoDecoder(
            DataMessage setMetaData,
            ReadOnlySpan<byte> avcDecoderConfig,
        AVHWDeviceType hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL)
        {
            ffmpeg.RootPath = @"D:\Harmonic\Harmonic\bin\Debug\netcoreapp3.0\";
            Console.WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");

            if (setMetaData == null)
            {
                throw new ArgumentNullException(nameof(setMetaData));
            }
            if (setMetaData.Data.Count < 3)
            {
                throw new ArgumentException(Resource.not_valid, nameof(setMetaData));
            }
            var metaData = setMetaData.Data[2] as Dictionary<string, object>;

            var codecId = GetCodecId(metaData);
            var bitRate = (int)(double)metaData["videodatarate"];
            var width = (int)(double)metaData["width"];
            var height = (int)(double)metaData["height"];
            var framerate = new AVRational { num = 1, den = (int)(double)metaData["framerate"] };
            var profile = metaData.ContainsKey("avcprofile") ? (int)(double)metaData["avcprofile"] : ffmpeg.FF_PROFILE_UNKNOWN;

            var decoderCodec = ffmpeg.avcodec_find_decoder(codecId);

            var encoderCodec = ffmpeg.avcodec_find_encoder(codecId);

            if (decoderCodec->type != AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                throw new ArgumentException(Resource.codecId_must_be_a_video_codecId);
            }

            _pDecoderCodecContext = ffmpeg.avcodec_alloc_context3(decoderCodec);
            _pDecoderCodecContext->width = width;
            _pDecoderCodecContext->height = height;
            _pDecoderCodecContext->time_base = framerate;
            _pDecoderCodecContext->bit_rate = bitRate;
            _pDecoderCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            // _pDecoderCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            _pDecoderCodecContext->extradata_size = avcDecoderConfig.Length;
            _pDecoderCodecContext->extradata = (byte*)ffmpeg.av_malloc((ulong)avcDecoderConfig.Length);
            fixed (byte* pData = &MemoryMarshal.GetReference(avcDecoderConfig))
            {
                Buffer.MemoryCopy(pData, _pDecoderCodecContext->extradata, avcDecoderConfig.Length, avcDecoderConfig.Length);
            }
            ErrorCode.ThrowIfFailed(ffmpeg.avcodec_open2(_pDecoderCodecContext, decoderCodec, null));

            _pEncoderCodecContext = ffmpeg.avcodec_alloc_context3(encoderCodec);
            _pEncoderCodecContext->width = width;
            _pEncoderCodecContext->height = height;
            _pEncoderCodecContext->time_base = framerate;
            _pEncoderCodecContext->bit_rate = bitRate;
            _pEncoderCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            _pEncoderCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P; // TODO
            _pEncoderCodecContext->keyint_min = 1;
            _pEncoderCodecContext->max_b_frames = 0;

            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "x264opts", "annexb=0", 0);

            ErrorCode.ThrowIfFailed(ffmpeg.avcodec_open2(_pEncoderCodecContext, encoderCodec, &options));

            ffmpeg.av_dict_free(&options);

            CodecName = ffmpeg.avcodec_get_name(encoderCodec->id);
            FrameSize = new Size(_pDecoderCodecContext->width, _pDecoderCodecContext->height);

            _pPacket = ffmpeg.av_packet_alloc();
        }

        public VideoMessage GenerateKeyFrame()
        {
            var packet = ffmpeg.av_packet_alloc();
            if (packet == null)
            {
                ErrorCode.ThrowIfFailed(ffmpeg.AVERROR(ffmpeg.ENOMEM));
            }
            try
            {
                ErrorCode.ThrowIfFailed(ffmpeg.avcodec_receive_packet(_pEncoderCodecContext, packet));

                var data = new byte[packet->size + 1];
                fixed (byte* pData = data)
                {
                    pData[0] = (byte)FrameType.GeneratedKeyFrame << 4 | (byte)CodecId.Avc;
                    Buffer.MemoryCopy(packet->data, pData + 1, packet->size, packet->size);
                }

                return VideoMessage.RefFromMemory(data);
            }
            finally
            {
                ffmpeg.av_packet_unref(packet);
                ffmpeg.av_packet_free(&packet);
            }
        }

        public void SendFrame(ReadOnlySpan<byte> data)
        {
            fixed (byte* pbData = &MemoryMarshal.GetReference(data))
            {
                try
                {
                    ErrorCode.ThrowIfFailed(ffmpeg.av_new_packet(_pPacket, data.Length));
                    Buffer.MemoryCopy(pbData, _pPacket->data, _pPacket->size, data.Length);

                    var error = ffmpeg.avcodec_send_packet(_pDecoderCodecContext, _pPacket);
                    if (error != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        var frame = ffmpeg.av_frame_alloc();
                        try
                        {
                            if (ffmpeg.avcodec_receive_frame(_pDecoderCodecContext, frame) >= 0)
                            {
                                error = ffmpeg.avcodec_send_frame(_pEncoderCodecContext, frame);
                                if (error != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                                {
                                    ErrorCode.ThrowIfFailed(error);
                                }

                                Interlocked.Exchange(ref _canGenerateKeyFrame, 1);
                            }
                        }
                        finally
                        {
                            ffmpeg.av_frame_unref(frame);
                            ffmpeg.av_frame_free(&frame);
                        }

                    }
                }
                finally
                {
                    ffmpeg.av_packet_unref(_pPacket);
                }
            }
        }

        ~VideoDecoder()
        {
            if (_pPacket != null)
            {
                ffmpeg.av_packet_unref(_pPacket);
                ffmpeg.av_free(_pPacket);
            }
            if (_pDecoderCodecContext != null)
            {
                if (_pDecoderCodecContext->extradata != null)
                {
                    ffmpeg.av_free(_pDecoderCodecContext->extradata);
                }
                ffmpeg.avcodec_close(_pDecoderCodecContext);
            }


        }
    }
}
