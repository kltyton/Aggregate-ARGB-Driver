using HardwareLite.Display.Media;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI.Screen;

internal sealed class MediaCapacityForm : Form
{
    public MediaCapacityForm(DeviceMediaCapacityException error)
    {
        SuspendLayout();
        UiTheme.Configure(this, "设备存储空间不足", new Size(530, 250), new Size(480, 230));
        Shown += (_, _) => CenterToParent();
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var title = UiTheme.Label("无损压缩后仍超过设备上限", false, 14);
        var detail = UiTheme.Label($"媒体已达到 {error.RequiredBytes / 1048576d:F2} MiB，设备当前可用 {error.AvailableBytes / 1048576d:F2} MiB。\n\n取消会保留当前显示。主机发送模式保留原媒体和全部显示设置。", true);
        detail.Dock = DockStyle.Fill; detail.AutoSize = false;
        var cancel = UiTheme.Button("取消上传", () => { DialogResult = DialogResult.Cancel; Close(); });
        var host = UiTheme.Button("使用主机发送", () => { DialogResult = DialogResult.OK; Close(); }, primary: true);
        content.Controls.Add(title, 0, 0); content.Controls.Add(detail, 0, 1); content.Controls.Add(UiTheme.Actions(cancel, host), 0, 2);
        Controls.Add(content); CancelButton = cancel; AcceptButton = cancel;
        ResumeLayout(true);
    }
}
