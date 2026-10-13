using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HardwareLite.Configuration;
using HardwareLite.Devices.Asus;
using HardwareLite.Devices.DeepCool;
using HardwareLite.Devices.Maxsun;
using HardwareLite.Display;
using HardwareLite.Monitoring;
using Microsoft.Win32;

namespace HardwareLite.Runtime;

internal sealed class DeviceController(Func<Settings> settings, SensorSampler sampler)
{
    private string _lighting = "已暂停", _screen = "已暂停", _case = "已暂停";
    private CancellationTokenSource? _cancellation;
    private Task[] _tasks = [];
    private Task? _stopping;
    private readonly TaskCompletionSource<Exception> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<Exception> Failure => _failure.Task;
    private long _resumeAt;
    private readonly DeviceConflicts _conflicts = new();
    private PrimeSlcScreen? _screenDevice;
    private FirmwarePackage? _pendingFirmware;
    private FirmwareUpdateStatus _firmware = FirmwareUpdateStatus.Idle;
    public FirmwareUpdateStatus Firmware => Volatile.Read(ref _firmware);
    public string ScreenFirmwareVersion => Volatile.Read(ref _screenDevice) is { IsDisposed: false } device ? device.FirmwareVersion : "";
    public int? ScreenFirmwareImageVariant => Volatile.Read(ref _screenDevice) is { IsDisposed: false } device ? device.FirmwareImageVariant : null;
    public long? ScreenMediaCapacityBytes => Volatile.Read(ref _screenDevice) is { IsDisposed: false } device ? device.MediaCapacityBytes : null;
    public bool ScreenNativeVideoSupported => Volatile.Read(ref _screenDevice) is { IsDisposed: false } device && device.NativeVideoSupported;
    public void RequestFirmware(string path, string hash)
    {
        if (Firmware.Running) throw new InvalidOperationException("固件更新已经在进行中。");
        if (!Connected || !settings().Screen.Enabled || Volatile.Read(ref _screenDevice) is not { IsDisposed: false, FirmwareVersion.Length: > 0 })
            throw new InvalidOperationException("请先连接正常运行的水冷屏幕。");
        int variant = ScreenFirmwareImageVariant ?? throw new InvalidOperationException("设备尚未返回固件平台标志。");
        var package = FirmwarePackage.Load(path, variant);
        if (!string.Equals(package.Sha256, hash, StringComparison.Ordinal)) throw new InvalidDataException("固件文件已改变，请重新选择并确认。");
        Volatile.Write(ref _firmware, new(true, 0, "等待设备开始更新"));
        Volatile.Write(ref _pendingFirmware, package);
    }
    public bool Connected => Volatile.Read(ref _cancellation) is not null;
    public DeviceStatus Status => new(Volatile.Read(ref _lighting),
        Volatile.Read(ref _screenDevice) is { IsDisposed: false, UploadingMedia: true } device ? device.Status : Volatile.Read(ref _screen),
        Volatile.Read(ref _case));

    public void Start()
    {
        if (_cancellation is not null) return;
        _conflicts.Refresh();
        _cancellation = new CancellationTokenSource();
        SystemEvents.PowerModeChanged += PowerChanged;
        _stopping = null;
        var cancellation = _cancellation.Token;
        _tasks =
        [
            Task.Run(async () =>
            {
                try
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        await Task.Delay(settings().Runtime.ConflictCheckMs, cancellation).ConfigureAwait(false);
                        _conflicts.Refresh();
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }, cancellation),
            Task.Run(() => RunDevice(() => new EneLighting(), config => config.Lighting.Enabled,
                ["MaxsunSync2", "MaxsunSyncService"], value => Volatile.Write(ref _lighting, value),
                async (device, clock, token) =>
                {
                    var config = settings().Lighting;
                    device.Update(config, sampler.Snapshot, clock.ElapsedMilliseconds);
                    bool dynamic = config.Mode is not (LightingMode.Static or LightingMode.Off or LightingMode.Smart) || config.Options.Groups.Values.Any(group => group.Mode is not (LightingMode.Static or LightingMode.Off or LightingMode.Smart));
                    await Task.Delay(dynamic ? config.Options.DynamicIntervalMs : config.Options.MonitorIntervalMs, token);
                    return "已连接";
                }, cancellation, stop: device => { if (settings().Lighting.Options.OnStop == DeviceStopBehavior.TurnOff) device.TurnOff(); }, resumeDelay: config => config.Runtime.LightingResumeDelayMs)),
            Task.Run(() => RunDevice(() => new Ch270Display(), config => config.Case.Enabled,
                ["CH270", "CH270.Service"], value => Volatile.Write(ref _case, value),
                async (device, clock, token) =>
                {
                    var config = settings();
                    bool ready = device.Update(sampler.Snapshot, config, clock.ElapsedMilliseconds);
                    await Task.Delay(config.Case.Options.UpdateIntervalMs == 0 ? config.SampleIntervalMs : config.Case.Options.UpdateIntervalMs, token);
                    return ready ? "已连接，共享监控数据" : "已连接，等待完整监控数据";
                }, cancellation, stop: device => { if (settings().Case.Options.OnStop == DeviceStopBehavior.TurnOff) device.TurnOff(); }, resumeDelay: config => config.Runtime.CaseResumeDelayMs)),
            Task.Run(() => RunScreen(cancellation))
        ];
        foreach (var task in _tasks)
            _ = task.ContinueWith(failed => _failure.TrySetResult(failed.Exception!.GetBaseException()), CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume) Volatile.Write(ref _resumeAt, Stopwatch.GetTimestamp());
    }

    private bool WaitingForResume(int delay)
    {
        long resumed = Volatile.Read(ref _resumeAt);
        return resumed != 0 && Stopwatch.GetElapsedTime(resumed).TotalMilliseconds < delay;
    }

    private async Task RunScreen(CancellationToken cancellation)
    {
        await RunDevice(() => { var device = new PrimeSlcScreen(); Volatile.Write(ref _screenDevice, device); return device; }, config => config.Screen.Enabled,
            ["ASUS_InfoHub"], value => Volatile.Write(ref _screen, value),
            async (device, clock, token) =>
            {
                if (Interlocked.Exchange(ref _pendingFirmware, null) is { } package)
                {
                    try
                    {
                        device.UpdateFirmware(package, value => Volatile.Write(ref _firmware, new(true, value, "设备固件更新中")));
                        Volatile.Write(ref _firmware, new(false, 100, "设备已确认更新完成"));
                    }
                    catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
                    { Volatile.Write(ref _firmware, new(false, 0, "更新失败：" + error.Message)); throw; }
                }
                var config = settings();
                long started = Stopwatch.GetTimestamp();
                device.Update(config, sampler.Snapshot, clock.ElapsedMilliseconds);
                double intervalMs = config.Screen.Options.DevicePlayback || config.Screen.Mode == ScreenMode.Monitor
                    ? Math.Min(config.SampleIntervalMs, 1000) : 1000d / config.Screen.MaxFps;
                double remainingMs = intervalMs - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (remainingMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remainingMs)), token);
                return device.Status;
            }, cancellation, () =>
            {
                Volatile.Write(ref _screenDevice, null);
                if (Interlocked.Exchange(ref _pendingFirmware, null) is not null)
                    Volatile.Write(ref _firmware, new(false, 0, "连接已释放，未开始更新"));
            }, stop: device => { if (settings().Screen.Options.OnStop == DeviceStopBehavior.TurnOff) device.TurnOff(settings().Screen); }, resumeDelay: config => config.Runtime.ScreenResumeDelayMs).ConfigureAwait(false);
    }

    private async Task RunDevice<T>(Func<T> open, Func<Settings, bool> enabled, string[] conflicts,
        Action<string> status, Func<T, Stopwatch, CancellationToken, Task<string>> update, CancellationToken cancellation, Action? released = null, Action<T>? stop = null, Func<Settings, int>? resumeDelay = null) where T : IDisposable
    {
        var clock = Stopwatch.StartNew();
        bool Resuming() { var config = settings(); int delay = resumeDelay?.Invoke(config) ?? -1; return WaitingForResume(delay < 0 ? config.ResumeDelayMs : delay); }
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (!enabled(settings())) { status("未启用"); await Task.Delay(settings().Runtime.DisabledPollMs, cancellation); continue; }
                if (Resuming()) { status("等待睡眠唤醒延迟"); await Task.Delay(settings().Runtime.ResumePollMs, cancellation); continue; }
                string? conflict = _conflicts.Find(conflicts);
                if (conflict is not null) { status(conflict); await Task.Delay(settings().Runtime.ConflictRetryMs, cancellation); continue; }
                try
                {
                    using T device = open();
                    status("已连接");
                    try { while (enabled(settings()) && !Resuming() && !cancellation.IsCancellationRequested)
                    {
                        conflict = _conflicts.Find(conflicts);
                        if (conflict is not null) { status(conflict); break; }
                        status(await update(device, clock, cancellation).ConfigureAwait(false));
                    } }
                    finally { if (!enabled(settings()) || cancellation.IsCancellationRequested) stop?.Invoke(device); }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException or ExternalException or ArgumentException or InvalidOperationException)
                {
                    status("连接失败：" + error.Message);
                    await Task.Delay(settings().Runtime.RetryMs, cancellation).ConfigureAwait(false);
                }
                finally { released?.Invoke(); }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { status("已暂停"); }
    }

    public Task StopAsync() => _stopping ??= StopCore();

    private async Task StopCore()
    {
        if (_cancellation is null) return;
        var cancellation = _cancellation;
        cancellation.Cancel();
        SystemEvents.PowerModeChanged -= PowerChanged;
        try { await Task.WhenAll(_tasks); }
        finally { cancellation.Dispose(); _cancellation = null; _tasks = []; }
    }
}
