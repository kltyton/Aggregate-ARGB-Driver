namespace HardwareLite.Configuration;

public enum AudioCaptureSource { OutputDevice, InputDevice, Application }
public enum SpectrumWindow { Hann, Hamming, Rectangular }
public enum SpectrumAggregation { Peak, Average }
public enum EffectWaveform { Cosine, Triangle, Sawtooth, Square }
public enum TailCurve { Linear, Smooth, Quadratic }
[Flags] public enum InputTriggers { Buttons = 1, MouseMove = 2, MouseWheel = 4 }

public sealed record LightingOptions
{
    public int DynamicIntervalMs { get; init; } = 100;
    public int MonitorIntervalMs { get; init; } = 1000;
    public Dictionary<int, LightingGroupProfile> Groups { get; init; } = [];
    public MissingDataPolicy ExpiredData { get; init; } = MissingDataPolicy.Retry;
    public DeviceStopBehavior OnStop { get; init; }
    public void Validate()
    {
        if (DynamicIntervalMs is < 10 or > 10000 || MonitorIntervalMs is < 10 or > 10000
            || Groups is null || !Enum.IsDefined(ExpiredData) || !Enum.IsDefined(OnStop))
            throw new InvalidDataException("灯光更新间隔或停用策略无效。");
        foreach (var group in Groups)
        {
            if (group.Key is < 0 or > 4 || group.Value is null) throw new InvalidDataException("灯光分组配置无效。");
            group.Value.Validate();
        }
    }
}
public sealed record LightingGroupProfile
{
    public LightingMode Mode { get; init; }
    public int Brightness { get; init; } = 100;
    public Dictionary<LightingMode, LightingParameters> Profiles { get; init; } = [];
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || Brightness is < 0 or > 100 || Profiles is null)
            throw new InvalidDataException("分组灯效或亮度无效。");
        foreach (var pair in Profiles)
        {
            if (!Enum.IsDefined(pair.Key) || pair.Value is null) throw new InvalidDataException("分组灯效参数无效。");
            pair.Value.Validate();
        }
    }
}
public sealed record AudioOptions
{
    public AudioCaptureSource Source { get; init; }
    public string DeviceId { get; init; } = "";
    public string ApplicationName { get; init; } = "";
    public bool ExcludeApplication { get; init; }
    public int SampleRate { get; init; } = 16000;
    public int FftSize { get; init; } = 1024;
    public int Bands { get; init; } = 32;
    public int MinimumHz { get; init; } = 15;
    public int MaximumHz { get; init; } = 8000;
    public int LowMidHz { get; init; } = 109;
    public int MidHighHz { get; init; } = 940;
    public bool UseFrequencyBoundaries { get; init; }
    public SpectrumWindow Window { get; init; }
    public SpectrumAggregation Aggregation { get; init; }
    public double AnalysisGain { get; init; } = 15;
    public int SilenceMs { get; init; } = 300;
    public int AttackMs { get; init; }
    public int DecayMs { get; init; }
    public void Validate()
    {
        if (!Enum.IsDefined(Source) || DeviceId is null || ApplicationName is null
            || Source == AudioCaptureSource.Application && ApplicationName.Length == 0
            || SampleRate is < 8000 or > 96000 || FftSize is < 128 or > 8192 || (FftSize & (FftSize - 1)) != 0
            || Bands is < 3 or > 128 || MinimumHz < 1 || MaximumHz <= MinimumHz || MaximumHz > SampleRate / 2
            || LowMidHz < MinimumHz || MidHighHz <= LowMidHz || MidHighHz > MaximumHz
            || !Enum.IsDefined(Window) || !Enum.IsDefined(Aggregation) || !double.IsFinite(AnalysisGain) || AnalysisGain is < 0.01 or > 100
            || SilenceMs is < 10 or > 10000 || AttackMs is < 0 or > 10000 || DecayMs is < 0 or > 10000)
            throw new InvalidDataException("音乐采集设备、频率、FFT 或响应设置无效。");
    }
}
