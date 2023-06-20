using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonic.Networking.Utils;

public static class NetworkBitConverter
{
    private static MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;

    public static int ToInt32(Span<byte> buffer, bool littleEndian = false)
    {
        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(int)).Reverse();
        }
        return BitConverter.ToInt32(buffer);
    }

    public static uint ToUInt32(Span<byte> buffer, bool littleEndian = false)
    {
        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(uint)).Reverse();
        }
        return BitConverter.ToUInt32(buffer);
    }
    public static ulong ToUInt64(Span<byte> buffer, bool littleEndian = false)
    {
        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(ulong)).Reverse();
        }
        return BitConverter.ToUInt64(buffer);
    }
    public static ushort ToUInt16(Span<byte> buffer, bool littleEndian = false)
    {
        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(ushort)).Reverse();
        }
        return BitConverter.ToUInt16(buffer);
    }
    public static uint ToUInt24(ReadOnlySpan<byte> buffer, bool littleEndian = false)
    {
        using (var owner = _memoryPool.Rent(4))
        {
            var memory = owner.Memory.Slice(0, 4);
            memory.Span.Clear();
            buffer.CopyTo(memory.Span.Slice(1));
            if (!littleEndian)
            {
                memory.Span.Reverse();
            }

            return BitConverter.ToUInt32(memory.Span);
        }
    }
    public static double ToDouble(Span<byte> buffer, bool littleEndian = false)
    {
        if (!littleEndian)
        {
            buffer[..sizeof(double)].Reverse();
        }
        return BitConverter.ToDouble(buffer);
    }

    public static bool TryGetUInt24Bytes(uint value, Span<byte> buffer, bool littleEndian = false)
    {
        if (buffer.Length < 3)
        {
            return false;
        }
        using (var owner = _memoryPool.Rent(4))
        {
            if (!BitConverter.TryWriteBytes(owner.Memory.Span, value))
            {
                return false;
            }

            var valueSpan = owner.Memory.Span.Slice(0, 3);

            if (!littleEndian)
            {
                valueSpan.Reverse();
            }
            valueSpan.CopyTo(buffer);
        }
        return true;
    }
    public static bool TryGetBytes(int value, Span<byte> buffer, bool littleEndian = false)
    {
        if (!BitConverter.TryWriteBytes(buffer, value))
        {
            return false;
        }

        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(int)).Reverse();
        }

        return true;
    }
    public static bool TryGetBytes(double value, Span<byte> buffer, bool littleEndian = false)
    {
        if (!BitConverter.TryWriteBytes(buffer, value))
        {
            return false;
        }

        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(double)).Reverse();
        }

        return true;
    }
    public static bool TryGetBytes(uint value, Span<byte> buffer, bool littleEndian = false)
    {
        if (!BitConverter.TryWriteBytes(buffer, value))
        {
            return false;
        }

        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(uint)).Reverse();
        }
            
        return true;
    }
    public static bool TryGetBytes(byte value, Span<byte> buffer)
    {
        if (buffer.Length < 1)
        {
            return false;
        }
        buffer[0] = value;

        return true;
    }
    public static bool TryGetBytes(ushort value, Span<byte> buffer, bool littleEndian = false)
    {
        if (!BitConverter.TryWriteBytes(buffer, value))
        {
            return false;
        }

        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(ushort)).Reverse();
        }

        return true;
    }
    public static bool TryGetBytes(ulong value, Span<byte> buffer, bool littleEndian = false)
    {
        if (!BitConverter.TryWriteBytes(buffer, value))
        {
            return false;
        }

        if (!littleEndian)
        {
            buffer.Slice(0, sizeof(ulong)).Reverse();
        }

        return true;
    }
}