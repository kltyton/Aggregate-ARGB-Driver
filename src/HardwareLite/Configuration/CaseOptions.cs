namespace HardwareLite.Configuration;
public enum CaseSubPage { CpuFrequency = 2, CpuFan = 3, Gpu = 4 }
public sealed record CaseOptions
{
    public CaseSubPage[] CyclePages { get; init; } = [];
    public Dictionary<CaseSubPage, int> PageDurationsMs { get; init; } = [];
    public int UpdateIntervalMs { get; init; }
    public string FanSensorId { get; init; } = "";
    public TemperatureUnit? TemperatureUnit { get; init; }
    public MissingDataPolicy MissingData { get; init; } = MissingDataPolicy.Off;
    public DeviceStopBehavior OnStop { get; init; }
    public void Validate()
    {
        if (CyclePages is null || CyclePages.Length > 3 || CyclePages.Any(page => !Enum.IsDefined(page))
            || CyclePages.Distinct().Count() != CyclePages.Length || PageDurationsMs is null
            || PageDurationsMs.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is < 100 or > 60000)
            || UpdateIntervalMs != 0 && UpdateIntervalMs is < 100 or > 10000
            || FanSensorId is null || TemperatureUnit.HasValue && !Enum.IsDefined(TemperatureUnit.Value)
            || !Enum.IsDefined(MissingData) || !Enum.IsDefined(OnStop))
            throw new InvalidDataException("机箱轮播页面、停留时间或数据策略无效。");
    }
}

