using HardwareLite.Configuration;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI.Lighting;

internal sealed class EffectSettingsForm : Form
{
    private readonly Dictionary<string, Control> _inputs = [];
    private readonly Label _message = UiTheme.Label("", true, 9);
    public LightingParameters Result { get; private set; }
    public EffectSettingsForm(LightingParameters value)
    {
        Result = value; SuspendLayout(); UiTheme.Configure(this, "灯效参数", new Size(850, 620), new Size(760, 580));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var common = UiTheme.Fields(118); UiTheme.Section(common, "颜色与动画");
        Color(common, "前景颜色", nameof(value.Color), value.Color); Color(common, "背景颜色", nameof(value.Background), value.Background);
        Number(common, "速度", nameof(value.Speed), 1, 100, value.Speed); Number(common, "亮度 %", nameof(value.Brightness), 0, 100, value.Brightness);
        Toggle(common, "灯珠方向反转", nameof(value.Reverse), value.Reverse); Toggle(common, "色带方向反转", nameof(value.ColorReverse), value.ColorReverse);
        Number(common, "尾长 %", nameof(value.TailLength), 1, 100, value.TailLength); Number(common, "色带预设", nameof(value.Palette), 0, 3, value.Palette);
        Select(common, "段大小", nameof(value.SegmentSize), ["8", "16", "32"], value.SegmentSize == 8 ? 0 : value.SegmentSize == 16 ? 1 : 2);
        Toggle(common, "星空随机颜色", nameof(value.RandomColor), value.RandomColor); Toggle(common, "关闭星空背景", nameof(value.BackgroundOff), value.BackgroundOff);
        var advanced = UiTheme.Fields(118); UiTheme.Section(advanced, "智能监控");
        Select(advanced, "数据来源", nameof(value.SmartSource), ["CPU 温度", "CPU 使用率", "GPU 温度", "GPU 使用率"], (int)value.SmartSource);
        Number(advanced, "下限", nameof(value.Minimum), 0, 999, value.Minimum); Number(advanced, "上限", nameof(value.Maximum), 1, 1000, value.Maximum);
        UiTheme.Section(advanced, "音乐联动"); Select(advanced, "音乐模式", nameof(value.AudioMode), ["三段频谱", "频谱", "峰值波浪"], (int)value.AudioMode);
        Number(advanced, "增益 %", nameof(value.AudioBoost), 1, 1000, value.AudioBoost);
        Color(advanced, "低频颜色", nameof(value.LowColor), value.LowColor); Color(advanced, "中频颜色", nameof(value.MidColor), value.MidColor); Color(advanced, "高频颜色", nameof(value.HighColor), value.HighColor);
        UiTheme.Section(advanced, "键鼠联动"); Select(advanced, "输入来源", nameof(value.InputSource), ["键盘", "鼠标", "键盘与鼠标"], (int)value.InputSource);
        UiTheme.Field(advanced, "自定义参数", UiTheme.Button("色带 / 曲线 / 触发", () =>
        {
            using var dialog = new EffectDetailForm(Result);
            if (dialog.ShowDialog(this) == DialogResult.OK) Result = dialog.Result;
        }));
        var left = UiTheme.Region(UiTheme.Scroll(common), 0); left.Margin = new Padding(0, 0, 12, 0); root.Controls.Add(left, 0, 0); root.Controls.Add(UiTheme.Region(UiTheme.Scroll(advanced), 0), 1, 0);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.Controls.Add(_message, 0, 0);
        var cancel = UiTheme.Button("取消", () => { DialogResult = DialogResult.Cancel; Close(); }); CancelButton = cancel;
        footer.Controls.Add(UiTheme.Actions(cancel, UiTheme.Button("确定", Save, true)), 1, 0); root.Controls.Add(footer, 0, 1); root.SetColumnSpan(footer, 2); Controls.Add(root); ResumeLayout(true);
    }
    private void Save()
    {
        try
        {
            Result = Result with { Color = C(nameof(Result.Color)), Background = C(nameof(Result.Background)), Speed = N(nameof(Result.Speed)), Brightness = N(nameof(Result.Brightness)),
                Reverse = B(nameof(Result.Reverse)), ColorReverse = B(nameof(Result.ColorReverse)), TailLength = N(nameof(Result.TailLength)), Palette = N(nameof(Result.Palette)),
                SegmentSize = new[] { 8, 16, 32 }[S(nameof(Result.SegmentSize))], RandomColor = B(nameof(Result.RandomColor)), BackgroundOff = B(nameof(Result.BackgroundOff)),
                SmartSource = (SmartSource)S(nameof(Result.SmartSource)), Minimum = N(nameof(Result.Minimum)), Maximum = N(nameof(Result.Maximum)), AudioMode = (AudioMode)S(nameof(Result.AudioMode)),
                AudioBoost = N(nameof(Result.AudioBoost)), LowColor = C(nameof(Result.LowColor)), MidColor = C(nameof(Result.MidColor)), HighColor = C(nameof(Result.HighColor)), InputSource = (InputSource)S(nameof(Result.InputSource)) };
            Result.Validate(); DialogResult = DialogResult.OK; Close();
        }
        catch (InvalidDataException error) { _message.ForeColor = UiTheme.Error; _message.Text = error.Message; }
    }
    private int N(string name) => (int)((AntdUI.InputNumber)_inputs[name]).Value;
    private int S(string name) => ((AntdUI.Select)_inputs[name]).SelectedIndex;
    private bool B(string name) => ((AntdUI.Switch)_inputs[name]).Checked;
    private string C(string name) { var value = ((AntdUI.ColorPicker)_inputs[name]).Value; return $"#{value.R:X2}{value.G:X2}{value.B:X2}"; }
    private void Number(TableLayoutPanel fields, string title, string key, int min, int max, int value) { var input = UiTheme.Number(min, max); input.Value = value; _inputs.Add(key, input); UiTheme.Field(fields, title, input); }
    private void Select(TableLayoutPanel fields, string title, string key, string[] values, int value) { var input = UiTheme.Choice(values); input.SelectedIndex = value; _inputs.Add(key, input); UiTheme.Field(fields, title, input); }
    private void Toggle(TableLayoutPanel fields, string title, string key, bool value) { var input = UiTheme.Switch(); input.Checked = value; _inputs.Add(key, input); UiTheme.Field(fields, title, input); }
    private void Color(TableLayoutPanel fields, string title, string key, string value) { var input = UiTheme.ColorField(); input.Value = ColorTranslator.FromHtml(value); _inputs.Add(key, input); UiTheme.Field(fields, title, input); }
}
