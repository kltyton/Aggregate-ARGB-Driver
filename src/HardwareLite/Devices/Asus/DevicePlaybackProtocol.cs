using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using HardwareLite.Configuration;
using HardwareLite.Devices.Usb;

namespace HardwareLite.Devices.Asus;

internal sealed class DevicePlaybackProtocol(WinUsbConnection usb, byte input, byte output)
{
    private uint _sequence;
    private readonly byte[] _read = new byte[512], _reply = new byte[36];
    private const int MaximumData = 0x32000;
    public uint Features { get; private set; }
    public int ApiVersion { get; private set; }
    public int MaximumFps { get; private set; }

    public void QueryCapabilities()
    {
        byte[] reply = Command(0x70);
        if (reply[4] is not (3 or 4 or 5 or 6) || Encoding.ASCII.GetString(reply, 9, 5) != "HL104" ||
            BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(5)) != 720 ||
            BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(7)) != 720 ||
            BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(26)) != MaximumData)
            throw new InvalidDataException("设备内播放接口与当前协议不匹配。");
        Features = BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(22));
        ApiVersion = reply[4];
        MaximumFps = reply[30];
        if ((Features & 7) != 7 || MaximumFps != 60)
            throw new IOException("设备内播放工作线程尚未就绪。");
    }

    public void Upload(string path, Action<int> progress)
    {
        using var media = File.OpenRead(path);
        if (media.Length <= 0 || media.Length > uint.MaxValue) throw new InvalidDataException("设备媒体文件大小无效。");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaximumData);
        try
        {
            uint crc = 0;
            int count;
            while ((count = media.Read(buffer, 0, MaximumData)) > 0) crc = FirmwarePackage.Checksum(buffer.AsSpan(0, count), crc);
            media.Position = 0;
            usb.SetTimeout(input, 15000);
            Command(0x71, (uint)media.Length, crc);
            bool committed = false;
            try
            {
                uint offset = 0;
                while ((count = media.Read(buffer, 0, MaximumData)) > 0)
                {
                    byte[] reply = Command(0x72, offset, payload: buffer.AsSpan(0, count));
                    offset = checked(offset + (uint)count);
                    if (BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(9)) != offset)
                        throw new IOException("设备确认的媒体偏移不匹配。");
                    progress((int)((ulong)offset * 100 / (ulong)media.Length));
                }
                usb.SetTimeout(input, (uint)Math.Clamp(30000L + media.Length * 60000 / (1024 * 1024), 120000L, 900000L));
                Command(0x73);
                committed = true;
            }
            finally { if (!committed) Command(0x74); }
        }
        finally
        {
            try { usb.SetTimeout(input, 1500); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
    }

    public void Play(ScreenSettings settings)
        => Command(0x75, DisplayArguments(settings) | (settings.Mode == ScreenMode.Monitor ? 1u : 0));
    public void Configure(ScreenSettings settings) => Command(0x7b, DisplayArguments(settings));
    public void SelectTrack(int track, long phaseMs)
    {
        if (track < 0 || phaseMs < 0) throw new ArgumentOutOfRangeException(nameof(track));
        Command(0x7e, (uint)track, (uint)phaseMs, (uint)((ulong)phaseMs >> 32));
    }
    private static uint DisplayArguments(ScreenSettings settings)
        => (uint)settings.Brightness << 24 | (settings.SleepWithDisplay ? 1u << 16 : 0) | (uint)settings.Rotation << 8;
    public bool HasMedia(string path)
    {
        using var media = File.OpenRead(path);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaximumData);
        try
        {
            uint crc = 0; int count;
            while ((count = media.Read(buffer, 0, MaximumData)) > 0) crc = FirmwarePackage.Checksum(buffer.AsSpan(0, count), crc);
            usb.SetTimeout(input, 120000);
            return Command(0x7a, checked((uint)media.Length), crc, acceptChecksumMismatch: true)[3] == 0;
        }
        finally { usb.SetTimeout(input, 1500); ArrayPool<byte>.Shared.Return(buffer); }
    }
    public void Stop() => Command(0x76);
    public bool IsPlaying() => Command(0x77)[19] != 0;
    public (uint Frames, ulong PresentationTimeUs, bool Playing, uint StreamErrors, uint StreamBytes, byte CropErrors) PlaybackStatistics()
    {
        byte[] reply = Command(0x7c);
        return (BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(20)),
            BinaryPrimitives.ReadUInt64BigEndian(reply.AsSpan(24)), reply[19] != 0,
            BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(9)), BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(13)), reply[18]);
    }
    public uint FreeStorageBytes() => BinaryPrimitives.ReadUInt32BigEndian(Command(0x77).AsSpan(20));
    public uint MediaCapacityBytes()
    {
        if (ApiVersion < 4 || (Features & 8) == 0) throw new InvalidOperationException("当前固件不支持无损设备媒体，请先更新固件。");
        return BinaryPrimitives.ReadUInt32BigEndian(Command(0x7d).AsSpan(20));
    }
    public void ClearFirmwareUpload() => Command(0x79);
    public void EnableOverlay() => Command(0x78);
    public void DisableOverlay() => Command(0x78, 1u << 24);
    public void ClearSprites() => Command(0x78, 2u << 24);
    public void LoadUi(string path, ReadOnlySpan<byte> configurationKey)
    {
        using var source = File.OpenRead(path);
        if (source.Length is <= 0 or > 8 * 1024 * 1024) throw new InvalidDataException("设备字形包大小无效。");
        Span<byte> header = stackalloc byte[56]; source.ReadExactly(header);
        if (!header[24..56].SequenceEqual(configurationKey)) throw new InvalidDataException("设备字形包与当前布局不匹配，请在 GUI 重新保存配置。");
        source.Position = 0;
        byte[] block = ArrayPool<byte>.Shared.Rent(MaximumData);
        bool committed = false;
        try
        {
            uint crc = 0; int count;
            while ((count = source.Read(block, 0, MaximumData)) > 0) crc = FirmwarePackage.Checksum(block.AsSpan(0, count), crc);
            source.Position = 0;
            Command(0x78, 5u << 24, (uint)source.Length, crc);
            uint offset = 0;
            while ((count = source.Read(block, 0, MaximumData)) > 0)
            {
                Command(0x78, 6u << 24, offset, payload: block.AsSpan(0, count));
                offset += (uint)count;
            }
            Command(0x78, 7u << 24); committed = true;
        }
        finally
        {
            try { if (!committed) Command(0x78, 9u << 24); }
            finally { ArrayPool<byte>.Shared.Return(block); }
        }
    }
    public void UpdateUi(ReadOnlySpan<byte> values) => Command(0x78, 8u << 24, payload: values);
    public void StoreSprite(ushort slot, ushort width, ushort height, ReadOnlySpan<byte> pixels)
    {
        if (slot >= 256 || width is 0 or > 720 || height is 0 or > 720 || pixels.Length != width * height * 4)
            throw new ArgumentException("监控字形尺寸或槽位无效。");
        Command(0x78, 3u << 24, slot, width, height, pixels);
    }
    public void RenderSprites(ReadOnlySpan<byte> instances)
    {
        if (instances.Length % 6 != 0 || instances.Length / 6 > 512)
            throw new ArgumentException("监控字形列表无效。");
        Command(0x78, 4u << 24, payload: instances);
    }

    private byte[] Command(byte command, uint argument0 = 0, uint argument1 = 0, uint argument2 = 0,
        uint argument3 = 0, ReadOnlySpan<byte> payload = default, bool acceptChecksumMismatch = false)
    {
        if (payload.Length > MaximumData) throw new ArgumentOutOfRangeException(nameof(payload));
        int count = checked(payload.Length + 36);
        int wireCount = count + (count % 512 == 0 ? 1 : 0);
        byte[] packet = ArrayPool<byte>.Shared.Rent(wireCount);
        uint sequence = unchecked(++_sequence);
        try
        {
            Array.Clear(packet, 0, 32);
            packet[0] = 0xaa; packet[1] = 0x55; packet[2] = command;
            packet[3] = (byte)(wireCount - count);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), sequence);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), argument0);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), argument1);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(16), argument2);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(20), (uint)payload.Length);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(24), argument3);
            payload.CopyTo(packet.AsSpan(32));
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(count - 4), FirmwarePackage.Checksum(packet.AsSpan(0, count - 4)));
            if (wireCount != count) packet[count] = 0;
            usb.Write(output, packet, wireCount);
        }
        finally { ArrayPool<byte>.Shared.Return(packet); }
        int received = usb.ReadInto(input, _read, _read.Length);
        if (received != 36) throw new InvalidDataException("设备内播放回应长度无效。");
        _read.AsSpan(0, 36).CopyTo(_reply);
        byte[] reply = _reply;
        if (reply.Length != 36 || reply[0] != 0xaa || reply[1] != 0x55 ||
            reply[2] != (command == 0x70 ? 0x6e : command) ||
            FirmwarePackage.Checksum(reply.AsSpan(0, 32)) != BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(32)) ||
            (command != 0x70 && BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(5)) != sequence))
            throw new InvalidDataException("设备内播放回应校验失败。");
        if (reply[3] != 0 && !(acceptChecksumMismatch && reply[3] == 4)) throw new IOException("设备内播放请求失败：" + (reply[3] switch
        {
            1 => "数据顺序或参数不正确", 2 => "设备存储或内存不足", 3 => "设备文件或显示操作失败",
            4 => "数据校验失败", 5 => "设备忙", _ => "未知错误 " + reply[3]
        }) + (reply[31] == 0 ? "" : "；文件提交阶段 " + reply[31]) +
            (command == 0x73 && BinaryPrimitives.ReadInt32BigEndian(reply.AsSpan(24)) is var nativeError && nativeError != 0
                ? "；原生文件错误 " + nativeError : "") +
            (command == 0x73 && reply[31] == 6 ? "；默认播放占用 " + reply[30] : ""));
        return reply;
    }

}
