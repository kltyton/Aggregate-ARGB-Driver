using System.Runtime.InteropServices;

namespace HardwareLite.UI.Shell;

internal static class UiTheme
{
    public static readonly Color Background = ColorTranslator.FromHtml("#0D1117");
    public static readonly Color Surface = ColorTranslator.FromHtml("#151C25");
    public static readonly Color InputBack = ColorTranslator.FromHtml("#1D2733");
    public static readonly Color Border = ColorTranslator.FromHtml("#2A3849");
    public static readonly Color Text = ColorTranslator.FromHtml("#EDF2F7");
    public static readonly Color Muted = ColorTranslator.FromHtml("#9CACBD");
    public static readonly Color Accent = ColorTranslator.FromHtml("#5AD5C7");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#81E4D9");
    public static readonly Color Selected = ColorTranslator.FromHtml("#1C3A3C");
    public static readonly Color Error = ColorTranslator.FromHtml("#F28B82");

    public static void Configure(Form form, string title, Size size, Size minimum)
    {
        AntdUI.Config.Mode = AntdUI.TMode.Dark;
        AntdUI.Config.Animation = SystemInformation.UIEffectsEnabled;
        AntdUI.Config.EmojiEnabled = false;
        form.AutoScaleDimensions = new SizeF(96, 96); form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Text = title; form.Font = new Font("Microsoft YaHei UI", 10);
        form.BackColor = Background; form.ForeColor = Text; form.ClientSize = size; form.MinimumSize = minimum;
        form.StartPosition = FormStartPosition.CenterParent;
        form.HandleCreated += (_, _) => { int dark = 1; _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); };
    }
    public static AntdUI.Button Button(string text, Action? click = null, bool primary = false, string? name = null)
    {
        var button = new AntdUI.Button { Text = text, Name = name ?? text, AccessibleName = text, IsAccessible = true, AccessibleRole = AccessibleRole.PushButton, Radius = 8,
            Size = new Size(Math.Max(88, text.Length * 13 + 32), 36), Margin = new Padding(0, 0, 8, 0),
            Type = primary ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default,
            BackColor = Accent, DefaultBack = InputBack, ForeColor = primary ? Background : Text,
            BackHover = primary ? AccentHover : Border, BorderWidth = primary ? 0 : 1, DefaultBorderColor = Border };
        if (click is not null) button.Click += (_, _) => click(); return button;
    }
    public static AntdUI.Select Choice(params string[] items)
    {
        var control = new AntdUI.Select { List = true, AutoSize = false, IsAccessible = true, AccessibleRole = AccessibleRole.ComboBox, Size = new Size(280, 36), Radius = 6,
            BackColor = InputBack, ForeColor = Text, BorderColor = Border, BorderActive = Accent, BorderHover = Accent,
            MaxCount = 10, WheelModifyEnabled = false };
        control.Items.AddRange(items.Cast<object>().ToArray()); return control;
    }
    public static AntdUI.InputNumber Number(int minimum, int maximum) => new() { AutoSize = false, IsAccessible = true, AccessibleRole = AccessibleRole.SpinButton, Minimum = minimum, Maximum = maximum, Size = new Size(120, 36), Radius = 6,
        BackColor = InputBack, ForeColor = Text, BorderColor = Border, BorderActive = Accent, BorderHover = Accent, WheelModifyEnabled = false };
    public static AntdUI.Input Input(bool readOnly = false) => new() { AutoSize = false, IsAccessible = true, AccessibleRole = AccessibleRole.Text, ReadOnly = readOnly, Size = new Size(280, 36), Radius = 6,
        BackColor = InputBack, ForeColor = Text, BorderColor = Border, BorderActive = Accent, BorderHover = Accent };
    public static AntdUI.Switch Switch() => new() { AutoSize = false, IsAccessible = true, AccessibleRole = AccessibleRole.CheckButton, Size = new Size(60, 32), MinimumSize = new Size(60, 32), Fill = Accent, Margin = new Padding(0) };
    public static AntdUI.ColorPicker ColorField() => new() { AutoSize = false, IsAccessible = true, AccessibleRole = AccessibleRole.PushButton, Size = new Size(150, 36), ShowText = true, DisabledAlpha = true, Radius = 6,
        BackColor = InputBack, ForeColor = Text, BorderColor = Border, BorderActive = Accent, BorderHover = Accent };
    public static Label Label(string text = "", bool muted = false, float size = 10) => new() { Text = text, AutoSize = true,
        ForeColor = muted ? Muted : Text, Font = new Font("Microsoft YaHei UI", size), BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
    public static AntdUI.Panel Region(Control content, int padding = 16)
    {
        var panel = new AntdUI.Panel { Back = Surface, Radius = 12, Dock = DockStyle.Fill, Padding = new Padding(Math.Max(1, padding)), Margin = new Padding(0), BorderWidth = 1, BorderColor = Border };
        panel.Controls.Add(content); return panel;
    }
    public static TableLayoutPanel Fields(int labelWidth = 126)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Surface, Margin = new Padding(0), Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table;
    }
    public static void ScaleDynamic(Control control, Form owner)
    {
        // Controls created after the form loaded missed its initial autoscale.
        if (owner.IsHandleCreated && owner.DeviceDpi != 96)
            control.Scale(new SizeF(owner.DeviceDpi / 96f, owner.DeviceDpi / 96f));
    }
    public static void Field(TableLayoutPanel table, string title, Control control)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(control is Label ? SizeType.AutoSize : SizeType.Absolute, 44));
        var label = Label(title, true); label.Margin = new Padding(0, 9, 8, 12); table.Controls.Add(label, 0, row);
        control.Margin = new Padding(0, 0, 0, 8); control.AccessibleName = title; control.Name = title;
        if (control is AntdUI.Switch) control.Anchor = AnchorStyles.Left | AnchorStyles.Top; else control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
    }
    public static void Section(TableLayoutPanel table, string title, string? detail = null)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); var label = Label(title, false, 12);
        label.Font = new Font(label.Font, FontStyle.Bold); label.ForeColor = Accent;
        label.Margin = new Padding(0, row == 0 ? 0 : 20, 0, 14); table.Controls.Add(label, 0, row); table.SetColumnSpan(label, 2);
        if (detail is null) return; row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var note = Label(detail, true, 9); note.MaximumSize = new Size(620, 0); table.Controls.Add(note, 0, row); table.SetColumnSpan(note, 2);
        table.SizeChanged += (_, _) => note.MaximumSize = new Size(Math.Max(1, table.ClientSize.Width), 0);
    }
    public static FlowLayoutPanel Actions(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0), BackColor = Color.Transparent, WrapContents = false };
        panel.Controls.AddRange(controls); return panel;
    }
    public static AntdUI.Panel Scroll(Control content)
    {
        var panel = new AntdUI.Panel { Dock = DockStyle.Fill, AutoScroll = true, Back = Surface, Radius = 12, Padding = new Padding(20), Margin = new Padding(0) };
        panel.Controls.Add(content); return panel;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
