using System.Buffers.Binary;
using System.IO.Compression;
using HardwareLite.Configuration;

namespace HardwareLite.Display.Media;

internal static class CompressedVideoMediaWriter
{
    private const int BlockBytes = 65536, HeaderBytes = 64;
    public static void Write(string destination, string video, int maximumFps, uint durationMs, long maximumBytes,
        Action<int> progress, CancellationToken cancellation)
    {
        using var source = File.OpenRead(video);
        long blocks = (source.Length + BlockBytes - 1) / BlockBytes;
        long tableBytes = HeaderBytes + blocks * 12;
        if (tableBytes > maximumBytes) throw new DeviceMediaCapacityException(tableBytes, maximumBytes);
        byte[] header = new byte[HeaderBytes];
        "HLV1"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), checked((ulong)source.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), BlockBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), checked((uint)blocks));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), HeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), (uint)maximumFps);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(40), 1);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(52), durationMs);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(60), FirmwarePackage.Checksum(header.AsSpan(0, 60)));
        using var output = new FileStream(destination, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        bool completed = false;
        try
        {
            output.Write(header); output.SetLength(tableBytes); output.Position = tableBytes;
            byte[] data = new byte[BlockBytes], record = new byte[12];
            using var compressed = new MemoryStream();
            for (long block = 0; block < blocks; ++block)
            {
                cancellation.ThrowIfCancellationRequested();
                int bytes = (int)Math.Min(BlockBytes, source.Length - source.Position);
                source.ReadExactly(data.AsSpan(0, bytes)); compressed.SetLength(0);
                using (var codec = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) codec.Write(data.AsSpan(0, bytes));
                bool raw = compressed.Length >= bytes;
                int count = raw ? bytes : checked((int)compressed.Length);
                long required = output.Position + count;
                if (required > maximumBytes) throw new DeviceMediaCapacityException(required, maximumBytes);
                uint offset = checked((uint)output.Position);
                if (raw) output.Write(data, 0, count); else output.Write(compressed.GetBuffer(), 0, count);
                long end = output.Position;
                BinaryPrimitives.WriteUInt32LittleEndian(record, offset);
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)count | (raw ? 0x80000000 : 0));
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), FirmwarePackage.Checksum(data.AsSpan(0, bytes)));
                output.Position = HeaderBytes + block * 12; output.Write(record); output.Position = end;
                progress((int)((block + 1) * 100 / blocks));
            }
            output.Flush(true); completed = true;
        }
        finally { output.Dispose(); if (!completed && File.Exists(destination)) File.Delete(destination); }
    }
}
