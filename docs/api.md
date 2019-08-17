
# NetConnection
## IReadOnlyDictionary<uint, RtmpController> NetStreams { get; }
access all NetStreams that is managed by this NetConnection

# RtmpSession
## RtmpControlMessageStream ControlMessageStream { get; };
get the ControlMessageStream, it will be useful when you intend to send a controll message.

## NetConnection NetConnection { get; }
get the NetConnection instance.

## ConnectionInformation ConnectionInformation { get; }
get the connection information, the data that was sent when peer call the `connect` command.

## T CreateNetStream<T>() where T: NetStream
create a netstream(usally a sub controller), then you can send message on it

## void DeleteNetStream(uint id)
destroy a message stream

## T CreateCommandMessage<T>() where T: CommandMessage
create a command message, it will create a command message using the amf encoding the `ConnectionInformation` provided.

## T CreateData<T>() where T : DataMessage
same as CreateCommandMessage

## Close()
close connection

## RtmpChunkStream CreateChunkStream()
when you want to send messages independently, you can create a chunkstream, different chunk stream can send message concurrently and using independent timestamp.

## Task SendControlMessageAsync(Message message)
send a control message using RtmpControlMessageStream and ControlChunkStream







