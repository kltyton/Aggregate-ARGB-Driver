using HardwareLite.Configuration;
namespace HardwareLite.UI.Shell;

internal abstract class OptionsForm<T> : Form where T : class
{
    private T _value;
    private readonly Action<T> _validate;
    private readonly Label _error = UiTheme.Label("", true, 9);
    protected readonly TableLayoutPanel LeftFields = UiTheme.Fields(180), RightFields = UiTheme.Fields(180);
    public T Result => _value;
    protected OptionsForm(string title, T value, Action<T> validate)
    {
        _value = value; _validate = validate;
        SuspendLayout(); UiTheme.Configure(this, title, new Size(920, 610), new Size(800, 540));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var left = UiTheme.Region(UiTheme.Scroll(LeftFields), 0); left.Margin = new Padding(0, 0, 12, 0);
        root.Controls.Add(left, 0, 0); root.Controls.Add(UiTheme.Region(UiTheme.Scroll(RightFields), 0), 1, 0);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _error.AutoSize = false; _error.Dock = DockStyle.Fill; footer.Controls.Add(_error, 0, 0);
        var cancel = UiTheme.Button("取消", () => { DialogResult = DialogResult.Cancel; Close(); }); CancelButton = cancel;
        footer.Controls.Add(UiTheme.Actions(cancel, UiTheme.Button("保存设置", Save, true)), 1, 0);
        root.Controls.Add(footer, 0, 1); root.SetColumnSpan(footer, 2); Controls.Add(root);
    }
    protected void Finish() { ResumeLayout(true); }
    protected void Update(Func<T, T> change) => _value = change(_value);
    protected T Value => _value;
    protected void Number(TableLayoutPanel fields, string title, int minimum, int maximum, Func<T, int> read, Func<T, int, T> write)
    {
        var input = UiTheme.Number(minimum, maximum); input.Value = read(_value);
        input.ValueChanged += (_, _) => Update(value => write(value, (int)input.Value)); UiTheme.Field(fields, title, input);
    }
    protected void Decimal(TableLayoutPanel fields, string title, decimal minimum, decimal maximum, int places, Func<T, double> read, Func<T, double, T> write)
    {
        var input = UiTheme.Number(0, (int)Math.Ceiling(maximum)); input.Minimum = minimum; input.Maximum = maximum; input.DecimalPlaces = places; input.Value = (decimal)read(_value);
        input.ValueChanged += (_, _) => Update(value => write(value, (double)input.Value)); UiTheme.Field(fields, title, input);
    }
    protected void Toggle(TableLayoutPanel fields, string title, Func<T, bool> read, Func<T, bool, T> write)
    {
        var input = UiTheme.Switch(); input.Checked = read(_value);
        input.CheckedChanged += (_, _) => Update(value => write(value, input.Checked)); UiTheme.Field(fields, title, input);
    }
    protected void TextField(TableLayoutPanel fields, string title, Func<T, string> read, Func<T, string, T> write)
    {
        var input = UiTheme.Input(); input.Text = read(_value);
        input.TextChanged += (_, _) => Update(value => write(value, input.Text)); UiTheme.Field(fields, title, input);
    }
    protected void Choice(TableLayoutPanel fields, string title, string[] names, Func<T, int> read, Func<T, int, T> write)
    {
        var input = UiTheme.Choice(names); input.SelectedIndex = read(_value);
        input.SelectedIndexChanged += (_, _) => { if (input.SelectedIndex >= 0) Update(value => write(value, input.SelectedIndex)); }; UiTheme.Field(fields, title, input);
    }
    protected void Source(TableLayoutPanel fields, string title, IReadOnlyDictionary<string, HardwareLite.Monitoring.SensorReading> sensors, Func<T, string> read, Func<T, string, T> write, string automatic = "使用默认来源")
    {
        string selected = read(_value);
        var keys = new List<string> { "" }; var names = new List<string> { automatic };
        foreach (var pair in sensors) { keys.Add(pair.Key); names.Add(pair.Value.Name + " [" + pair.Value.Unit + "]"); }
        if (selected.Length > 0 && !keys.Contains(selected)) { keys.Add(selected); names.Add("等待连接 · " + selected); }
        Choice(fields, title, names.ToArray(), _ => keys.IndexOf(selected), (value, index) => write(value, keys[index]));
    }
    private void Save()
    {
        try { _validate(_value); DialogResult = DialogResult.OK; Close(); }
        catch (Exception error) when (error is InvalidDataException or IOException or FormatException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        { _error.ForeColor = UiTheme.Error; _error.Text = error.Message; }
    }
}
