using System.Drawing.Drawing2D;
namespace HardwareLite.Configuration;

public enum VideoDecoderMode { Software, Hardware, Automatic }
public enum MissingDataPolicy { Placeholder, KeepLast, Off, Retry }
public enum DeviceStopBehavior { KeepState, TurnOff }
public enum DeviceMediaEncoding { H264, LosslessPixels }

public sealed record ScreenOptions
{
    public int JpegQuality { get; init; } = 90;
    public bool DevicePlayback { get; init; }
    public DeviceMediaEncoding DeviceEncoding { get; init; }
    public int DeviceVideoBitrate { get; init; } = 2000000;
    public int PreviewFps { get; init; } = 10;
    public bool PreviewEnabled { get; init; } = true;
    public bool PreserveGifDelays { get; init; }
    public int GifMinimumDelayMs { get; init; } = 20;
    public int GifMissingDelayMs { get; init; } = 100;
    public double PlaybackSpeed { get; init; } = 1;
    public VideoDecoderMode VideoDecoder { get; init; }
    public string VideoAdapterId { get; init; } = "";
    public int DecodeWidth { get; init; }
    public int DecodeHeight { get; init; }
    public InterpolationMode Interpolation { get; init; } = InterpolationMode.Default;
    public int MediaCapacity { get; init; } = 30;
    public int KeepaliveMs { get; init; } = 1000;
    public bool ShuffleAvoidRepeats { get; init; }
    public TemperatureUnit? TemperatureUnit { get; init; }
    public MissingDataPolicy ExpiredData { get; init; }
    public DeviceStopBehavior OnStop { get; init; }

    public void Validate()
    {
        if (JpegQuality is < 0 or > 100 || PreviewFps is < 1 or > 60 || !Enum.IsDefined(DeviceEncoding)
            || DeviceVideoBitrate is < 128000 or > 20000000
            || GifMinimumDelayMs is < 1 or > 1000 || GifMissingDelayMs is < 1 or > 10000
            || !double.IsFinite(PlaybackSpeed) || PlaybackSpeed is < 0.125 or > 8
            || DecodeWidth is < 0 or > 7680 || DecodeHeight is < 0 or > 7680
            || (DecodeWidth == 0) != (DecodeHeight == 0) || MediaCapacity is < 1 or > 1024
            || KeepaliveMs is < 100 or > 60000 || !Enum.IsDefined(VideoDecoder) || VideoAdapterId is null
            || !Enum.IsDefined(Interpolation) || Interpolation == InterpolationMode.Invalid
            || !Enum.IsDefined(ExpiredData) || !Enum.IsDefined(OnStop)
            || TemperatureUnit.HasValue && !Enum.IsDefined(TemperatureUnit.Value))
            throw new InvalidDataException("屏幕画质、预览、播放、解码尺寸或容量设置无效。");
    }
}

public sealed record ClockOptions
{
    public bool FaceVisible { get; init; } = true;
    public bool SecondHandVisible { get; init; } = true;
    public bool HourMarksVisible { get; init; } = true;
    public bool HourNumbersVisible { get; init; }
    public int FaceWidth { get; init; } = 3;
    public int HourWidth { get; init; } = 5;
    public int MinuteWidth { get; init; } = 5;
    public int SecondWidth { get; init; } = 2;
    public double HourLength { get; init; } = 0.5;
    public double MinuteLength { get; init; } = 0.75;
    public double SecondLength { get; init; } = 0.85;
    public void Validate()
    {
        if (FaceWidth is < 1 or > 20 || HourWidth is < 1 or > 20 || MinuteWidth is < 1 or > 20 || SecondWidth is < 1 or > 20
            || !double.IsFinite(HourLength + MinuteLength + SecondLength)
            || HourLength is < 0.1 or > 1 || MinuteLength is < 0.1 or > 1 || SecondLength is < 0.1 or > 1)
            throw new InvalidDataException("指针时钟线宽或长度无效。");
    }
}
