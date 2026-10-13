using System.Diagnostics;
using System.Runtime.InteropServices;
using HardwareLite.Configuration;
using HardwareLite.Display;
using HardwareLite.Display.Media;
using HardwareLite.Monitoring;
using HardwareLite.Runtime;
using HardwareLite.UI.Lighting;
using HardwareLite.UI.Screen;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI;

internal sealed partial class SettingsForm : Form
{
    private readonly Func<Settings> _read;
    private readonly Action<Settings> _save;
    private readonly Func<SensorSnapshot> _snapshot;
    private readonly Func<DeviceStatus> _status;
    private readonly Func<bool> _connected;
    private readonly Action _toggle;
    private readonly ISensorSource _sampler;
    private readonly Func<bool> _autostartEnabled;
    private readonly Action<bool> _setAutostart;
    private readonly Action _firmwareMaintenance;
    private readonly Action _stopBackend;
    private readonly Func<long?> _mediaCapacity;
    private readonly Func<bool> _nativeVideoSupported;
    private readonly Label _metrics = UiTheme.Label("", true), _message = UiTheme.Label("", true, 9);
    private readonly AntdUI.Button _connect = UiTheme.Button("暂停设备控制");
    private readonly AntdUI.InputNumber _interval = UiTheme.Number(0, 10), _resumeDelay = UiTheme.Number(0, 60);
    private readonly AntdUI.Select _unit = UiTheme.Choice("摄氏 °C", "华氏 °F");
    private readonly AntdUI.Select _gpu = UiTheme.Choice(), _fan = UiTheme.Choice(), _pump = UiTheme.Choice(), _systemFan = UiTheme.Choice(), _boardTemperature = UiTheme.Choice();
    private readonly AntdUI.Switch _autostart = UiTheme.Switch(), _caseEnabled = UiTheme.Switch(), _lightEnabled = UiTheme.Switch(), _screenEnabled = UiTheme.Switch(), _sleep = UiTheme.Switch();
    private readonly AntdUI.Select _casePage = UiTheme.Choice("轮播", "CPU", "GPU", "关闭显示", "固定 CPU 频率", "固定 CPU 风扇");
    private readonly AntdUI.InputNumber _switch = UiTheme.Number(1, 60);
    private readonly AntdUI.Select _lightMode = UiTheme.Choice("常亮", "关闭", "呼吸", "彩虹", "循环", "星空", "彗星", "波浪", "智能监控", "音乐", "键鼠联动");
    private readonly AntdUI.Select _lightGroup = UiTheme.Choice("全部同步", "分组 1 · 128 灯珠", "分组 2 · 128 灯珠", "分组 3 · 1 灯珠", "分组 4 · 1 灯珠", "分组 5 · 6 灯珠");
    private readonly AntdUI.ColorPicker _color = UiTheme.ColorField(), _titleColor = UiTheme.ColorField(), _valueColor = UiTheme.ColorField();
    private readonly AntdUI.InputNumber _lightBrightness = UiTheme.Number(0, 100), _screenBrightness = UiTheme.Number(0, 100), _fps = UiTheme.Number(1, 60), _mediaInterval = UiTheme.Number(1, 3600);
    private readonly AntdUI.Select _screenMode = UiTheme.Choice("硬件监控", "图片 / GIF / 视频", "媒体 + 监控");
    private readonly AntdUI.Select _factoryBackground = UiTheme.Choice();
    private readonly AntdUI.Input _media = UiTheme.Input(true);
    private readonly AntdUI.Select _rotation = UiTheme.Choice("0°", "90°", "180°", "270°"), _playback = UiTheme.Choice("单项循环", "顺序轮播", "随机轮播");
    private readonly AntdUI.Select[] _slots = [UiTheme.Choice(MetricText.Names), UiTheme.Choice(MetricText.Names), UiTheme.Choice(MetricText.Names)];
    private readonly Label _lightStatus = UiTheme.Label("", true, 9), _screenStatus = UiTheme.Label("", true, 9), _caseStatus = UiTheme.Label("", true, 9);
    private readonly Label _pageTitle = UiTheme.Label("水冷屏幕", false, 18), _pageDetail = UiTheme.Label("PRIME SLC II · LCD", true, 9);
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
    private readonly FlowLayoutPanel _thumbnails = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTheme.Surface };
    private readonly PictureBox _preview = new() { Name = "ScreenPreview", Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = UiTheme.Surface, AccessibleName = "水冷屏幕实时预览" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 }, _previewTimer = new() { Interval = 100 };
    private ScreenCanvas? _previewCanvas;
    private bool _previewPaused;
    private readonly Stopwatch _previewClock = Stopwatch.StartNew();
    private readonly List<AntdUI.Button> _navigation = [], _effectButtons = [];
    private readonly List<Control> _pages = [];
    private Dictionary<LightingMode, LightingParameters> _lightProfiles = [];
    private LightingMode _currentLightMode;
    private string[] _mediaFiles = [];
    private Dictionary<string, MediaEdit> _mediaEdits = [];
    private ScreenLayout _layout = new();
    private Settings? _previewSettings;
    private bool _loading = true;
    private int _page;
    private bool _saving;
    private readonly CancellationTokenSource _mediaPreparationCancellation = new();

    public SettingsForm(Func<Settings> read, Action<Settings> save, ISensorSource sampler, Func<DeviceStatus> status, Func<bool> connected, Action toggle, Func<bool> autostartEnabled, Action<bool> setAutostart, Action firmwareMaintenance, Action stopBackend, Func<long?> mediaCapacity, Func<bool> nativeVideoSupported)
    {
        _read = read; _save = save; _snapshot = () => sampler.Snapshot; _status = status; _connected = connected; _toggle = toggle; _sampler = sampler;
        _autostartEnabled = autostartEnabled; _setAutostart = setAutostart;
        _firmwareMaintenance = firmwareMaintenance;
        _stopBackend = stopBackend;
        _mediaCapacity = mediaCapacity;
        _nativeVideoSupported = nativeVideoSupported;
        SuspendLayout(); UiTheme.Configure(this, "聚合argb驱动 · 设备控制", new Size(1180, 640), new Size(980, 580)); StartPosition = FormStartPosition.CenterScreen;
        BuildShell(); LoadSettings(); _loading = false; BindChanges(this);
        _lightMode.SelectedIndexChanged += (_, _) => ChangeLightMode();
        for (int i = 0; i < _slots.Length; i++) { int index = i; _slots[i].SelectedIndexChanged += (_, _) => ChangeSlot(index); }
        _titleColor.ValueChanged += (_, _) => ChangeTextColor(true); _valueColor.ValueChanged += (_, _) => ChangeTextColor(false);
        _factoryBackground.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _factoryBackground.SelectedValue is not string path) return;
            _media.Text = path; _playback.SelectedIndex = 0; RebuildThumbnails(); DraftChanged();
        };
        _media.TextChanged += (_, _) => SyncBackgroundSelection();
        _connect.Click += (_, _) =>
        {
            try { _toggle(); RefreshStatus(); }
            catch (Exception error) when (error is IOException or TimeoutException or InvalidOperationException or OperationCanceledException)
            { Report("设备操作失败：" + error.Message, true); }
        };
        _timer.Tick += (_, _) => RefreshStatus(); _timer.Start(); _previewTimer.Tick += (_, _) => RefreshPreview();
        SelectPage(0); RefreshStatus(); ResumeLayout(true);
    }
    private void LoadSettings()
    {
        var value = _read();
        LoadAdvanced(value);
        try { _autostart.Checked = _autostartEnabled(); }
        catch (Exception error) when (error is COMException or InvalidOperationException or IOException or UnauthorizedAccessException) { Report("自启设置读取失败：" + error.Message, true); }
        _interval.Value = value.SampleIntervalMs / 1000m; _unit.SelectedIndex = (int)value.TemperatureUnit; _resumeDelay.Value = value.ResumeDelayMs / 1000m;
        AddSensors(_gpu, _sampler.Gpus, value.GpuId, "自动选择独立显卡"); AddSensors(_fan, _sampler.Fans, value.CpuFanId, "未选择（不显示风扇页）");
        AddSensors(_pump, _sampler.Fans, value.AioPumpId, "未选择 AIO 泵"); AddSensors(_systemFan, _sampler.Fans, value.SystemFanId, "未选择系统风扇");
        AddSensors(_boardTemperature, _sampler.BoardTemperatures, value.SystemTemperatureId, "自动识别主板温度");
        _caseEnabled.Checked = value.Case.Enabled; _casePage.SelectedIndex = (int)value.Case.Page; _switch.Value = value.Case.SwitchSeconds;
        _lightProfiles = new(value.Lighting.Profiles); _currentLightMode = value.Lighting.Mode;
        _lightEnabled.Checked = value.Lighting.Enabled; _lightMode.SelectedIndex = (int)value.Lighting.Mode; _lightBrightness.Value = value.Lighting.Brightness;
        _lightGroup.SelectedIndex = value.Lighting.Group + 1; _color.Value = ColorTranslator.FromHtml(value.Lighting.Parameters.Color);
        _screenEnabled.Checked = value.Screen.Enabled; _screenMode.SelectedIndex = (int)value.Screen.Mode;
        _media.Text = value.Screen.MediaPath; _screenBrightness.Value = value.Screen.Brightness; _fps.Value = value.Screen.MaxFps;
        _layout = value.Screen.Layout; _rotation.SelectedIndex = value.Screen.Rotation; _sleep.Checked = value.Screen.SleepWithDisplay;
        _mediaFiles = [..value.Screen.MediaFiles]; _playback.SelectedIndex = (int)value.Screen.Playback; _mediaInterval.Value = value.Screen.MediaIntervalSeconds;
        _mediaEdits = new(value.Screen.MediaEdits); SyncLayoutControls(); RebuildThumbnails(); RefreshEffectButtons();
        _factoryBackground.PlaceholderText = "选择原厂背景";
        foreach (var background in value.Screen.BuiltInBackgrounds) _factoryBackground.Items.Add(new AntdUI.SelectItem(background.Key, background.Value));
        _factoryBackground.Enabled = value.Screen.BuiltInBackgrounds.Count > 0; SyncBackgroundSelection();
    }
    private void BindChanges(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is AntdUI.InputNumber number) number.ValueChanged += (_, _) => DraftChanged();
            else if (control is AntdUI.Select select) select.SelectedIndexChanged += (_, _) => DraftChanged();
            else if (control is AntdUI.Input input) input.TextChanged += (_, _) => DraftChanged();
            else if (control is AntdUI.Switch toggle) toggle.CheckedChanged += (_, _) => DraftChanged();
            BindChanges(control);
        }
    }
    private void DraftChanged()
    {
        if (_loading) return; _previewSettings = DraftSettings(); _previewCanvas?.Invalidate(); if (_page == 0) RefreshPreview();
    }
    private Settings DraftSettings() => _read() with { TemperatureUnit = (TemperatureUnit)_unit.SelectedIndex, Monitoring = _advanced.Monitoring, Runtime = _advanced.Runtime, Screen = _read().Screen with
    {
        Enabled = _screenEnabled.Checked, Mode = (ScreenMode)_screenMode.SelectedIndex, MediaPath = _media.Text, Brightness = (int)_screenBrightness.Value,
        MaxFps = (int)_fps.Value, Layout = _layout, Rotation = _rotation.SelectedIndex, SleepWithDisplay = _sleep.Checked, MediaFiles = [.._mediaFiles],
        Playback = (MediaPlayback)_playback.SelectedIndex, MediaIntervalSeconds = (int)_mediaInterval.Value, MediaEdits = new(_mediaEdits),
        Options = _advanced.Screen.Options, FontFiles = _advanced.Screen.FontFiles, BuiltInBackgrounds = new(_advanced.Screen.BuiltInBackgrounds),
    } };
    private void RefreshPreview()
    {
        if (_loading || _previewPaused || _page != 0 || _previewSettings is not { } settings) return;
        try
        {
            _previewCanvas ??= new ScreenCanvas();
            var snapshot = _snapshot(); if (!_previewCanvas.IsDue(settings, snapshot, _previewClock.ElapsedMilliseconds)) return;
            _preview.Image ??= new Bitmap(720, 720, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            _previewCanvas.RenderInto((Bitmap)_preview.Image, settings, snapshot, _previewClock.ElapsedMilliseconds);
            _preview.Invalidate();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OutOfMemoryException) { Report("预览失败：" + error.Message, true); }
    }
    private async void Save()
    {
        if (_saving) return;
        _saving = true;
        PausePreview();
        try
        {
            SaveLightProfile(); var settings = _read() with
            {
                SampleIntervalMs = (int)(_interval.Value * 1000), TemperatureUnit = (TemperatureUnit)_unit.SelectedIndex, ResumeDelayMs = (int)(_resumeDelay.Value * 1000),
                Monitoring = _advanced.Monitoring, Runtime = _advanced.Runtime,
                GpuId = SensorId(_gpu), CpuFanId = SensorId(_fan), AioPumpId = SensorId(_pump), SystemFanId = SensorId(_systemFan), SystemTemperatureId = SensorId(_boardTemperature),
                Case = new() { Enabled = _caseEnabled.Checked, Page = (CasePage)_casePage.SelectedIndex, SwitchSeconds = (int)_switch.Value, Options = _advanced.Case.Options },
                Lighting = new() { Enabled = _lightEnabled.Checked, Mode = (LightingMode)_lightMode.SelectedIndex, Color = Hex(_color.Value), Brightness = (int)_lightBrightness.Value, Group = _lightGroup.SelectedIndex - 1, Profiles = new(_lightProfiles), Options = _advanced.Lighting.Options, Audio = _advanced.Lighting.Audio },
                Screen = DraftSettings().Screen,
            };
            if (settings.Screen.Enabled && settings.Screen.Mode != ScreenMode.Monitor && (settings.Screen.Playback == MediaPlayback.Single ? !File.Exists(settings.Screen.MediaPath)
                 : settings.Screen.MediaFiles.Length == 0 || settings.Screen.MediaFiles.Any(path => !File.Exists(path)))) throw new InvalidDataException("请选择可读取的图片、GIF、MP4 或 AVI，并检查媒体库中的文件。");
            settings.Validate();
            var progress = new Progress<int>(value => { if (!IsDisposed) Report($"正在准备设备媒体 · {value}%"); });
            var cancellation = _mediaPreparationCancellation.Token;
            try
            {
                long capacity = settings.Screen.Enabled && settings.Screen.Options.DevicePlayback && settings.Screen.Mode != ScreenMode.Monitor
                    ? _mediaCapacity() ?? throw new InvalidOperationException("设备未连接或当前固件不支持无损设备媒体，请先连接并更新固件。") : long.MaxValue;
                settings = await Task.Run(() => DeviceMediaPreparation.Prepare(settings,
                    Path.Combine(AppContext.BaseDirectory, "config", "device-media"), capacity, value => ((IProgress<int>)progress).Report(value), cancellation));
                cancellation.ThrowIfCancellationRequested();
                if (DeviceMediaPreparation.IsNativeVideo(settings.Screen) && !_nativeVideoSupported())
                    throw new InvalidOperationException("当前固件不支持压缩设备视频，请先更新固件或选择无损画面编码。");
            }
            catch (DeviceMediaCapacityException error)
            {
                cancellation.ThrowIfCancellationRequested();
                using var choice = new MediaCapacityForm(error);
                if (choice.ShowDialog(this) != DialogResult.OK) { Report("已取消上传，当前显示保持原配置。"); return; }
                settings = settings with { Screen = settings.Screen with { DeviceMediaPath = "", Options = settings.Screen.Options with { DevicePlayback = false } } };
                _advanced = _advanced with { Screen = _advanced.Screen with { Options = settings.Screen.Options } };
            }
            cancellation.ThrowIfCancellationRequested();
            settings = await Task.Run(() => DeviceUiPreparation.Prepare(settings, _snapshot(),
                Path.Combine(AppContext.BaseDirectory, "config", "device-ui"), cancellation));
            cancellation.ThrowIfCancellationRequested();
            if (_autostart.Checked != _autostartEnabled()) _setAutostart(_autostart.Checked);
            _save(settings); Report("设置已保存并应用。");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or COMException or InvalidOperationException or TimeoutException or OperationCanceledException) { if (!IsDisposed) Report("保存失败：" + error.Message, true); }
        finally { _saving = false; if (!IsDisposed) ResumePreview(); }
    }
    private void RefreshStatus()
    {
        RefreshSensors(_gpu, _sampler.Gpus, "自动选择独立显卡"); RefreshSensors(_fan, _sampler.Fans, "未选择（不显示风扇页）");
        RefreshSensors(_pump, _sampler.Fans, "未选择 AIO 泵"); RefreshSensors(_systemFan, _sampler.Fans, "未选择系统风扇"); RefreshSensors(_boardTemperature, _sampler.BoardTemperatures, "自动识别主板温度");
        var snapshot = _snapshot(); _metrics.Text = $"CPU  {Temperature(snapshot.CpuTemperature, snapshot.IsFresh)}     GPU  {Temperature(snapshot.GpuTemperature, snapshot.IsFresh)}";
        var status = _status(); _lightStatus.Text = status.Lighting; _screenStatus.Text = status.Screen; _caseStatus.Text = status.Case;
        _connect.Text = _connected() ? "暂停设备控制" : "连接设备"; RefreshCasePreview(snapshot);
    }
    private string Temperature(float? value, bool fresh) => !fresh || !value.HasValue ? "--" : _unit.SelectedIndex == (int)TemperatureUnit.Fahrenheit ? $"{value.Value * 1.8f + 32:0} °F" : $"{value.Value:0} °C";
    private void Report(string message, bool error = false) { _message.ForeColor = error ? UiTheme.Error : UiTheme.Muted; _message.Text = message; }
    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static string SensorId(AntdUI.Select control) => (control.SelectedValue as SensorOption)?.Id ?? "";
    private static void AddSensors(AntdUI.Select box, IReadOnlyList<SensorOption> sensors, string selected, string automatic)
    {
        box.Items.Clear(); box.Items.Add(new AntdUI.SelectItem(automatic, new SensorOption("", automatic))); foreach (var sensor in sensors) box.Items.Add(new AntdUI.SelectItem(sensor.Name, sensor));
        int index = sensors.ToList().FindIndex(option => option.Id == selected);
        if (selected.Length > 0 && index < 0) { box.Items.Add(new AntdUI.SelectItem("等待传感器连接 · " + selected, new SensorOption(selected, selected))); box.SelectedIndex = box.Items.Count - 1; } else box.SelectedIndex = index + 1;
    }
    private void RefreshSensors(AntdUI.Select box, IReadOnlyList<SensorOption> sensors, string automatic)
    {
        if (box.ExpandDrop || box.Items.OfType<AntdUI.SelectItem>().Skip(1).Select(item => ((SensorOption)item.Tag).Id).SequenceEqual(sensors.Select(item => item.Id))) return;
        bool loading = _loading; _loading = true; try { AddSensors(box, sensors, SensorId(box), automatic); } finally { _loading = loading; }
    }
    private void SaveLightProfile() { var value = _lightProfiles.GetValueOrDefault(_currentLightMode) ?? new LightingParameters(); _lightProfiles[_currentLightMode] = value with { Color = Hex(_color.Value), Brightness = (int)_lightBrightness.Value }; }
    private void ChangeLightMode()
    {
        if (_loading || _lightMode.SelectedIndex < 0) return; SaveLightProfile(); _currentLightMode = (LightingMode)_lightMode.SelectedIndex;
        var value = _lightProfiles.GetValueOrDefault(_currentLightMode) ?? new LightingParameters(); _color.Value = ColorTranslator.FromHtml(value.Color); _lightBrightness.Value = value.Brightness; RefreshEffectButtons();
    }
    private void RefreshEffectButtons() { for (int i = 0; i < _effectButtons.Count; i++) { _effectButtons[i].DefaultBack = i == _lightMode.SelectedIndex ? UiTheme.Selected : UiTheme.InputBack; _effectButtons[i].ForeColor = i == _lightMode.SelectedIndex ? UiTheme.Accent : UiTheme.Text; } }
    private void EditEffect()
    {
        SaveLightProfile(); using var dialog = new EffectSettingsForm(_lightProfiles[_currentLightMode]); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _lightProfiles[_currentLightMode] = dialog.Result; _color.Value = ColorTranslator.FromHtml(dialog.Result.Color); _lightBrightness.Value = dialog.Result.Brightness;
    }
    private void PausePreview()
    {
        _previewPaused = true;
        _previewTimer.Stop();
        _previewCanvas?.Dispose();
        _previewCanvas = null;
    }
    private void ResumePreview()
    {
        _previewPaused = false;
        if (_page != 0) return;
        RefreshPreview();
        if (_advanced.Screen.Options.PreviewEnabled) _previewTimer.Start();
    }
    private void EditLayout()
    {
        PausePreview();
        try
        {
            using var editor = new LayoutEditor(DraftSettings(), _snapshot);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            _layout = editor.Result; SyncLayoutControls(); DraftChanged();
        }
        finally { ResumePreview(); }
    }
    private void ChangeSlot(int index)
    {
        if (_loading || index >= _layout.Elements.Length || _slots[index].SelectedIndex < 0) return;
        var elements = _layout.Elements.ToArray(); var metric = (ScreenMetric)_slots[index].SelectedIndex;
        elements[index] = elements[index] with { Metric = metric, Title = MetricTitle(metric) }; _layout = _layout with { Elements = elements }; DraftChanged();
    }
    private static string MetricTitle(ScreenMetric metric) => metric.ToString().StartsWith("Cpu", StringComparison.Ordinal) ? "CPU" : metric.ToString().StartsWith("Gpu", StringComparison.Ordinal) ? "GPU" : metric == ScreenMetric.SystemTemperature ? "System" : MetricText.Names[(int)metric];
    private void ChangeTextColor(bool title)
    {
        if (_loading) return; string color = Hex(title ? _titleColor.Value : _valueColor.Value);
        _layout = _layout with { Elements = _layout.Elements.Select(item => title ? item with { TitleColor = color } : item with { Color = color }).ToArray() }; DraftChanged();
    }
    private void SyncLayoutControls()
    {
        bool loading = _loading; _loading = true; try
        {
            for (int i = 0; i < _slots.Length; i++) { _slots[i].Enabled = i < _layout.Elements.Length; _slots[i].SelectedIndex = i < _layout.Elements.Length ? (int)_layout.Elements[i].Metric : -1; }
            if (_layout.Elements.FirstOrDefault() is { } item) { _titleColor.Value = ColorTranslator.FromHtml(item.TitleColor); _valueColor.Value = ColorTranslator.FromHtml(item.Color); }
        } finally { _loading = loading; }
    }
    private void BrowseMedia()
    {
        using var dialog = new OpenFileDialog { Filter = MediaFile.DialogFilter, CheckFileExists = true, Multiselect = true }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var combined = _mediaFiles.Concat(dialog.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int rejected = Math.Max(0, combined.Length - _advanced.Screen.Options.MediaCapacity);
        _mediaFiles = combined.Take(_advanced.Screen.Options.MediaCapacity).ToArray();
        string? added = dialog.FileNames.FirstOrDefault(path => _mediaFiles.Contains(path, StringComparer.OrdinalIgnoreCase));
        if (added is not null) _media.Text = added;
        RebuildThumbnails(); DraftChanged();
        if (rejected > 0) Report($"媒体库上限 {_advanced.Screen.Options.MediaCapacity} 项，另有 {rejected} 项未添加。", true);
    }
    private void EditMedia(string path)
    {
        PausePreview();
        try { if (!File.Exists(path)) throw new FileNotFoundException("请先选择图片、GIF、MP4 或 AVI。", path); using var editor = new MediaCropForm(path, _mediaEdits.GetValueOrDefault(path) ?? new(), _advanced.Screen.Options);
            if (editor.ShowDialog(this) != DialogResult.OK) return; _mediaEdits[path] = editor.Result; DraftChanged(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OutOfMemoryException) { Report("裁剪无法打开：" + error.Message, true); }
        finally { ResumePreview(); }
    }
    private void EditLibrary()
    {
        PausePreview();
        try
        {
            var files = _mediaFiles.Prepend(_media.Text).Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
            using var dialog = new MediaLibraryForm(files, _mediaEdits, _advanced.Screen.Options.MediaCapacity, _advanced.Screen.Options); if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _mediaFiles = dialog.Files; _mediaEdits = dialog.Edits;
            if (!_mediaFiles.Contains(_media.Text, StringComparer.OrdinalIgnoreCase))
                _media.Text = _mediaFiles.FirstOrDefault() ?? "";
            RebuildThumbnails(); DraftChanged();
        }
        finally { ResumePreview(); }
    }
    private void RebuildThumbnails()
    {
        foreach (Control control in _thumbnails.Controls.Cast<Control>().ToArray()) { if (control is PictureBox picture) picture.Image?.Dispose(); control.Dispose(); } _thumbnails.Controls.Clear();
        foreach (string path in _mediaFiles.Prepend(_media.Text).Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var picture = new MediaThumbnail { Size = new Size(72, 72), Margin = new Padding(0, 0, 8, 0), SizeMode = PictureBoxSizeMode.Zoom, BackColor = UiTheme.InputBack, Name = "MediaThumbnail", AccessibleName = Path.GetFileName(path), Tag = path, Cursor = Cursors.Hand };
            try { picture.Image = MediaFile.OpenThumbnail(path, 72, 72); }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OutOfMemoryException) { picture.AccessibleDescription = "图片无法读取：" + error.Message; }
            picture.Click += (_, _) => { _media.Text = path; RefreshThumbnailSelection(); DraftChanged(); }; picture.DoubleClick += (_, _) => EditMedia(path);
            picture.KeyDown += (_, args) => { if (args.KeyCode is Keys.Enter or Keys.Space) { _media.Text = path; RefreshThumbnailSelection(); DraftChanged(); } }; _thumbnails.Controls.Add(picture);
        } RefreshThumbnailSelection();
    }
    private void RefreshThumbnailSelection() { foreach (PictureBox picture in _thumbnails.Controls.OfType<PictureBox>()) picture.BackColor = string.Equals(picture.Tag as string, _media.Text, StringComparison.OrdinalIgnoreCase) ? UiTheme.Selected : UiTheme.InputBack; }
    private void SyncBackgroundSelection()
    {
        int index = _factoryBackground.Items.OfType<AntdUI.SelectItem>().ToList().FindIndex(item => string.Equals(item.Tag as string, _media.Text, StringComparison.OrdinalIgnoreCase));
        if (_factoryBackground.SelectedIndex == index) return;
        bool loading = _loading; _loading = true;
        try { _factoryBackground.SelectedIndex = index; } finally { _loading = loading; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _mediaPreparationCancellation.Cancel(); _timer.Dispose(); _previewTimer.Dispose(); _preview.Image?.Dispose(); _previewCanvas?.Dispose(); foreach (var picture in _thumbnails.Controls.OfType<PictureBox>()) picture.Image?.Dispose(); } base.Dispose(disposing);
    }
}
