namespace HardwareLite.Configuration;

public enum CpuClockMethod { Average, Maximum, Minimum, SelectedCore }
public sealed record MonitorOptions
{
    public bool Cpu { get; init; } = true;
    public bool Gpu { get; init; } = true;
    public bool Motherboard { get; init; } = true;
    public bool Memory { get; init; } = true;
    public bool Storage { get; init; }
    public bool Network { get; init; }
    public bool Controller { get; init; }
    public int ExpiryMs { get; init; } = 15000;
    public CpuClockMethod CpuClockMethod { get; init; }
    public string CpuClockSensorId { get; init; } = "";
    public Dictionary<ScreenMetric, string> Sources { get; init; } = [];
    public void Validate()
    {
        if (ExpiryMs is < 100 or > 300000 || !Enum.IsDefined(CpuClockMethod) || CpuClockSensorId is null
            || Sources is null || Sources.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null)
            || CpuClockMethod == CpuClockMethod.SelectedCore && CpuClockSensorId.Length == 0)
            throw new InvalidDataException("监控来源、频率统计或过期时间无效。");
    }
}
public sealed record RuntimeOptions
{
    public int RetryMs { get; init; } = 3000;
    public int ConflictRetryMs { get; init; } = 3000;
    public int ConflictCheckMs { get; init; } = 1000;
    public int DisabledPollMs { get; init; } = 1000;
    public int ResumePollMs { get; init; } = 1000;
    public int ScreenResumeDelayMs { get; init; } = -1;
    public int LightingResumeDelayMs { get; init; } = -1;
    public int CaseResumeDelayMs { get; init; } = -1;
    public void Validate()
    {
        if (RetryMs is < 100 or > 60000 || ConflictRetryMs is < 100 or > 60000
            || ConflictCheckMs is < 100 or > 60000 || DisabledPollMs is < 100 or > 60000 || ResumePollMs is < 100 or > 60000
            || ScreenResumeDelayMs is < -1 or > 60000 || LightingResumeDelayMs is < -1 or > 60000 || CaseResumeDelayMs is < -1 or > 60000)
            throw new InvalidDataException("后台重连或轮询间隔无效。");
    }
}
