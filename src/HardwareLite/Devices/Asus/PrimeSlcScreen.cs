using System.Drawing.Imaging;
using HardwareLite.Configuration;
using HardwareLite.Display;
using HardwareLite.Display.Media;
using HardwareLite.Monitoring;

namespace HardwareLite.Devices.Asus;

internal sealed class PrimeSlcScreen : IDisposable
{
    private readonly PrimeSlcDisplay _display = new();
    private ScreenCanvas? _canvas;
    private DevicePlaybackProtocol? _native;
    private DeviceScreenOverlay? _overlay;
    private DeviceTextOverlay? _textOverlay;
    private ScreenSettings? _nativeSettings;
    private bool _nativeSleeping;
    private long _mediaSlot = -1, _lastNativeStatus;
    private int _track;
    private string[] _shuffleFiles = [];
    private int[] _shuffleOrder = [];
    private int _shufflePosition;
    private Bitmap? _frame;
    private bool _startupHeapReleased;
    public string Status => _display.Status + (_canvas is { VideoDecoderStatus.Length: > 0 } canvas ? " · " + canvas.VideoDecoderStatus : "");
    public string FirmwareVersion => _display.FirmwareVersion;
    public int? FirmwareImageVariant => _display.FirmwareImageVariant;
    public long? MediaCapacityBytes { get; private set; }
    public bool NativeVideoSupported => _native is { ApiVersion: >= 5 } && (_native.Features & 16) != 0;
    public PrimeSlcScreen()
    {
        try
        {
            if (!_display.IsCustomFirmware) return;
            _native = _display.DevicePlayback();
            if (_native.ApiVersion >= 4) MediaCapacityBytes = _native.MediaCapacityBytes();
        }
        catch { _display.Dispose(); _canvas?.Dispose(); throw; }
    }
    public bool IsDisposed { get; private set; }
    public bool UploadingMedia { get; private set; }
    public void TurnOff(ScreenSettings settings)
    {
        if (_nativeSettings is not null) { _native!.Configure(settings with { Brightness = 0 }); _nativeSleeping = true; return; }
        _frame ??= new Bitmap(720, 720, PixelFormat.Format24bppRgb);
        _display.Show(_frame, settings with { Brightness = 0 });
    }
    public void UpdateFirmware(FirmwarePackage package, Action<int> progress)
    {
        if (_nativeSettings is not null) { _native!.Stop(); _native.ClearSprites(); _nativeSettings = null; _overlay?.Dispose(); _overlay = null; }
        _display.UpdateFirmware(package, progress);
        _native = null;
        MediaCapacityBytes = null;
        if (_display.IsCustomFirmware)
        {
            _native = _display.DevicePlayback();
            if (_native.ApiVersion >= 4) MediaCapacityBytes = _native.MediaCapacityBytes();
        }
        _canvas?.Invalidate();
    }
    public void Update(Settings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        if (settings.Screen.Options.DevicePlayback)
        {
            UpdateDevicePlayback(settings, snapshot, elapsedMs);
            ReleaseStartupHeap(settings.Screen, snapshot);
            return;
        }
        if (_nativeSettings is not null)
        {
            _native!.Stop(); _native.ClearSprites(); _overlay?.Dispose(); _overlay = null; _nativeSettings = null;
        }
        _canvas ??= new ScreenCanvas(deviceOutput: true);
        if (_canvas.IsDue(settings, snapshot, elapsedMs))
        {
            _frame ??= new Bitmap(720, 720, PixelFormat.Format24bppRgb);
            _canvas.RenderInto(_frame, settings, snapshot, elapsedMs);
            _display.Show(_frame, settings.Screen);
            ReleaseStartupHeap(settings.Screen, snapshot);
        }
        else _display.Maintain(settings.Screen.Options.KeepaliveMs);
    }
    private void ReleaseStartupHeap(ScreenSettings settings, SensorSnapshot snapshot)
    {
        if (_startupHeapReleased || !snapshot.IsFresh && settings.Mode != ScreenMode.Media) return;
        _startupHeapReleased = true;
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
    private void UpdateDevicePlayback(Settings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        var screen = settings.Screen;
        _native ??= _display.DevicePlayback();
        if (_native.ApiVersion < 4 || (_native.Features & 8) == 0)
            throw new InvalidOperationException("当前固件不支持无损设备媒体，请先更新固件。");
        if (_nativeSleeping) { _native.Configure(screen); _nativeSleeping = false; }
        if (!ReferenceEquals(screen, _nativeSettings))
        {
            DeviceMediaPreparation.ValidatePrepared(screen);
            if (DeviceMediaPreparation.IsNativeVideo(screen) && !NativeVideoSupported)
                throw new InvalidOperationException("当前固件不支持压缩设备视频，请先更新固件或选择无损画面编码。");
            bool restart = _nativeSettings is null || (_nativeSettings.Mode == ScreenMode.Monitor) != (screen.Mode == ScreenMode.Monitor)
                || _nativeSettings.DeviceMediaPath != screen.DeviceMediaPath;
            if (restart)
            {
                if (screen.Mode != ScreenMode.Monitor && !_native.HasMedia(screen.DeviceMediaPath))
                {
                    UploadingMedia = true;
                    try { _native.Upload(screen.DeviceMediaPath, _display.SetMediaUploadStatus); }
                    finally { UploadingMedia = false; }
                }
                _native.Play(screen); _native.EnableOverlay(); _native.ClearSprites();
                if (screen.Mode != ScreenMode.Monitor && screen.Playback == MediaPlayback.Single)
                    _native.SelectTrack(0, elapsedMs);
                _mediaSlot = -1; _track = 0;
                _overlay?.Dispose(); _overlay = null;
                _canvas?.Dispose(); _canvas = null; _frame?.Dispose(); _frame = null;
            }
            else _native.Configure(screen);
            if (restart) _textOverlay = null;
            else if (_textOverlay is not null && !_textOverlay.Reconfigure(screen))
                _textOverlay = new DeviceTextOverlay(_native, screen, _textOverlay);
            _nativeSettings = screen;
            _display.SetDevicePlaybackStatus();
            MediaCapacityBytes = _native.MediaCapacityBytes();
        }
        if (screen.Mode != ScreenMode.Monitor && screen.Playback != MediaPlayback.Single)
        {
            long slot = elapsedMs / (screen.MediaIntervalSeconds * 1000L);
            if (slot != _mediaSlot)
            {
                _mediaSlot = slot;
                if (screen.Playback != MediaPlayback.Shuffle) _track = (int)(slot % screen.MediaFiles.Length);
                else if (!screen.Options.ShuffleAvoidRepeats) _track = Random.Shared.Next(screen.MediaFiles.Length);
                else
                {
                    if (!_shuffleFiles.SequenceEqual(screen.MediaFiles) || _shufflePosition >= _shuffleOrder.Length)
                    {
                        _shuffleFiles = [.. screen.MediaFiles];
                        _shuffleOrder = Enumerable.Range(0, screen.MediaFiles.Length).ToArray(); _shufflePosition = 0;
                        Random.Shared.Shuffle(_shuffleOrder);
                        if (_shuffleOrder.Length > 1 && _shuffleOrder[0] == _track)
                            (_shuffleOrder[0], _shuffleOrder[1]) = (_shuffleOrder[1], _shuffleOrder[0]);
                    }
                    _track = _shuffleOrder[_shufflePosition++];
                }
                _native.SelectTrack(_track, elapsedMs);
            }
        }
        if (screen.Mode != ScreenMode.Monitor && elapsedMs - _lastNativeStatus >= 1000)
        {
            _lastNativeStatus = elapsedMs;
            if (!_native.IsPlaying()) throw new IOException("设备内动画已中断，请检查媒体和设备连接。");
        }
        if (screen.Mode != ScreenMode.Media)
        {
            if (_native.ApiVersion >= 6 && (_native.Features & 32) != 0)
            {
                _overlay?.Dispose(); _overlay = null;
                _textOverlay ??= new DeviceTextOverlay(_native, screen);
                _textOverlay.Update(_native, settings, snapshot, elapsedMs);
            }
            else
            {
                _overlay ??= new DeviceScreenOverlay();
                _overlay.Update(_native, settings, snapshot, elapsedMs);
            }
        }
        else if (_overlay is not null || _textOverlay is not null)
        {
            _native.RenderSprites(ReadOnlySpan<byte>.Empty);
            _native.ClearSprites(); _overlay?.Dispose(); _overlay = null; _textOverlay = null;
        }
    }
    public void Dispose() { IsDisposed = true; _display.Dispose(); _canvas?.Dispose(); _overlay?.Dispose(); _frame?.Dispose(); }
}
