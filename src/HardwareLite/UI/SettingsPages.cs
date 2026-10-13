using HardwareLite.Configuration;
using HardwareLite.Monitoring;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI;

internal sealed partial class SettingsForm
{
    private readonly Label _caseReadout = UiTheme.Label("等待采样", false, 20);
    private void BuildShell()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 16), ColumnCount = 1, RowCount = 4, BackColor = UiTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = UiTheme.Label("聚合argb驱动", false, 16); brand.Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold);
        var identity = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        brand.Margin = new Padding(0, 0, 0, 2); identity.Controls.Add(brand); identity.Controls.Add(UiTheme.Label("设备控制中心", true, 8));
        header.Controls.Add(identity, 0, 0);
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        _pageTitle.Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold); _pageTitle.Margin = new Padding(0, 0, 0, 2);
        _pageDetail.Margin = new Padding(0); heading.Controls.Add(_pageTitle); heading.Controls.Add(_pageDetail);
        header.Controls.Add(heading, 1, 0); _metrics.Margin = new Padding(0, 14, 0, 0); header.Controls.Add(_metrics, 2, 0); root.Controls.Add(header, 0, 0);
        var nav = UiTheme.Actions(); string[] names = ["水冷屏幕", "主板灯光", "机箱屏幕", "共享监控"];
        for (int i = 0; i < names.Length; i++) { int index = i; var button = UiTheme.Button(names[i], () => SelectPage(index), name: "Navigate" + index); button.Width = 148; button.Font = new Font(Font, FontStyle.Bold); _navigation.Add(button); nav.Controls.Add(button); }
        root.Controls.Add(nav, 0, 1); root.Controls.Add(_body, 0, 2);
        _pages.Add(BuildScreenPage()); _pages.Add(BuildLightingPage()); _pages.Add(BuildCasePage()); _pages.Add(BuildGeneralPage());
        foreach (var page in _pages) { page.Dock = DockStyle.Fill; page.Visible = false; _body.Controls.Add(page); }
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 12, 0, 0), Margin = new Padding(0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _message.Text = "更改后请保存。关闭窗口后设备继续运行。"; _message.AutoSize = false; _message.Dock = DockStyle.Fill; footer.Controls.Add(_message, 0, 0);
        var save = UiTheme.Button("保存并应用", Save, true); save.Height = 40; _connect.Height = 40;
        footer.Controls.Add(UiTheme.Actions(_connect, save), 1, 0); root.Controls.Add(footer, 0, 3); Controls.Add(root);
    }
    private void SelectPage(int index)
    {
        _previewTimer.Stop(); _page = index;
        if (index != 0)
        {
            _previewCanvas?.Dispose(); _previewCanvas = null;
            _preview.Image?.Dispose(); _preview.Image = null;
        }
        string[] titles = ["屏幕设置", "灯光设置", "数字屏幕", "监控设置"];
        string[] details = ["ASUS PRIME SLC II · LCD", "MAXSUN · ARGB", "DeepCool CH270", "传感器与后台"];
        _pageTitle.Text = titles[index]; _pageDetail.Text = details[index];
        for (int i = 0; i < _pages.Count; i++) { _pages[i].Visible = i == index; _navigation[i].DefaultBack = i == index ? UiTheme.Selected : UiTheme.Background; _navigation[i].ForeColor = i == index ? UiTheme.Accent : UiTheme.Muted; _navigation[i].DefaultBorderColor = i == index ? UiTheme.Accent : UiTheme.Border; }
        _pages[index].BringToFront();
        if (index == 0 && !_loading) { DraftChanged(); if (_advanced.Screen.Options.PreviewEnabled) _previewTimer.Start(); }
    }
    private Control BuildScreenPage()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 0, 12, 0), BackColor = UiTheme.Surface };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        var toolbar = UiTheme.Actions(UiTheme.Button("自定义布局", EditLayout), UiTheme.Button("裁剪 / GIF 片段", () => EditMedia(_media.Text)), UiTheme.Button("旋转 90°", () => _rotation.SelectedIndex = (_rotation.SelectedIndex + 1) % 4));
        left.Controls.Add(toolbar, 0, 0); left.Controls.Add(_preview, 0, 1);
        var mediaHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        mediaHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); mediaHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _media.Dock = DockStyle.Fill; _media.AccessibleName = "当前媒体文件"; mediaHeader.Controls.Add(_media, 0, 0);
        mediaHeader.Controls.Add(UiTheme.Actions(UiTheme.Button("添加", BrowseMedia), UiTheme.Button("媒体库", EditLibrary)), 1, 0);
        left.Controls.Add(mediaHeader, 0, 2); left.Controls.Add(_thumbnails, 0, 3); var previewRegion = UiTheme.Region(left, 12); previewRegion.Margin = new Padding(0, 0, 12, 0); split.Controls.Add(previewRegion, 0, 0);
        var fields = UiTheme.Fields(108); UiTheme.Section(fields, "显示内容");
        UiTheme.Field(fields, "屏幕开关", _screenEnabled); UiTheme.Field(fields, "显示模式", _screenMode); UiTheme.Field(fields, "内置背景", _factoryBackground);
        UiTheme.Section(fields, "监控布局");
        UiTheme.Field(fields, "上方项目", _slots[0]); UiTheme.Field(fields, "左下项目", _slots[1]); UiTheme.Field(fields, "右下项目", _slots[2]);
        UiTheme.Field(fields, "标题颜色", _titleColor); UiTheme.Field(fields, "数值颜色", _valueColor);
        UiTheme.Field(fields, "时钟样式", UiTheme.Actions(UiTheme.Button("数字", () => SetClock(false)), UiTheme.Button("指针", () => SetClock(true))));
        UiTheme.Section(fields, "屏幕与播放"); UiTheme.Field(fields, "亮度 %", _screenBrightness); UiTheme.Field(fields, "旋转角度", _rotation);
        UiTheme.Field(fields, "发送上限 · FPS", _fps); UiTheme.Field(fields, "媒体播放", _playback); UiTheme.Field(fields, "轮播间隔 · 秒", _mediaInterval); UiTheme.Field(fields, "休眠模式", _sleep);
        UiTheme.Field(fields, "设备状态", _screenStatus);
        UiTheme.Field(fields, "高级显示设置", UiTheme.Button("画质 / 播放 / 解码", EditScreenOptions));
        UiTheme.Field(fields, "字体与背景", UiTheme.Button("管理字体 / 背景", EditScreenResources));
        UiTheme.Field(fields, "固件维护", UiTheme.Button("查看 / 更新固件", _firmwareMaintenance));
        split.Controls.Add(UiTheme.Region(UiTheme.Scroll(fields), 0), 1, 0); return split;
    }
    private void SetClock(bool analog)
    {
        _layout = _layout with { Elements = analog ? [new() { Metric = ScreenMetric.AnalogClock, Title = "", X = 100, Y = 100, Width = 520, Height = 520 }]
            : [new() { Metric = ScreenMetric.Time, Title = "", X = 40, Y = 250, Width = 640, Height = 150, FontSize = 110, Alignment = TextAlignment.Center },
                new() { Metric = ScreenMetric.Date, Title = "", X = 40, Y = 420, Width = 640, Height = 100, FontSize = 48, Alignment = TextAlignment.Center }] };
        SyncLayoutControls(); DraftChanged();
    }
    private Control BuildLightingPage()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        var modes = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = UiTheme.Surface };
        modes.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); modes.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); modes.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        modes.Controls.Add(UiTheme.Label("灯效", false, 12), 0, 0); var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Surface };
        string[] names = ["常亮", "关闭", "呼吸", "彩虹", "循环", "星空", "彗星", "波浪", "智能监控", "音乐", "键鼠联动"];
        for (int i = 0; i < names.Length; i++) { int index = i; var button = UiTheme.Button(names[i], () => _lightMode.SelectedIndex = index, name: "LightingMode" + i); button.Size = new Size(156, 60); button.Margin = new Padding(0, 0, 12, 12); _effectButtons.Add(button); grid.Controls.Add(button); }
        modes.Controls.Add(grid, 0, 1); _lightStatus.MaximumSize = new Size(600, 0); modes.Controls.Add(_lightStatus, 0, 2);
        var region = UiTheme.Region(modes); region.Margin = new Padding(0, 0, 12, 0); split.Controls.Add(region, 0, 0);
        var fields = UiTheme.Fields(); UiTheme.Section(fields, "灯光控制"); UiTheme.Field(fields, "灯光开关", _lightEnabled); UiTheme.Field(fields, "控制分组", _lightGroup);
        UiTheme.Field(fields, "当前灯效", _lightMode); UiTheme.Field(fields, "前景颜色", _color); UiTheme.Field(fields, "亮度 %", _lightBrightness);
        UiTheme.Section(fields, "效果参数", "每种灯效分别保留颜色、速度和方向设置。"); UiTheme.Field(fields, "速度与联动", UiTheme.Button("高级灯效参数", EditEffect));
        UiTheme.Field(fields, "独立分组配置", UiTheme.Actions(UiTheme.Button("记录该分组", StoreLightingGroup), UiTheme.Button("恢复共享", FollowLightingGroup)));
        UiTheme.Field(fields, "查看 / 编辑分组", UiTheme.Button("编辑独立分组", EditLightingGroup));
        UiTheme.Field(fields, "更新与音频", UiTheme.Button("采集设备 / FFT / 策略", EditLightingOptions));
        split.Controls.Add(UiTheme.Region(UiTheme.Scroll(fields), 0), 1, 0); return split;
    }
    private Control BuildCasePage()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        var readout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiTheme.Surface, Padding = new Padding(24) };
        readout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); readout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); readout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        readout.Controls.Add(UiTheme.Label("CH270 · 硬件监控", false, 18), 0, 0); _caseReadout.Font = new Font("Segoe UI", 20); _caseReadout.ForeColor = UiTheme.Accent; _caseReadout.Margin = new Padding(0, 24, 0, 0);
        readout.Controls.Add(_caseReadout, 0, 1); readout.Controls.Add(_caseStatus, 0, 2); var region = UiTheme.Region(readout); region.Margin = new Padding(0, 0, 12, 0); split.Controls.Add(region, 0, 0);
        var fields = UiTheme.Fields(); UiTheme.Section(fields, "数字屏幕"); UiTheme.Field(fields, "屏幕开关", _caseEnabled); UiTheme.Field(fields, "显示页面", _casePage); UiTheme.Field(fields, "轮播间隔 · 秒", _switch);
        UiTheme.Section(fields, "监控来源", "CPU、GPU 与风扇数据使用共享监控设置。"); UiTheme.Field(fields, "页面与独立来源", UiTheme.Button("轮播 / 风扇 / 策略", EditCaseOptions)); split.Controls.Add(UiTheme.Region(UiTheme.Scroll(fields), 0), 1, 0); return split;
    }
    private void RefreshCasePreview(SensorSnapshot snapshot)
    {
        string Value(float? value, string unit) => snapshot.IsFresh && value.HasValue ? $"{value:0}{unit}" : "--";
        _caseReadout.Text = $"CPU   {Temperature(snapshot.CpuTemperature, snapshot.IsFresh)}\n{Value(snapshot.CpuUsage, "%")}   {Value(snapshot.CpuPower, " W")}\n\nGPU   {Temperature(snapshot.GpuTemperature, snapshot.IsFresh)}\n{Value(snapshot.GpuUsage, "%")}   {Value(snapshot.GpuPower, " W")}";
    }
    private Control BuildGeneralPage()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        var sensors = UiTheme.Fields(150); UiTheme.Section(sensors, "共享传感器"); UiTheme.Field(sensors, "监控显卡", _gpu); UiTheme.Field(sensors, "CPU 风扇", _fan);
        UiTheme.Field(sensors, "AIO 泵", _pump); UiTheme.Field(sensors, "系统风扇", _systemFan); UiTheme.Field(sensors, "主板温度", _boardTemperature);
        var region = UiTheme.Region(UiTheme.Scroll(sensors), 0); region.Margin = new Padding(0, 0, 12, 0); split.Controls.Add(region, 0, 0);
        var fields = UiTheme.Fields(150); UiTheme.Section(fields, "采样设置"); UiTheme.Field(fields, "采样间隔 · 秒", _interval); UiTheme.Field(fields, "温度单位", _unit);
        UiTheme.Field(fields, "高级监控设置", UiTheme.Button("来源 / 类别 / 重连", EditMonitoringOptions));
        UiTheme.Section(fields, "后台运行"); UiTheme.Field(fields, "开机自启", _autostart); UiTheme.Field(fields, "唤醒延迟 · 秒", _resumeDelay);
        UiTheme.Section(fields, "后台服务", "关闭窗口后配置界面退出，后台继续控制设备。需要修改时手动重新打开。");
        UiTheme.Field(fields, "完全退出", UiTheme.Button("停止后台并退出", () =>
        {
            try { _stopBackend(); Close(); }
            catch (Exception error) when (error is IOException or TimeoutException or InvalidOperationException or OperationCanceledException)
            { Report("后台无法退出：" + error.Message, true); }
        }));
        split.Controls.Add(UiTheme.Region(UiTheme.Scroll(fields), 0), 1, 0); return split;
    }
}
