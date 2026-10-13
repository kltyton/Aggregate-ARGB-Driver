using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareLite.Configuration;
using HardwareLite.Monitoring;
using HardwareLite.Runtime;

namespace HardwareLite.Communication;

public sealed record BackendRequest(string Command, Settings? Settings = null, bool? Autostart = null, string? FirmwarePath = null, string? FirmwareSha256 = null);
public sealed record BackendState(bool Connected, bool Autostart, DeviceStatus Devices, SensorSnapshot Snapshot,
    SensorOption[] Gpus, SensorOption[] Fans, SensorOption[] BoardTemperatures, string Error = "")
{
    public static BackendState Offline { get; } = new(false, false, DeviceStatus.Paused, SensorSnapshot.Empty, [], [], [], "后台未运行");
    public string FirmwareVersion { get; init; } = "";
    public int? FirmwareImageVariant { get; init; }
    public FirmwareUpdateStatus Firmware { get; init; } = FirmwareUpdateStatus.Idle;
    public long? DeviceMediaCapacityBytes { get; init; }
    public bool DeviceNativeVideoSupported { get; init; }
}

public static class BackendProtocol
{
    private const int MaxMessageBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
    public static string InstanceId { get; } = WindowsIdentity.GetCurrent().User!.Value + "." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant())))[..16];
    public static string PipeName => "HardwareLite.Service." + InstanceId;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellation)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length > MaxMessageBytes) throw new InvalidDataException("本机消息超过长度限制。");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellation)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes) throw new InvalidDataException("本机消息长度无效。");
        byte[] bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("本机消息为空。");
    }
}
