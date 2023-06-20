using Harmonic.Networking.Flv;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Amf.Serialization.Amf0;
using Harmonic.Networking.Amf.Serialization.Amf3;
using Harmonic.Networking.Rtmp;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Messages;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Messages.UserControlMessages;
using Harmonic.Networking.Rtmp.Streaming;
using Harmonic.Networking.Utils;
using Harmonic.Rpc;
using Harmonic.Service;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Harmonic.Networking.Flv.Data;

namespace Harmonic.Controllers.Record;

public class RecordStream : NetStream
{
    private PublishingType _publishingType;
    private FileStream _recordFile = null;
    private FileStream _recordFileData = null;
    private RecordService _recordService = null;
    private DataMessage _metaData = null;
    private uint _currentTimestamp = 0;
    private SemaphoreSlim _playLock = new SemaphoreSlim(1);
    private int _playing = 0;
    private AmfObject _keyframes = null;
    private List<object> _keyframeTimes;
    private List<object> _keyframeFilePositions;
    private long _bufferMs = -1;

    private RtmpChunkStream VideoChunkStream { get; set; } = null;
    private RtmpChunkStream AudioChunkStream { get; set; } = null;
    private bool _disposed = false;
    private CancellationTokenSource _playCts;

    protected override async void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!_disposed)
        {
            _disposed = true;
            if (_recordFileData != null)
            {
                try
                {
                    var filePath = _recordFileData.Name;
                    using (var recordFile = new FileStream(filePath.Substring(0, filePath.Length - 5) + ".flv", FileMode.OpenOrCreate))
                    {
                        recordFile.SetLength(0);
                        recordFile.Seek(0, SeekOrigin.Begin);
                        await recordFile.WriteAsync(FlvMuxer.MultiplexFlvHeader(true, true));
                        var metaData = _metaData.Data[1] as Dictionary<string, object>;
                        metaData["duration"] = ((double)_currentTimestamp) / 1000;
                        metaData["keyframes"] = _keyframes;
                        _metaData.MessageHeader.MessageLength = 0;
                        var dataTagLen = FlvMuxer.MultiplexFlv(_metaData).Length;

                        var offset = recordFile.Position + dataTagLen;
                        for (int i = 0; i < _keyframeFilePositions.Count; i++)
                        {
                            _keyframeFilePositions[i] = (double)_keyframeFilePositions[i] + offset;
                        }

                        await recordFile.WriteAsync(FlvMuxer.MultiplexFlv(_metaData));
                        _recordFileData.Seek(0, SeekOrigin.Begin);
                        await _recordFileData.CopyToAsync(recordFile);
                        _recordFileData.Dispose();
                        File.Delete(filePath);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            _recordFile?.Dispose();
        }
    }

    public RecordStream(RecordService recordService)
    {
        _recordService = recordService;
    }


    [RpcMethod(Name = "publish")]
    public async Task Publish([FromOptionalArgument] string streamName, [FromOptionalArgument] string publishingType)
    {
        if (string.IsNullOrEmpty(streamName))
        {
            throw new InvalidOperationException("empty publishing name");
        }
        if (!PublishingHelpers.IsTypeSupported(publishingType))
        {
            throw new InvalidOperationException($"not supported publishing type {publishingType}");
        }

        _publishingType = PublishingHelpers.PublishingTypes[publishingType];

        await RtmpSession.SendControlMessageAsync(new StreamIsRecordedMessage() { StreamID = MessageStream.MessageStreamId });
        await RtmpSession.SendControlMessageAsync(new StreamBeginMessage() { StreamID = MessageStream.MessageStreamId });
        var onStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        MessageStream.RegisterMessageHandler<DataMessage>(HandleData);
        MessageStream.RegisterMessageHandler<AudioMessage>(HandleAudioMessage);
        MessageStream.RegisterMessageHandler<VideoMessage>(HandleVideoMessage);
        MessageStream.RegisterMessageHandler<UserControlMessage>(HandleUserControlMessage);
        onStatus.InfoObject = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Publish.Start" },
            {"description", "Stream is now published." },
            {"details", streamName }
        };
        await MessageStream.SendMessageAsync(ChunkStream, onStatus);

        _recordFileData = new FileStream(_recordService.GetRecordFilename(streamName) + ".data", FileMode.OpenOrCreate);
        _recordFileData.SetLength(0);
        _keyframes = new AmfObject();
        _keyframeTimes = new List<object>();
        _keyframeFilePositions = new List<object>();
        _keyframes.Add("times", _keyframeTimes);
        _keyframes.Add("filepositions", _keyframeFilePositions);
    }

    private void HandleUserControlMessage(UserControlMessage msg)
    {
        if (msg.UserControlEventType == UserControlEventType.SetBufferLength)
        {
            _bufferMs = (msg as SetBufferLengthMessage).BufferMilliseconds;
        }
    }

    private async void HandleAudioMessage(AudioMessage message)
    {
        try
        {
            _currentTimestamp = Math.Max(_currentTimestamp, message.MessageHeader.Timestamp);

            await SaveMessage(message);
        }
        catch
        {
            RtmpSession.Close();
        }
    }

    private async void HandleVideoMessage(VideoMessage message)
    {
        try
        {
            _currentTimestamp = Math.Max(_currentTimestamp, message.MessageHeader.Timestamp);

            var head = message.Data.Span[0];

            var data = FlvDemuxer.DemultiplexVideoData(message);
            if (data.FrameType == FrameType.KeyFrame)
            {
                _keyframeTimes.Add((double)message.MessageHeader.Timestamp / 1000);
                _keyframeFilePositions.Add((double)_recordFileData.Position);
            }

            await SaveMessage(message);
        }
        catch
        {
            RtmpSession.Close();
        }
    }

    private void HandleData(DataMessage message)
    {
        try
        {
            _metaData = message;
            _metaData.Data.RemoveAt(0);
        }
        catch
        {
            RtmpSession.Close();
        }
    }

    [RpcMethod("seek")]
    public async Task Seek([FromOptionalArgument] double milliSeconds)
    {
        var resetData = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Seek.Notify" },
            {"description", "Seeking stream." },
            {"details", "seek" }
        };
        var resetStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        resetStatus.InfoObject = resetData;
        await MessageStream.SendMessageAsync(ChunkStream, resetStatus);

        _playCts?.Cancel();
        while (_playing == 1)
        {
            await Task.Yield();
        }

        var cts = new CancellationTokenSource();
        _playCts?.Dispose();
        _playCts = cts;
        await SeekAndPlay(milliSeconds, cts.Token);
    }

    [RpcMethod("play")]
    public async Task Play(
        [FromOptionalArgument] string streamName,
        [FromOptionalArgument] double start = -1,
        [FromOptionalArgument] double duration = -1,
        [FromOptionalArgument] bool reset = false)
    {
        _recordFile = new FileStream(_recordService.GetRecordFilename(streamName) + ".flv", FileMode.Open, FileAccess.Read);
        await FlvDemuxer.AttachStream(_recordFile);

        var resetData = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Play.Reset" },
            {"description", "Resetting and playing stream." },
            {"details", streamName }
        };
        var resetStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        resetStatus.InfoObject = resetData;
        await MessageStream.SendMessageAsync(ChunkStream, resetStatus);

        var startData = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Play.Start" },
            {"description", "Started playing." },
            {"details", streamName }
        };

        var startStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        startStatus.InfoObject = startData;
        await MessageStream.SendMessageAsync(ChunkStream, startStatus);
        var bandwidthLimit = new WindowAcknowledgementSizeMessage()
        {
            WindowSize = 500 * 1024
        };
        await RtmpSession.ControlMessageStream.SendMessageAsync(RtmpSession.ControlChunkStream, bandwidthLimit);
        VideoChunkStream = RtmpSession.CreateChunkStream();
        AudioChunkStream = RtmpSession.CreateChunkStream();

        var cts = new CancellationTokenSource();
        _playCts?.Dispose();
        _playCts = cts;
        start = Math.Max(start, 0);
        await SeekAndPlay(start / 1000, cts.Token);
    }

    [RpcMethod("pause")]
    public async Task Pause([FromOptionalArgument] bool isPause, [FromOptionalArgument] double milliseconds)
    {
        if (isPause)
        {
            _playCts?.Cancel();
            while (_playing == 1)
            {
                await Task.Yield();
            }
        }
        else
        {
            var cts = new CancellationTokenSource();
            _playCts?.Dispose();
            _playCts = cts;
            await SeekAndPlay(milliseconds, cts.Token);
        }
    }

    private async Task StartPlayNoLock(CancellationToken ct)
    {
        while (_recordFile.Position < _recordFile.Length && !ct.IsCancellationRequested)
        {
            while (_bufferMs != -1 && _currentTimestamp >= _bufferMs)
            {
                await Task.Yield();
            }

            await PlayRecordFileNoLock(ct);
        }
    }

    private Task<Message> ReadMessage(CancellationToken ct)
    {
        return FlvDemuxer.DemultiplexFlvAsync(ct);
    }

    private async Task SeekAndPlay(double milliSeconds, CancellationToken ct)
    {
        await _playLock.WaitAsync();
        Interlocked.Exchange(ref _playing, 1);
        try
        {

            _recordFile.Seek(9, SeekOrigin.Begin);
            FlvDemuxer.SeekNoLock(milliSeconds, _metaData == null ? null : _metaData.Data[2] as Dictionary<string, object>, ct);
            await StartPlayNoLock(ct);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        finally
        {
            Interlocked.Exchange(ref _playing, 0);
            _playLock.Release();
        }
    }

    private async Task PlayRecordFileNoLock(CancellationToken ct)
    {
        var message = await ReadMessage(ct);
        if (message is AudioMessage)
        {
            await MessageStream.SendMessageAsync(AudioChunkStream, message);
        }
        else if (message is VideoMessage)
        {
            await MessageStream.SendMessageAsync(VideoChunkStream, message);
        }
        else if (message is DataMessage data)
        {
            data.Data.Insert(0, "@setDataFrame");
            _metaData = data;
            await MessageStream.SendMessageAsync(ChunkStream, data);
        }
        _currentTimestamp = Math.Max(_currentTimestamp, message.MessageHeader.Timestamp);
    }

    private async Task SaveMessage(Message message)
    {
        await _recordFileData.WriteAsync(FlvMuxer.MultiplexFlv(message));
    }
}