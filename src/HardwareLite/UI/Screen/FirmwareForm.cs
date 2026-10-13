using HardwareLite.Communication;
using HardwareLite.Configuration;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI.Screen;

internal sealed class FirmwareForm : Form
{
    private readonly Func<BackendState> _state;
    private readonly Action<FirmwarePackage> _upload;
    private readonly Label _device = UiTheme.Label(), _file = UiTheme.Label("请选择机型匹配的原厂升级包", true), _status = UiTheme.Label("未开始更新", true);
    private readonly AntdUI.Button _start;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private FirmwarePackage? _package;
    public FirmwareForm(Func<BackendState> state, Action<FirmwarePackage> upload)
    {
        _state = state; _upload = upload;
        UiTheme.Configure(this, "水冷屏幕 · 固件维护", new Size(640, 380), new Size(640, 380));
        var fields = UiTheme.Fields(108);
        UiTheme.Section(fields, "PRIME SLC II 360 ARGB LCD");
        UiTheme.Field(fields, "当前设备", _device);
        UiTheme.Field(fields, "升级文件", _file);
        UiTheme.Field(fields, "选择文件", UiTheme.Button("选择原厂 BIN", Browse));
        UiTheme.Field(fields, "更新状态", _status);
        var note = UiTheme.Label("更新期间保持设备供电，请勿拔出 USB。\n原安装包内的 1.0.1 低于本机 1.0.2，不会自动刷入。", true, 9);
        UiTheme.Field(fields, "注意事项", note);
        _start = UiTheme.Button("更新固件", StartUpdate, true);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(16), BackColor = UiTheme.Background };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        body.Controls.Add(UiTheme.Region(UiTheme.Scroll(fields)), 0, 0);
        body.Controls.Add(UiTheme.Actions(_start, UiTheme.Button("关闭", Close)), 0, 1);
        Controls.Add(body);
        _timer.Tick += (_, _) => RefreshState(); _timer.Start(); RefreshState();
    }
    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "原厂固件升级包|*.bin", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int variant = _state().FirmwareImageVariant ?? throw new InvalidDataException("请先连接屏幕并读取固件平台信息。");
            _package = FirmwarePackage.Load(dialog.FileName, variant);
            _file.Text = Path.GetFileName(_package.FilePath) + $" · {_package.Application.Length:N0} 字节";
            _status.Text = "机型与校验和通过；请核对文件来源和版本后更新";
            RefreshState();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { _package = null; _status.Text = "文件校验失败：" + error.Message; _start.Enabled = false; }
    }
    private void StartUpdate()
    {
        if (_package is null) return;
        if (MessageBox.Show(this, $"将为当前水冷屏幕写入 {_file.Text}。\n请确认这是 ASUS 提供的对应机型固件，且版本符合预期。\n更新期间不能断电或拔出设备。继续更新？", "确认固件更新", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { _upload(_package); RefreshState(); }
        catch (Exception error) when (error is IOException or TimeoutException or InvalidOperationException or OperationCanceledException)
        { _status.Text = "无法开始更新：" + error.Message; }
    }
    private void RefreshState()
    {
        var state = _state();
        _device.Text = state.FirmwareVersion.Length == 0 ? "未连接或设备信息未返回" : "固件 " + state.FirmwareVersion + (state.FirmwareImageVariant is int variant ? $" · 平台 {variant}" : "");
        _start.Enabled = _package is not null && state.Connected && state.FirmwareVersion.Length > 0 && _package.ImageVariant == state.FirmwareImageVariant && !state.Firmware.Running;
        if (state.Firmware != FirmwareUpdateStatus.Idle) _status.Text = state.Firmware.Message + $" · {state.Firmware.Progress}%";
    }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
