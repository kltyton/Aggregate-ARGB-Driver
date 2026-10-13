namespace HardwareLite.Runtime;

public sealed record DeviceStatus(string Lighting, string Screen, string Case)
{
    public static DeviceStatus Paused { get; } = new("已暂停", "已暂停", "已暂停");
}
