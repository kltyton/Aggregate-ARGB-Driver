using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using HardwareLite.Communication;
using HardwareLite.Configuration;
using HardwareLite.Monitoring;
using HardwareLite.Runtime;

namespace HardwareLite.Service.Runtime;

internal sealed class ServiceHost : IDisposable
{
    private readonly PipeSecurity _pipeSecurity = CreatePipeSecurity();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SettingsStore _store = new(Path.Combine(AppContext.BaseDirectory, "config"));
    private readonly SensorSampler _sampler = new();
    private readonly DeviceController _devices;
    private Settings _settings;
    private long _guiLease;
    private string _monitorError = "";
    private bool _autostart;
    public ServiceHost()
    {
        _settings = _store.Load();
        if (_store.HasPendingMigration) _store.Save(_settings);
        _autostart = Autostart.Enabled;
        if (_store.ImportedAutostart is bool enabled) { Autostart.SetEnabled(enabled); _autostart = enabled; }
        else if (_autostart) Autostart.SetEnabled(true);
        _devices = new DeviceController(() => Volatile.Read(ref _settings), _sampler);
    }
    public async Task RunAsync()
    {
        _devices.Start();
        Task monitor = Task.Run(MonitorAsync);
        Task server = ServeAsync(_shutdown.Token);
        try
        {
            if (await Task.WhenAny(server, _devices.Failure) == _devices.Failure)
                throw new InvalidOperationException("设备控制任务失败。", await _devices.Failure);
            await server;
        }
        finally
        {
            _shutdown.Cancel();
            await server;
            try { await _devices.StopAsync(); }
            finally { await monitor; }
        }
    }
    private bool NeedsSensors()
    {
        var settings = Volatile.Read(ref _settings);
        return Stopwatch.GetTimestamp() < Volatile.Read(ref _guiLease) || _devices.Connected &&
            (settings.Case.Enabled && settings.Case.Page != CasePage.Off || settings.Screen.Enabled && settings.Screen.Mode != ScreenMode.Media ||
              settings.Lighting.Enabled && (settings.Lighting.Mode == LightingMode.Smart || settings.Lighting.Options.Groups.Values.Any(group => group.Mode == LightingMode.Smart)));
    }
    private async Task MonitorAsync()
    {
        try { await _sampler.RunAsync(() => Volatile.Read(ref _settings), NeedsSensors, _shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error)
        {
            Volatile.Write(ref _monitorError, "监控失败：" + error.GetBaseException().Message);
            _sampler.ReportFailure(Volatile.Read(ref _monitorError));
            string directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "monitor-error.txt"), error.ToString());
        }
    }
    private static PipeSecurity CreatePipeSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetOwner(identity.User!);
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
    private BackendState State(string error = "") => new(_devices.Connected, _autostart, _devices.Status, _sampler.Snapshot,
        [.. _sampler.Gpus], [.. _sampler.Fans], [.. _sampler.BoardTemperatures], error)
        { FirmwareVersion = _devices.ScreenFirmwareVersion, FirmwareImageVariant = _devices.ScreenFirmwareImageVariant, Firmware = _devices.Firmware,
            DeviceMediaCapacityBytes = _devices.ScreenMediaCapacityBytes, DeviceNativeVideoSupported = _devices.ScreenNativeVideoSupported };
    private async Task ServeAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            using var pipe = NamedPipeServerStreamAcl.Create(BackendProtocol.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, _pipeSecurity);
            try
            {
                await pipe.WaitForConnectionAsync(cancellation);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await BackendProtocol.ReadAsync<BackendRequest>(pipe, deadline.Token);
                string error = "";
                try
                {
                    switch (request.Command)
                    {
                        case "status": Volatile.Write(ref _guiLease, Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency); break;
                        case "apply":
                            if (_devices.Firmware.Running) throw new InvalidOperationException("固件更新期间请等待完成后再改变设备设置。");
                            var settings = (request.Settings ?? throw new InvalidDataException("缺少设备配置。")) with { Version = 2 };
                            _store.Save(settings); Volatile.Write(ref _settings, settings); break;
                        case "toggle":
                            if (_devices.Firmware.Running) throw new InvalidOperationException("固件更新进行中，请等待完成后再暂停。");
                            if (_devices.Connected) await _devices.StopAsync(); else _devices.Start(); break;
                        case "autostart":
                            bool enabled = request.Autostart ?? throw new InvalidDataException("缺少自启状态。");
                            Autostart.SetEnabled(enabled); _autostart = enabled; break;
                        case "firmware":
                            _devices.RequestFirmware(request.FirmwarePath ?? throw new InvalidDataException("缺少固件路径。"),
                                request.FirmwareSha256 ?? throw new InvalidDataException("缺少固件确认信息。")); break;
                        case "stop":
                            if (_devices.Firmware.Running) throw new InvalidOperationException("固件更新进行中，请等待完成后再退出后台。");
                            break;
                        default: throw new InvalidDataException("未知后台命令。");
                    }
                }
                catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                { error = failure.GetBaseException().Message; }
                await BackendProtocol.WriteAsync(pipe, State(error), deadline.Token);
                if (request.Command == "stop" && error.Length == 0) _shutdown.Cancel();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException)
            {
                if (error is not EndOfStreamException && error is not OperationCanceledException)
                {
                    string directory = Path.Combine(AppContext.BaseDirectory, "logs");
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "communication-error.txt"), error.ToString());
                }
            }
        }
    }
    public void Dispose() => _shutdown.Dispose();
}
