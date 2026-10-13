using System.Buffers.Binary;
using System.Buffers;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using HardwareLite.Devices.Usb;
using HardwareLite.Configuration;

namespace HardwareLite.Devices.Asus;

internal sealed class PrimeSlcDisplay : IDisposable
{
    private static readonly Lazy<ImageCodecInfo> Jpeg = new(() => ImageCodecInfo.GetImageEncoders().Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid));
    private WinUsbConnection _usb;
    private byte _output;
    private byte _input;
    private byte[]? _packet;
    private int _packetLength;
    private MemoryStream? _jpeg;
    private long _lastSend;
    public string Status { get; private set; } = "已发送画面，设备信息未返回";
    public string FirmwareVersion { get; private set; } = "";
    public int? FirmwareImageVariant { get; private set; }
    public bool IsCustomFirmware { get; private set; }
    public DevicePlaybackProtocol DevicePlayback()
    {
        var protocol = new DevicePlaybackProtocol(_usb, _input, _output);
        protocol.QueryCapabilities();
        return protocol;
    }
    public void SetDevicePlaybackStatus()
    {
        ReleaseHostResources();
        Status = "设备内播放 · 固件 " + FirmwareVersion;
    }
    private void ReleaseHostResources()
    {
        _jpeg?.Dispose(); _jpeg = null;
        if (_packet is not null) { ArrayPool<byte>.Shared.Return(_packet); _packet = null; }
        _packetLength = 0;
    }
    public void SetMediaUploadStatus(int progress) => Status = progress < 100 ? $"上传设备媒体 · {progress}%" : "校验并保存设备媒体";

    public PrimeSlcDisplay()
    {
        _usb = new WinUsbConnection("vid_0b05&pid_1dc4");
        try
        {
            _output = _usb.Pipes.Single(pipe => pipe.Type == 2 && (pipe.Id & 0x80) == 0).Id;
            _input = _usb.Pipes.Single(pipe => pipe.Type == 2 && (pipe.Id & 0x80) != 0).Id;
            QueryInformation(_input);
        }
        catch { _usb.Dispose(); throw; }
    }

    private bool QueryInformation(byte input)
    {
        byte[] request = new byte[36];
        request[0] = 0xAA; request[1] = 0x55; request[2] = 0x4B; request[3] = 0x4C; request[4] = 0x54;
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(32), InformationChecksum(request.AsSpan(0, 32)));
        _usb.Write(_output, request);
        byte[] response;
        try { response = _usb.Read(input, 36); }
        catch (Win32Exception error) when (error.NativeErrorCode == 121) { return false; }
        if (response.Length != 36 || response[0] != 0xAA || response[1] != 0x55 || response[2] != 0x3B ||
            InformationChecksum(response.AsSpan(0, 32)) != BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(32)))
            throw new IOException("水冷屏幕返回的设备信息无效。");
        int width = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(14, 2));
        int height = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(16, 2));
        if (width != 720 || height != 720) throw new IOException($"水冷屏幕尺寸不匹配：{width}×{height}。");
        FirmwareVersion = Encoding.ASCII.GetString(response, 18, 5).TrimEnd('\0');
        FirmwareImageVariant = response[3] == 0 ? 0 : 1;
        IsCustomFirmware = response[23] == (byte)'H';
        Status = "已发送画面 · 固件 " + FirmwareVersion;
        return true;
    }

    private static uint InformationChecksum(ReadOnlySpan<byte> bytes)
        => FirmwarePackage.Checksum(bytes);

    public void Show(Bitmap image, HardwareLite.Configuration.ScreenSettings settings)
    {
        _jpeg ??= new MemoryStream();
        _jpeg.SetLength(0);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)settings.Options.JpegQuality);
        image.Save(_jpeg, Jpeg.Value, parameters);
        int jpegLength = checked((int)_jpeg.Length);
        int length = jpegLength + 40;
        _packetLength = length % 512 == 0 ? length + 1 : length;
        if (_packet is null || _packet.Length < _packetLength)
        {
            if (_packet is not null) ArrayPool<byte>.Shared.Return(_packet);
            _packet = ArrayPool<byte>.Shared.Rent(_packetLength);
        }
        byte[] packet = _packet;
        Array.Clear(packet, 0, 40);
        if (_packetLength != length) packet[length] = 0;
        packet[0] = 0x5A; packet[1] = 0xA5;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(2, 4), jpegLength);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6, 2), Checksum(packet.AsSpan(2, 4)));
        packet[8] = (byte)settings.Brightness;
        packet[10] = settings.SleepWithDisplay ? (byte)1 : (byte)0;
        _jpeg.GetBuffer().AsSpan(0, jpegLength).CopyTo(packet.AsSpan(40));
        Send();
    }

    public void Maintain(int intervalMs = 1000)
    {
        if (_packet is not null && Stopwatch.GetElapsedTime(_lastSend).TotalMilliseconds >= intervalMs) Send();
    }

    public void UpdateFirmware(FirmwarePackage package, Action<int> progress)
    {
        if (FirmwareVersion.Length == 0) throw new InvalidOperationException("设备未返回正常运行状态，不能开始固件更新。");
        if (package.ImageVariant != FirmwareImageVariant) throw new InvalidOperationException("固件映像与当前设备的平台标志不匹配，请重新选择文件。");
        Thread.Sleep(200);
        // The device writes and synchronizes NOR flash before acknowledging each chunk.
        _usb.SetTimeout(_input, 120000);
        try { UploadFirmware(package, progress); }
        finally { _usb.SetTimeout(_input, 1500); }
    }

    private void UploadFirmware(FirmwarePackage package, Action<int> progress)
    {
        byte[] image = package.Application;
        for (int offset = 0; offset < image.Length;)
        {
            int count = Math.Min(0x32000, image.Length - offset);
            byte[] packet = new byte[count + 18];
            packet[0] = 0xAA; packet[1] = 0x55; packet[2] = 0x4A;
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(3, 4), image.Length);
            packet[7] = (byte)(count >> 16); packet[8] = (byte)(count >> 8); packet[9] = (byte)count;
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(10, 4), offset);
            image.AsSpan(offset, count).CopyTo(packet.AsSpan(14));
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(count + 14, 4), FirmwarePackage.Checksum(packet.AsSpan(0, count + 14)));
            try { _usb.Write(_output, packet); }
            catch (Win32Exception error) { throw new IOException($"固件数据写入失败（{offset}/{image.Length} 字节）：{error.Message}", error); }
            byte[] reply;
            try { reply = _usb.Read(_input, 512); }
            catch (Win32Exception error) { throw new IOException($"设备未回应固件数据（{offset}/{image.Length} 字节）：{error.Message}", error); }
            if (reply.Length != 13 || reply[0] != 0xAA || reply[1] != 0x55 || reply[2] != 0x5A ||
                BinaryPrimitives.ReadInt32BigEndian(reply.AsSpan(4)) != offset ||
                FirmwarePackage.Checksum(reply.AsSpan(0, 9)) != BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(9, 4)))
                throw new IOException("设备未确认固件数据，更新已停止。");
            offset += count;
            progress(offset * 100 / image.Length);
        }
        var clock = Stopwatch.StartNew();
        var total = Stopwatch.StartNew();
        _usb.SetTimeout(_input, 30000);
        while (clock.ElapsedMilliseconds < 30000 && total.ElapsedMilliseconds < 180000)
        {
            byte[] reply = _usb.Read(_input, 64);
            if (reply.Length != 9 || reply[0] != 0xAA || reply[1] != 0x55 || reply[2] != 0x6C ||
                FirmwarePackage.Checksum(reply.AsSpan(0, 5)) != BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(5, 4)))
                throw new IOException("固件写入进度回应无效。");
            progress(reply[4]);
            clock.Restart();
            if (reply[4] != 100) continue;
            ReconnectAfterFirmwareUpdate();
            if (FirmwareVersion != package.Version || FirmwareImageVariant != package.ImageVariant)
                throw new IOException($"固件更新后设备信息不匹配：预期 {package.Version}，实际 {FirmwareVersion}。");
            return;
        }
        throw new IOException("设备未在规定时间内确认固件更新完成。");
    }

    private void ReconnectAfterFirmwareUpdate()
    {
        var deadline = Stopwatch.StartNew();
        Exception? connectionError = null;
        while (deadline.ElapsedMilliseconds < 15000)
        {
            Thread.Sleep(250);
            WinUsbConnection replacement;
            try { replacement = new WinUsbConnection("vid_0b05&pid_1dc4"); }
            catch (Exception error) when (error is Win32Exception or IOException) { connectionError = error; continue; }
            _usb.Dispose(); _usb = replacement;
            _output = _usb.Pipes.Single(pipe => pipe.Type == 2 && (pipe.Id & 0x80) == 0).Id;
            _input = _usb.Pipes.Single(pipe => pipe.Type == 2 && (pipe.Id & 0x80) != 0).Id;
            try { if (QueryInformation(_input)) return; }
            catch (Win32Exception error) when (error.NativeErrorCode is 6 or 31 or 1167 or 121) { connectionError = error; }
        }
        throw new IOException("固件写入完成后设备未恢复正常 USB 连接。", connectionError);
    }

    private void Send()
    {
        _usb.Write(_output, _packet!, _packetLength);
        _lastSend = Stopwatch.GetTimestamp();
    }

    private static ushort Checksum(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (ushort)((crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xA001);
        }
        return crc;
    }

    public void Dispose()
    {
        _usb.Dispose(); ReleaseHostResources();
    }
}
