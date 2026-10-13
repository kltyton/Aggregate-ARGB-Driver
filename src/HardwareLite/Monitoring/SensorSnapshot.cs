namespace HardwareLite.Monitoring;

public sealed record SensorOption(string Id, string Name);
public sealed record SensorReading(string Name, float? Value, string Unit);

public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    float? CpuTemperature,
    float? CpuPower,
    float? CpuUsage,
    float? CpuClock,
    float? CpuFan,
    float? GpuTemperature,
    float? GpuPower,
    float? GpuUsage,
    float? GpuClock,
    float? SystemTemperature,
    string Status)
{
    public IReadOnlyDictionary<string, SensorReading> Sensors { get; init; } = new Dictionary<string, SensorReading>();
    public float? CpuVoltage { get; init; }
    public float? GpuVoltage { get; init; }
    public float? MemoryClock { get; init; }
    public float? AioPump { get; init; }
    public float? SystemFan { get; init; }
    public static SensorSnapshot Empty { get; } = new(DateTimeOffset.MinValue, null, null, null, null, null, null, null, null, null, null, "等待采样");
    public int ExpiryMs { get; init; } = 15000;
    public bool IsFresh => DateTimeOffset.UtcNow - Timestamp < TimeSpan.FromMilliseconds(ExpiryMs);
}
