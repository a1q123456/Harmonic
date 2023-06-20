namespace Harmonic.Networking.Rtmp.Data;

public enum ChunkHeaderType : byte
{
    // Timestampe + Message Length + Message Type Id + Message Stream Id +
    Type0 = 0,
    // Timestamp Delta + Message Length + Message Type Id
    Type1 = 1,
    // Timestamp Delta
    Type2 = 2,
    // Nothing
    Type3 = 3
}