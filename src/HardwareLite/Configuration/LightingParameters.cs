using System.ComponentModel;

namespace HardwareLite.Configuration;

public enum SmartSource { CpuTemperature, CpuUsage, GpuTemperature, GpuUsage }
public enum AudioMode { TriBand, Spectrum, PeakWave }
public enum InputSource { Keyboard, Mouse, Both }

public sealed record LightingParameters
{
    [DisplayName("前景颜色 #RRGGBB")] public string Color { get; init; } = "#39C5BB";
    [DisplayName("背景颜色 #RRGGBB")] public string Background { get; init; } = "#000000";
    [DisplayName("速度（1–100）")] public int Speed { get; init; } = 50;
    [DisplayName("亮度百分比")] public int Brightness { get; init; } = 100;
    [DisplayName("灯珠方向反转")] public bool Reverse { get; init; }
    [DisplayName("色带方向反转")] public bool ColorReverse { get; init; }
    [DisplayName("尾长百分比")] public int TailLength { get; init; } = 30;
    [DisplayName("色带预设（0–3）")] public int Palette { get; init; }
    [DisplayName("段大小（8/16/32）")] public int SegmentSize { get; init; } = 8;
    [DisplayName("星空随机颜色")] public bool RandomColor { get; init; }
    [DisplayName("关闭星空背景")] public bool BackgroundOff { get; init; }
    [DisplayName("智能监控来源")] public SmartSource SmartSource { get; init; }
    [DisplayName("智能下限")] public int Minimum { get; init; } = 30;
    [DisplayName("智能上限")] public int Maximum { get; init; } = 80;
    [DisplayName("音乐模式")] public AudioMode AudioMode { get; init; }
    [DisplayName("音乐增益百分比")] public int AudioBoost { get; init; } = 100;
    [DisplayName("低频颜色")] public string LowColor { get; init; } = "#FF0000";
    [DisplayName("中频颜色")] public string MidColor { get; init; } = "#00FF00";
    [DisplayName("高频颜色")] public string HighColor { get; init; } = "#0000FF";
    [DisplayName("输入联动来源")] public InputSource InputSource { get; init; } = InputSource.Both;
    public string[] Gradient { get; init; } = [];
    public EffectWaveform Waveform { get; init; }
    public TailCurve TailCurve { get; init; }
    public double StarDensity { get; init; } = 1;
    public int StarSharpness { get; init; } = 12;
    public int StarSeed { get; init; }
    public InputTriggers Triggers { get; init; } = InputTriggers.Buttons;
    public int[] TriggerKeys { get; init; } = [];
    public bool TriggerColorCycle { get; init; }

    public void Validate()
    {
        foreach (string color in new[] { Color, Background, LowColor, MidColor, HighColor }) ScreenLayout.ValidateColor(color);
        if (Gradient is null || Gradient.Length > 32 || !Enum.IsDefined(Waveform) || !Enum.IsDefined(TailCurve)
            || !double.IsFinite(StarDensity) || StarDensity is < 0.01 or > 1 || StarSharpness is < 1 or > 64
            || (Triggers & ~(InputTriggers.Buttons | InputTriggers.MouseMove | InputTriggers.MouseWheel)) != 0 || Triggers == 0
            || TriggerKeys is null || TriggerKeys.Any(key => key is < 1 or > 255))
            throw new InvalidDataException("自定义色带、曲线、星空或输入设置无效。");
        foreach (string color in Gradient) ScreenLayout.ValidateColor(color);
        if (Brightness is < 0 or > 100 || Speed is < 1 or > 100 || TailLength is < 1 or > 100 || Palette is < 0 or > 3 || SegmentSize is not (8 or 16 or 32)
            || Minimum < 0 || Maximum <= Minimum || Maximum > 1000 || AudioBoost is < 1 or > 1000
            || !Enum.IsDefined(SmartSource) || !Enum.IsDefined(AudioMode) || !Enum.IsDefined(InputSource))
            throw new InvalidDataException("灯效速度、范围、段大小或增益无效。");
    }
}
