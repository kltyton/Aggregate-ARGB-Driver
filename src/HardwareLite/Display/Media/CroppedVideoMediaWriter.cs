using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HardwareLite.Configuration;
using HardwareLite.Monitoring;
using NAudio.MediaFoundation;

namespace HardwareLite.Display.Media;

internal static class CroppedVideoMediaWriter
{
    private static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71"), H264 = new("34363248-0000-0010-8000-00aa00389b71"),
        Rgb32 = new("00000016-0000-0010-8000-00aa00389b71"), Size = new("1652c33d-d6b2-4012-b834-72030849a37d"),
        Rate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"), Aspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"),
        Interlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"), Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");

    public static bool TryWrite(string destination, Settings settings, Action<int> progress, CancellationToken cancellation)
    {
        var screen = settings.Screen;
        if (screen.Options.DeviceEncoding != DeviceMediaEncoding.H264 || screen.Playback != MediaPlayback.Single) return false;
        var mediaSettings = settings with { Screen = screen with { Mode = ScreenMode.Media, FontFiles = [] } };
        using var canvas = new ScreenCanvas(deviceOutput: true);
        using var image = new Bitmap(720, 720, PixelFormat.Format32bppRgb);
        canvas.RenderInto(image, mediaSettings, SensorSnapshot.Empty, 0);
        if (canvas.MediaDurationMs <= 0) return false;
        var edit = screen.MediaEdits.GetValueOrDefault(screen.MediaPath) ?? new MediaEdit();
        long duration = Math.Max(1, (long)Math.Ceiling(Math.Max(1, (long)(canvas.MediaDurationMs * edit.End) -
            (long)(canvas.MediaDurationMs * edit.Start)) / screen.Options.PlaybackSpeed));
        double sourceRate = canvas.MediaFrameRate * screen.Options.PlaybackSpeed;
        double fps = sourceRate > 0 ? Math.Min(screen.MaxFps, sourceRate) : screen.MaxFps;
        if (!(fps > 0) || !double.IsFinite(fps)) throw new InvalidDataException("设备视频帧率无效。");
        uint rateNumerator = Math.Max(1, (uint)Math.Round(fps * 1000)), rateDenominator = 1000;
        fps = rateNumerator / (double)rateDenominator;
        if (fps > screen.MaxFps) { rateNumerator = (uint)screen.MaxFps; rateDenominator = 1; fps = screen.MaxFps; }
        long durationTicks = checked(duration * 10000);
        IMFMediaType? output = null, input = null;
        IMFSinkWriter? writer = null;
        IMFSample? pending = null;
        long pendingTime = 0;
        byte[] pixels = new byte[720 * 720 * 4], previous = new byte[720 * 720 * 4];
        bool completed = false;
        try
        {
            MediaFoundationInterop.MFCreateSinkWriterFromURL(destination, null, null, out writer);
            output = MediaFoundationApi.CreateMediaType();
            SetType(output, H264, rateNumerator, rateDenominator);
            output.SetUINT32(MediaFoundationAttributes.MF_MT_AVG_BITRATE, screen.Options.DeviceVideoBitrate);
            output.SetUINT32(Profile, 77);
            writer.AddStream(output, out int stream);
            input = MediaFoundationApi.CreateMediaType(); SetType(input, Rgb32, rateNumerator, rateDenominator);
            input.SetUINT32(new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6"), 720 * 4);
            writer.SetInputMediaType(stream, input, null);
            writer.BeginWriting();
            long frames = Math.Max(1, (long)Math.Ceiling(duration * fps / 1000));
            for (long frame = 0; frame < frames; ++frame)
            {
                cancellation.ThrowIfCancellationRequested();
                long time = (long)Math.Floor(frame * 10000000 / fps);
                if (time >= durationTicks) break;
                canvas.RenderInto(image, mediaSettings, SensorSnapshot.Empty, time / 10000);
                var bitmap = image.LockBits(new Rectangle(0, 0, 720, 720), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try { for (int row = 0; row < 720; ++row) Marshal.Copy(bitmap.Scan0 + row * bitmap.Stride, pixels, row * 2880, 2880); }
                finally { image.UnlockBits(bitmap); }
                for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 0;
                if (pending is not null && pixels.AsSpan().SequenceEqual(previous)) continue;
                if (pending is not null)
                {
                    pending.SetSampleDuration(time - pendingTime); writer.WriteSample(stream, pending); Release(pending); pending = null;
                }
                var buffer = MediaFoundationApi.CreateMemoryBuffer(pixels.Length);
                try
                {
                    buffer.Lock(out IntPtr data, out _, out _);
                    try { Marshal.Copy(pixels, 0, data, pixels.Length); } finally { buffer.Unlock(); }
                    buffer.SetCurrentLength(pixels.Length);
                    pending = MediaFoundationApi.CreateSample(); pending.AddBuffer(buffer); pending.SetSampleTime(time);
                }
                finally { Release(buffer); }
                pixels.AsSpan().CopyTo(previous); pendingTime = time;
                progress((int)((frame + 1) * 100 / frames));
            }
            if (pending is not null) { pending.SetSampleDuration(durationTicks - pendingTime); writer.WriteSample(stream, pending); }
            writer.DoFinalize(); completed = true; progress(100);
            return true;
        }
        finally
        {
            Release(pending); Release(input); Release(output); Release(writer);
            if (!completed && File.Exists(destination)) File.Delete(destination);
        }
    }

    private static void SetType(IMFMediaType type, Guid subtype, uint numerator, uint denominator)
    {
        type.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, Video);
        type.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, subtype);
        type.SetUINT32(Interlace, 2);
        type.SetUINT64(Size, (long)((720UL << 32) | 720));
        type.SetUINT64(Rate, (long)(((ulong)numerator << 32) | denominator));
        type.SetUINT64(Aspect, (long)((1UL << 32) | 1));
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}
