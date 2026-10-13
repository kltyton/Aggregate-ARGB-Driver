namespace HardwareLite.Monitoring;

public interface ISensorSource
{
    SensorSnapshot Snapshot { get; }
    IReadOnlyList<SensorOption> Gpus { get; }
    IReadOnlyList<SensorOption> Fans { get; }
    IReadOnlyList<SensorOption> BoardTemperatures { get; }
}
