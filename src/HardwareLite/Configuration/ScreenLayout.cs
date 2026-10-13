namespace HardwareLite.Configuration;

public enum ScreenMetric
{
    CpuTemperature, CpuUsage, CpuPower, CpuClock, CpuFan,
    GpuTemperature, GpuUsage, GpuPower, GpuClock, SystemTemperature,
    Sensor, Time, Date, Text, AnalogClock,
    CpuVoltage, GpuVoltage, AioPump, SystemFan, MemoryClock
}
public enum ImageFit { Fill, Fit, Stretch, Original }
public enum TextAlignment { Left, Center, Right }

public sealed record ScreenElement
{
    public ScreenMetric Metric { get; init; }
    public string SensorId { get; init; } = "";
    public string Title { get; init; } = "CPU";
    public string Caption { get; init; } = "";
    public string Text { get; init; } = "";
    public string Format { get; init; } = "";
    public string NumericFormat { get; init; } = "";
    public int DecimalPlaces { get; init; } = -1;
    public bool HideUnit { get; init; }
    public string CustomUnit { get; init; } = "";
    public TemperatureUnit? TemperatureUnit { get; init; }
    public int X { get; init; } = 108;
    public int Y { get; init; } = 90;
    public int Width { get; init; } = 504;
    public int Height { get; init; } = 156;
    public string FontFamily { get; init; } = "Segoe UI";
    public int FontSize { get; init; } = 63;
    public bool Bold { get; init; } = true;
    public string Color { get; init; } = "#39C5BB";
    public string TitleColor { get; init; } = "#99D300";
    public string TitleFontFamily { get; init; } = "";
    public int TitleFontSize { get; init; }
    public string UnitFontFamily { get; init; } = "";
    public int UnitFontSize { get; init; }
    public TextAlignment Alignment { get; init; }
    public string CaptionFontFamily { get; init; } = "";
    public int CaptionFontSize { get; init; } = 22;
    public int CaptionHeight { get; init; } = 26;
    public string CaptionColor { get; init; } = "#A5B8C5";
    public ClockOptions Clock { get; init; } = new();
}

public sealed record ScreenLayout
{
    public string BackgroundColor { get; init; } = "#10151C";
    public ImageFit ImageFit { get; init; } = ImageFit.Fill;
    public int ImageScale { get; init; } = 100;
    public int ImageX { get; init; }
    public int ImageY { get; init; }
    public int ImageRotation { get; init; }
    public int BackgroundShade { get; init; } = 30;
    public int MaxElements { get; init; } = 32;
    public int MinimumElementSize { get; init; } = 16;
    public int MinimumFontSize { get; init; } = 8;
    public int MaximumFontSize { get; init; } = 200;
    public ScreenElement[] Elements { get; init; } =
    [
        new() { Metric = ScreenMetric.CpuUsage, Caption = "CPU Usage", Title = "CPU", X = 40, Y = 90, Width = 640, Height = 330, FontSize = 130 },
        new() { Metric = ScreenMetric.GpuUsage, Caption = "GPU Usage", Title = "GPU", X = 40, Y = 450, Width = 300, Height = 220, FontSize = 80 },
        new() { Metric = ScreenMetric.SystemTemperature, Caption = "System Temperature", Title = "System", X = 380, Y = 450, Width = 300, Height = 220, FontSize = 80 }
    ];

    public void Validate()
    {
        if (MaxElements is < 1 or > 256 || MinimumElementSize is < 1 or > 720 || MinimumFontSize is < 1 or > 1024
            || MaximumFontSize < MinimumFontSize || MaximumFontSize > 1024 || Elements is null || Elements.Length > MaxElements)
            throw new InvalidDataException("布局容量或尺寸范围无效。");
        if (ImageScale is < 10 or > 500 || ImageX is < -1440 or > 1440 || ImageY is < -1440 or > 1440
            || ImageRotation is < 0 or > 359 || BackgroundShade is < 0 or > 100 || !Enum.IsDefined(ImageFit))
            throw new InvalidDataException("背景缩放、偏移或旋转超出范围。");
        ValidateColor(BackgroundColor);
        foreach (var item in Elements)
        {
            if (item is null || !Enum.IsDefined(item.Metric) || !Enum.IsDefined(item.Alignment)
                || item.X is < 0 or > 719 || item.Y is < 0 or > 719 || item.Width < MinimumElementSize || item.Width > 720 || item.Height < MinimumElementSize || item.Height > 720
                || item.X + item.Width > 720 || item.Y + item.Height > 720 || item.FontSize < MinimumFontSize || item.FontSize > MaximumFontSize
                || item.TitleFontSize < 0 || item.TitleFontSize > MaximumFontSize || item.UnitFontSize < 0 || item.UnitFontSize > MaximumFontSize
                || string.IsNullOrWhiteSpace(item.FontFamily) || item.TitleFontFamily is null || item.UnitFontFamily is null
                || item.Title is null || item.Caption is null || item.Text is null || item.Format is null || item.SensorId is null)
                throw new InvalidDataException("布局项目的位置、字体或内容无效。");
            ValidateColor(item.Color); ValidateColor(item.TitleColor);
            if (item.NumericFormat is null || item.CustomUnit is null || item.CaptionFontFamily is null || item.Clock is null
                || item.DecimalPlaces is < -1 or > 6 || item.CaptionFontSize < MinimumFontSize || item.CaptionFontSize > MaximumFontSize
                || item.CaptionHeight is < 0 or > 720 || item.TemperatureUnit.HasValue && !Enum.IsDefined(item.TemperatureUnit.Value))
                throw new InvalidDataException("数值格式、说明文字或时钟设置无效。");
            ValidateColor(item.CaptionColor); item.Clock.Validate();
            if (item.NumericFormat.Length > 0)
                try { _ = 1.25.ToString(item.NumericFormat); } catch (FormatException error) { throw new InvalidDataException("数值格式无效。", error); }
            if (item.Metric is ScreenMetric.Time or ScreenMetric.Date && item.Format.Length > 0)
                try { _ = DateTime.Now.ToString(item.Format); } catch (FormatException error) { throw new InvalidDataException("时间或日期格式无效。", error); }
        }
    }

    public static void ValidateColor(string value)
    {
        if (value is null || value.Length != 7 || value[0] != '#'
            || !int.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
            throw new InvalidDataException("颜色应为 #RRGGBB。");
    }
}
