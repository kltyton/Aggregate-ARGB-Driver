using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using HardwareLite.Configuration;
using HardwareLite.Monitoring;

namespace HardwareLite.Display.Media;

internal sealed class DeviceMediaCapacityException(long requiredBytes, long availableBytes)
    : IOException($"无损媒体已达到 {requiredBytes / 1048576d:F2} MiB，超过设备可用的 {availableBytes / 1048576d:F2} MiB。")
{
    public long RequiredBytes { get; } = requiredBytes;
    public long AvailableBytes { get; } = availableBytes;
}

internal static class DeviceMediaWriter
{
    internal const int Width = 720, Height = 720, FrameBytes = Width * Height * 4;
    private const uint KeyFrame = 0x80000000, RawFrame = 0x40000000;
    private sealed record FrameIndex(uint TimeMs, uint Offset, uint SizeAndFlags, uint Checksum);

    public static void Write(string destination, Settings settings, long maximumBytes,
        Action<int> progress, CancellationToken cancellation)
    {
        settings.Validate();
        string video = destination + ".encoded.mp4";
        try
        {
            if (CroppedVideoMediaWriter.TryWrite(video, settings, value => progress(value * 80 / 100), cancellation))
            {
                using var timing = new VideoFrameReader(video);
                CompressedVideoMediaWriter.Write(destination, video, settings.Screen.MaxFps, checked((uint)timing.DurationMs), maximumBytes,
                    value => progress(80 + value * 20 / 100), cancellation);
                return;
            }
        }
        finally { if (File.Exists(video)) File.Delete(video); }
        string[] paths = settings.Screen.Playback == MediaPlayback.Single
            ? [settings.Screen.MediaPath] : settings.Screen.MediaFiles;
        long tableBytes = 32 + paths.Length * 16L;
        if (maximumBytes < tableBytes) throw new DeviceMediaCapacityException(tableBytes, maximumBytes);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        byte[] header = new byte[32];
        "HLA1"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), Width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), Height);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)settings.Screen.MaxFps);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)paths.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 32);
        output.Write(header);
        output.SetLength(tableBytes);
        output.Position = output.Length;
        byte[] pixels = new byte[FrameBytes], previous = new byte[FrameBytes], delta = new byte[FrameBytes];
        bool completed = false;
        try
        {
            for (int track = 0; track < paths.Length; ++track)
            {
                cancellation.ThrowIfCancellationRequested();
                var screen = settings.Screen with
                {
                    Mode = ScreenMode.Media, Playback = MediaPlayback.Single, MediaPath = paths[track], FontFiles = []
                };
                var mediaSettings = settings with { Screen = screen };
                using var canvas = new ScreenCanvas(deviceOutput: true);
                using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
                canvas.RenderInto(bitmap, mediaSettings, SensorSnapshot.Empty, 0);
                var edit = screen.MediaEdits.GetValueOrDefault(screen.MediaPath) ?? new MediaEdit();
                long duration = canvas.MediaDurationMs > 0
                    ? Math.Max(1, (long)Math.Ceiling(Math.Max(1, (long)(canvas.MediaDurationMs * edit.End) -
                        (long)(canvas.MediaDurationMs * edit.Start)) / screen.Options.PlaybackSpeed)) : 1000;
                if (duration > uint.MaxValue) throw new InvalidDataException("媒体播放片段时长超过设备格式的范围。");
                long samples = Math.Max(1, (long)Math.Ceiling(duration * screen.MaxFps / 1000d));
                var frames = new List<FrameIndex>();
                using var compressed = new MemoryStream();
                for (long sample = 0; sample < samples; ++sample)
                {
                    cancellation.ThrowIfCancellationRequested();
                    uint time = checked((uint)(sample * 1000 / screen.MaxFps));
                    canvas.RenderInto(bitmap, mediaSettings, SensorSnapshot.Empty, time);
                    CopyPixels(bitmap, pixels);
                    bool first = frames.Count == 0;
                    if (!first && pixels.AsSpan().SequenceEqual(previous))
                    {
                        progress((int)((track + (sample + 1) / (double)samples) * 100 / paths.Length));
                        continue;
                    }
                    bool key = first || frames.Count % 60 == 0;
                    ReadOnlySpan<byte> data = pixels;
                    if (!key)
                    {
                        for (int index = 0; index < FrameBytes; ++index) delta[index] = (byte)(pixels[index] ^ previous[index]);
                        data = delta;
                    }
                    compressed.SetLength(0);
                    using (var codec = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) codec.Write(data);
                    bool raw = compressed.Length >= FrameBytes;
                    int count = raw ? FrameBytes : checked((int)compressed.Length);
                    long required = output.Position + count + (frames.Count + 1) * 16L;
                    if (required > maximumBytes) throw new DeviceMediaCapacityException(required, maximumBytes);
                    uint offset = checked((uint)output.Position);
                    if (raw) output.Write(data); else output.Write(compressed.GetBuffer(), 0, count);
                    frames.Add(new(time, offset, (uint)count | (key ? KeyFrame : 0) | (raw ? RawFrame : 0),
                        FirmwarePackage.Checksum(data)));
                    pixels.AsSpan().CopyTo(previous);
                    progress((int)((track + (sample + 1) / (double)samples) * 100 / paths.Length));
                }
                uint indexOffset = checked((uint)output.Position);
                byte[] indexRecord = new byte[16];
                foreach (var frame in frames)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(indexRecord, frame.TimeMs);
                    BinaryPrimitives.WriteUInt32LittleEndian(indexRecord.AsSpan(4), frame.Offset);
                    BinaryPrimitives.WriteUInt32LittleEndian(indexRecord.AsSpan(8), frame.SizeAndFlags);
                    BinaryPrimitives.WriteUInt32LittleEndian(indexRecord.AsSpan(12), frame.Checksum);
                    output.Write(indexRecord);
                }
                long end = output.Position;
                output.Position = 32 + track * 16L;
                BinaryPrimitives.WriteUInt32LittleEndian(indexRecord, checked((uint)duration));
                BinaryPrimitives.WriteUInt32LittleEndian(indexRecord.AsSpan(4), checked((uint)frames.Count));
                BinaryPrimitives.WriteUInt32LittleEndian(indexRecord.AsSpan(8), indexOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(indexRecord.AsSpan(12), 0);
                output.Write(indexRecord);
                output.Position = end;
            }
            progress(100);
            output.Flush(flushToDisk: true);
            completed = true;
        }
        finally
        {
            output.Dispose();
            if (!completed && File.Exists(destination)) File.Delete(destination);
        }
    }

    private static void CopyPixels(Bitmap bitmap, byte[] pixels)
    {
        var source = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int row = 0; row < Height; ++row)
                Marshal.Copy(source.Scan0 + row * source.Stride, pixels, row * Width * 4, Width * 4);
            for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 0;
        }
        finally { bitmap.UnlockBits(source); }
    }
}
