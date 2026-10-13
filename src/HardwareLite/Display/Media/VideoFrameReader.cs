using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;
using HardwareLite.Configuration;

namespace HardwareLite.Display.Media;

internal sealed class VideoFrameReader : IDisposable
{
    private static readonly object StartupLock = new();
    private static readonly Guid VideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    private static readonly Guid VideoType = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid FrameRateAttribute = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private const int VideoStream = MediaFoundationInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM;
    private IMFSourceReader? _reader;
    private Bitmap? _frame;
    private int _stride;
    private int _width, _height;
    private readonly bool _borrowFrames;
    private IMFMediaBuffer? _heldBuffer;
    private IMFSample? _heldSample;
    private IMfBuffer2D? _heldSurface;
    private long _lastRequest = -1, _frameTime = -1;
    private bool _hasFrame, _ended;
    private VideoDevice? _device;
    private VideoReadback? _readback;
    private bool _processedOutput;
    public string DecoderStatus { get; private set; } = "软件视频处理";
    public long DurationMs { get; private set; }
    public long FrameSequence { get; private set; }
    public double FrameRate
    {
        get
        {
            _reader!.GetNativeMediaType(VideoStream, 0, out var timing);
            try
            {
                timing.GetUINT64(FrameRateAttribute, out long ratio);
                uint numerator = (uint)((ulong)ratio >> 32), denominator = (uint)ratio;
                return numerator == 0 || denominator == 0 ? 0 : numerator / (double)denominator;
            }
            catch (COMException error) when (error.HResult == unchecked((int)0xc00d36e6)) { return 0; }
            finally { Release(timing); }
        }
    }
    private readonly string _sourcePath;
    private ScreenOptions _options;

    public VideoFrameReader(string path, ScreenOptions? options = null) : this(path, options, false) { }

    public VideoFrameReader(string path, ScreenOptions? options, bool borrowFrames)
    {
        _borrowFrames = borrowFrames;
        options ??= new();
        options.Validate();
        _sourcePath = path; _options = options;
        if (!File.Exists(path)) throw new FileNotFoundException("视频文件不存在。", path);
        lock (StartupLock) MediaFoundationApi.Startup();
        try { Initialize(path, options); }
        catch (COMException error) when (options.VideoDecoder == VideoDecoderMode.Automatic)
        {
            Dispose();
            _options = options with { VideoDecoder = VideoDecoderMode.Software };
            try { Initialize(path, _options); DecoderStatus = "自动模式使用软件：" + error.Message; }
            catch (COMException softwareError) { Dispose(); throw new InvalidDataException("自动模式的软件解码也失败：" + softwareError.Message, softwareError); }
            catch { Dispose(); throw; }
        }
        catch (COMException error)
        {
            Dispose();
            throw new InvalidDataException($"Windows 无法解码视频 {Path.GetFileName(path)}：{error.Message}", error);
        }
        catch { Dispose(); throw; }
    }

    private void Initialize(string path, ScreenOptions options)
    {
        IMFAttributes? attributes = null;
        IMFMediaType? output = null;
        try
        {
            attributes = MediaFoundationApi.CreateAttributes(4);
            bool accelerated = options.VideoDecoder != VideoDecoderMode.Software;
            if (accelerated)
            {
                _device = new VideoDevice(options.VideoAdapterId);
            }
            _processedOutput = accelerated || options.DecodeWidth > 0;
            attributes.SetUINT32(_processedOutput ? new Guid("0f81da2c-b537-4672-a8b2-a681b17307a3") : VideoProcessing, 1);
            if (accelerated)
            {
                attributes.SetUnknown(new Guid("ec822da2-e1e9-4b29-a0d8-563c719f5269"), _device!.Manager);
                attributes.SetUINT32(new Guid("a634a91c-822b-41b9-a494-4de4643612b0"), 1);
                DecoderStatus = "硬件加速 · " + _device.Adapter.Name;
            }
            MediaFoundationInterop.MFCreateSourceReaderFromURL(path, attributes, out _reader);
            _reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
            _reader.SetStreamSelection(VideoStream, true);
            output = MediaFoundationApi.CreateMediaType();
            output.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, VideoType);
            output.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, Rgb32);
            if (options.DecodeWidth > 0)
            {
                _reader.GetNativeMediaType(VideoStream, 0, out var native);
                try
                {
                    native.GetUINT64(FrameSize, out long dimensions);
                    int width = (int)((ulong)dimensions >> 32), height = (int)(dimensions & uint.MaxValue);
                    if (width <= 0 || height <= 0) throw new InvalidDataException("视频源尺寸无效。");
                    double scale = Math.Min(options.DecodeWidth / (double)width, options.DecodeHeight / (double)height);
                    int resizedWidth = Math.Max(1, (int)Math.Round(width * scale)), resizedHeight = Math.Max(1, (int)Math.Round(height * scale));
                    output.SetUINT64(FrameSize, (long)(((ulong)resizedWidth << 32) | (uint)resizedHeight));
                }
                finally { Release(native); }
            }
            _reader.SetCurrentMediaType(VideoStream, IntPtr.Zero, output);
            ReadFormat();
            DurationMs = ReadDuration() / 10000;
            if (DurationMs <= 0) throw new InvalidDataException("视频没有有效的播放时长。");
        }
        finally { Release(output); Release(attributes); }
    }

    public Bitmap ReadFrame(long positionMs)
    {
        ObjectDisposedException.ThrowIf(_reader is null, this);
        positionMs = Math.Clamp(positionMs, 0, DurationMs - 1);
        try
        {
            if (_lastRequest >= 0 && (positionMs < _lastRequest || positionMs - _lastRequest > 1000)) Seek(positionMs);
            _lastRequest = positionMs;
            long target = checked(positionMs * 10000);
            while (!_ended && (!_hasFrame || _frameTime < target))
            {
                IMFSample? sample = null;
                try
                {
                    _reader.ReadSample(VideoStream, 0, out _, out var flags, out ulong timestamp, out sample);
                    if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0)
                        throw new InvalidDataException("Windows 视频解码器报告错误。");
                    if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0) ReadFormat();
                    _ended = (flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0;
                    if (sample is null) continue;
                    long time = checked((long)timestamp);
                    if (time >= target || _ended || !_hasFrame)
                    {
                        CopyFrame(sample);
                        if (_heldBuffer is not null) { _heldSample = sample; sample = null; }
                        _frameTime = time; _hasFrame = true; FrameSequence++;
                    }
                }
                finally { Release(sample); }
            }
            if (!_hasFrame) throw new InvalidDataException("视频中没有可读取的图像帧。");
            return _frame!;
        }
        catch (COMException error) when (_options.VideoDecoder == VideoDecoderMode.Automatic)
        {
            Dispose(); _options = _options with { VideoDecoder = VideoDecoderMode.Software };
            try
            {
                Initialize(_sourcePath, _options); _lastRequest = _frameTime = -1; _ended = _hasFrame = false;
                DecoderStatus = "自动模式使用软件：" + error.Message; return ReadFrame(positionMs);
            }
            catch (COMException softwareError) { Dispose(); throw new InvalidDataException("自动模式的软件读取也失败：" + softwareError.Message, softwareError); }
            catch { Dispose(); throw; }
        }
        catch (COMException error) { throw new InvalidDataException("视频读取失败：" + error.Message, error); }
    }

    private void ReadFormat()
    {
        _reader!.GetCurrentMediaType(VideoStream, out var type);
        try
        {
            type.GetUINT64(FrameSize, out long dimensions);
            type.GetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, out Guid subtype);
            if (subtype != Rgb32) throw new InvalidDataException("视频处理器未返回 RGB32。");
            int width = checked((int)((ulong)dimensions >> 32)), height = checked((int)(dimensions & uint.MaxValue));
            if (width <= 0 || height <= 0) throw new InvalidDataException("视频帧尺寸无效。");
            try { type.GetUINT32(DefaultStride, out _stride); }
            catch (COMException error) when (error.HResult == unchecked((int)0xC00D36E6)) { _stride = checked(width * 4); }
            if (_stride == int.MinValue || Math.Abs(_stride) < checked(width * 4)) throw new InvalidDataException("视频帧的行长度无效。");
            ReleaseFrame(); _width = width; _height = height;
            if (!_borrowFrames) _frame = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            _hasFrame = false;
        }
        finally { Release(type); }
    }

    private long ReadDuration()
    {
        IntPtr value = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        Marshal.StructureToPtr(default(PropVariant), value, false);
        try
        {
            Marshal.ThrowExceptionForHR(_reader!.GetPresentationAttribute(MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE,
                MediaFoundationAttributes.MF_PD_DURATION, value));
            object duration = Marshal.PtrToStructure<PropVariant>(value).Value;
            return duration is long number && number > 0 ? number : throw new InvalidDataException("无法读取视频时长。");
        }
        finally { PropVariant.Clear(value); Marshal.FreeHGlobal(value); }
    }

    private void Seek(long positionMs)
    {
        IntPtr value = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        Marshal.StructureToPtr(PropVariant.FromLong(checked(positionMs * 10000)), value, false);
        try { _reader!.SetCurrentPosition(Guid.Empty, value); _hasFrame = false; _ended = false; _frameTime = -1; }
        finally { PropVariant.Clear(value); Marshal.FreeHGlobal(value); }
    }

    private void CopyFrame(IMFSample sample)
    {
        sample.GetBufferCount(out int count);
        IMFMediaBuffer? buffer;
        if (_processedOutput && count == 1) sample.GetBufferByIndex(0, out buffer);
        else sample.ConvertToContiguousBuffer(out buffer);
        try
        {
            if (_borrowFrames)
            {
                if (_device is not null && buffer is IMfDxgiBuffer dxgi)
                {
                    ReleaseFrame();
                    _readback ??= new VideoReadback(_device.Device);
                    _frame = _readback.Read(dxgi, _width, _height);
                    return;
                }
                BorrowFrame(buffer); buffer = null; return;
            }
            if (_processedOutput && buffer is IMfBuffer2D surface)
            {
                surface.Lock2DSize(1, out IntPtr scanline, out int pitch, out IntPtr start, out uint size);
                try
                {
                    var frame = _frame!;
                    long first = scanline.ToInt64(), last = checked(first + (long)(frame.Height - 1) * pitch);
                    long begin = start.ToInt64(), end = checked(begin + size);
                    if (pitch == int.MinValue || Math.Abs(pitch) < checked(frame.Width * 4)
                        || Math.Min(first, last) < begin || checked(Math.Max(first, last) + frame.Width * 4L) > end)
                        throw new InvalidDataException("二维视频缓冲的行范围无效。");
                    var destination = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                    try { Marshal.ThrowExceptionForHR(MFCopyImage(destination.Scan0, destination.Stride, scanline, pitch, checked((uint)frame.Width * 4), (uint)frame.Height)); }
                    finally { frame.UnlockBits(destination); }
                }
                finally { surface.Unlock2D(); }
                return;
            }
            buffer.Lock(out IntPtr data, out _, out int length);
            try
            {
                var frame = _frame!;
                if (length < checked(Math.Abs(_stride) * frame.Height)) throw new InvalidDataException("解码后的视频帧不完整。");
                var destination = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    int first = _stride < 0 ? checked(-_stride * (frame.Height - 1)) : 0;
                    Marshal.ThrowExceptionForHR(MFCopyImage(destination.Scan0, destination.Stride, IntPtr.Add(data, first), _stride,
                        checked((uint)frame.Width * 4), (uint)frame.Height));
                }
                finally { frame.UnlockBits(destination); }
            }
            finally { buffer.Unlock(); }
        }
        finally { Release(buffer); }
    }

    private void BorrowFrame(IMFMediaBuffer buffer)
    {
        ReleaseFrame();
        IMfBuffer2D? surface = _processedOutput ? buffer as IMfBuffer2D : null;
        bool locked = false;
        try
        {
            IntPtr scanline; int pitch;
            if (surface is not null)
            {
                surface.Lock2DSize(1, out scanline, out pitch, out IntPtr start, out uint size); locked = true;
                long first = scanline.ToInt64(), last = checked(first + (long)(_height - 1) * pitch);
                long begin = start.ToInt64(), end = checked(begin + size);
                if (pitch == int.MinValue || Math.Abs(pitch) < checked(_width * 4)
                    || Math.Min(first, last) < begin || checked(Math.Max(first, last) + _width * 4L) > end)
                    throw new InvalidDataException("二维视频缓冲的行范围无效。");
            }
            else
            {
                buffer.Lock(out IntPtr data, out _, out int length); locked = true;
                if (length < checked(Math.Abs(_stride) * _height)) throw new InvalidDataException("解码后的视频帧不完整。");
                pitch = _stride;
                scanline = IntPtr.Add(data, pitch < 0 ? checked(-pitch * (_height - 1)) : 0);
            }
            // The bitmap borrows locked decoder memory until the next accepted frame or disposal.
            _frame = new Bitmap(_width, _height, pitch, PixelFormat.Format32bppRgb, scanline);
            _heldBuffer = buffer; _heldSurface = surface;
        }
        catch
        {
            if (locked) { if (surface is not null) surface.Unlock2D(); else buffer.Unlock(); }
            throw;
        }
    }

    private void ReleaseFrame()
    {
        _frame?.Dispose(); _frame = null;
        _readback?.Unmap();
        if (_heldBuffer is null) return;
        try { if (_heldSurface is not null) _heldSurface.Unlock2D(); else _heldBuffer.Unlock(); }
        finally { Release(_heldBuffer); _heldBuffer = null; _heldSurface = null; Release(_heldSample); _heldSample = null; }
    }

    private static void Release(object? value) { if (value is not null) Marshal.ReleaseComObject(value); }
    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCopyImage(IntPtr destination, int destinationStride, IntPtr source, int sourceStride, uint widthBytes, uint lines);
    public void Dispose() { ReleaseFrame(); _readback?.Dispose(); _readback = null; Release(_reader); _reader = null; _device?.Dispose(); _device = null; }
}
