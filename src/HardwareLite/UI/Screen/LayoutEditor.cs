using System.Diagnostics;
using HardwareLite.Configuration;
using HardwareLite.Display;
using HardwareLite.Monitoring;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI.Screen;

internal sealed partial class LayoutEditor : Form
{
    private readonly Settings _settings;
    private readonly Func<SensorSnapshot> _snapshot;
    private readonly ScreenCanvas _canvas = new();
    private readonly PictureBox _preview = new() { Name = "LayoutPreview", AccessibleName = "屏幕布局预览", Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(25, 30, 38), TabStop = true };
    private readonly AntdUI.Select _items = UiTheme.Choice();
    private readonly AntdUI.Panel _properties = new() { Dock = DockStyle.Fill, AutoScroll = true, Back = UiTheme.Surface, Radius = 0 };
    private readonly AntdUI.Select _metric = UiTheme.Choice();
    private readonly AntdUI.Select _sensor = UiTheme.Choice();
    private readonly RowStyle _sensorRow = new(SizeType.Absolute, 44);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Label _message = UiTheme.Label("", true, 9);
    private ScreenLayout _layout;
    private int _selected = -1;
    private Point _mouseOrigin;
    private ScreenElement? _dragOrigin;
    private bool _resize;
    private bool _selecting;
    public ScreenLayout Result => _layout;

    public LayoutEditor(Settings settings, Func<SensorSnapshot> snapshot)
    {
        _settings = settings; _snapshot = snapshot; _layout = settings.Screen.Layout with { Elements = [..settings.Screen.Layout.Elements] };
        SuspendLayout(); UiTheme.Configure(this, "水冷屏幕 · 自定义布局", new Size(1100, 630), new Size(980, 580));
        _preview.BackColor = UiTheme.Surface;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var help = UiTheme.Label("拖动项目调整位置，拖动右下角调整大小；方向键微调，Shift 为 10 像素。", true, 9);
        root.Controls.Add(help, 0, 0); root.SetColumnSpan(help, 2);
        root.Controls.Add(UiTheme.Region(_preview, 12), 0, 1);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 0, 0, 0) };
        right.BackColor = UiTheme.Surface; right.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(_sensorRow); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _items.Dock = DockStyle.Fill;
        right.Controls.Add(_items, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        AddButton(actions, "删除", Remove); AddButton(actions, "上移", () => Reorder(-1)); AddButton(actions, "下移", () => Reorder(1));
        AddButton(actions, "背景", () => { _items.SelectedIndex = -1; SelectItem(-1); });
        right.Controls.Add(actions, 0, 1);
        var addRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _metric.Items.AddRange(MetricText.Names.Cast<object>().ToArray()); _metric.Width = 230; _metric.SelectedIndex = 0;
        addRow.Controls.Add(_metric); AddButton(addRow, "添加", Add); right.Controls.Add(addRow, 0, 2);
        foreach (var item in snapshot().Sensors) _sensor.Items.Add(new AntdUI.SelectItem(item.Value.Name + " [" + item.Value.Unit + "]", new SensorOption(item.Key, item.Value.Name)));
        if (_sensor.Items.Count > 0) _sensor.SelectedIndex = 0;
        _sensor.Dock = DockStyle.Fill; right.Controls.Add(_sensor, 0, 3); right.Controls.Add(_properties, 0, 4); root.Controls.Add(right, 1, 1);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
        AddButton(footer, "保存布局", () => { try { _layout.Validate(); DialogResult = DialogResult.OK; Close(); } catch (InvalidDataException error) { _message.Text = error.Message; } });
        AddButton(footer, "取消", () => { DialogResult = DialogResult.Cancel; Close(); });
        AddButton(footer, "默认布局", () => { _layout = new ScreenLayout(); RefreshItems(0); Changed(); });
        AddButton(footer, "时钟布局", () => { _layout = _layout with { Elements = [new() { Metric = ScreenMetric.Time, Title = "", X = 40, Y = 250, Width = 640, Height = 150, FontSize = 110, Alignment = TextAlignment.Center }, new() { Metric = ScreenMetric.Date, Title = "", X = 40, Y = 420, Width = 640, Height = 100, FontSize = 48, Alignment = TextAlignment.Center }] }; RefreshItems(0); Changed(); });
        AddButton(footer, "指针时钟", () => { _layout = _layout with { Elements = [new() { Metric = ScreenMetric.AnalogClock, Title = "", X = 100, Y = 100, Width = 520, Height = 520 }] }; RefreshItems(0); Changed(); });
        footer.Controls.Add(_message); root.Controls.Add(footer, 0, 2); root.SetColumnSpan(footer, 2); Controls.Add(root);
        _items.SelectedIndexChanged += (_, _) => SelectItem(_items.SelectedIndex);
        _sensor.SelectedIndexChanged += (_, _) => { if (!_selecting && _selected >= 0 && _layout.Elements[_selected].Metric == ScreenMetric.Sensor && _sensor.SelectedValue is SensorOption option) Replace(_layout.Elements[_selected] with { SensorId = option.Id }); };
        _preview.Paint += (_, args) => DrawSelection(args.Graphics);
        _preview.MouseDown += MouseDownPreview; _preview.MouseMove += MouseMovePreview; _preview.MouseUp += (_, _) => { _dragOrigin = null; _preview.Capture = false; ShowProperties(); };
        _preview.PreviewKeyDown += (_, args) => { if (args.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down) args.IsInputKey = true; };
        _preview.KeyDown += (_, args) =>
        {
            if (_selected < 0) return;
            if (args.KeyCode == Keys.Delete) { Remove(); return; }
            int step = args.Shift ? 10 : 1; var item = _layout.Elements[_selected];
            int dx = args.KeyCode == Keys.Left ? -step : args.KeyCode == Keys.Right ? step : 0;
            int dy = args.KeyCode == Keys.Up ? -step : args.KeyCode == Keys.Down ? step : 0;
            if (dx != 0 || dy != 0) { Replace(item with { X = Math.Clamp(item.X + dx, 0, 720 - item.Width), Y = Math.Clamp(item.Y + dy, 0, 720 - item.Height) }); ShowProperties(); args.Handled = true; }
        };
        _timer.Interval = (int)Math.Ceiling(1000d / settings.Screen.Options.PreviewFps);
        _timer.Tick += (_, _) => Redraw(); RefreshItems(0); Redraw(); if (settings.Screen.Options.PreviewEnabled) _timer.Start();
        AutoScaleDimensions = new SizeF(96, 96); ResumeLayout(true);
    }

    private static void AddButton(FlowLayoutPanel panel, string text, Action click)
    { panel.Controls.Add(UiTheme.Button(text, click, text == "保存布局")); }
    private void Add()
    {
        if (_layout.Elements.Length >= _layout.MaxElements) { _message.Text = $"最多 {_layout.MaxElements} 个项目。"; return; }
        var metric = (ScreenMetric)_metric.SelectedIndex;
        var item = new ScreenElement { Metric = metric, Title = metric is ScreenMetric.Time or ScreenMetric.Date or ScreenMetric.Text or ScreenMetric.AnalogClock ? "" : MetricText.Names[(int)metric], Text = "自定义文字", X = 60, Y = 60, SensorId = (_sensor.SelectedValue as SensorOption)?.Id ?? "" };
        _layout = _layout with { Elements = [.._layout.Elements, item] }; RefreshItems(_layout.Elements.Length - 1); Changed();
    }
    private void Remove()
    { if (_selected < 0) return; int next = Math.Min(_selected, _layout.Elements.Length - 2); _layout = _layout with { Elements = _layout.Elements.Where((_, index) => index != _selected).ToArray() }; RefreshItems(next); Changed(); }
    private void Reorder(int change)
    {
        int next = _selected + change; if (_selected < 0 || next < 0 || next >= _layout.Elements.Length) return;
        var elements = _layout.Elements.ToArray(); (elements[_selected], elements[next]) = (elements[next], elements[_selected]); _layout = _layout with { Elements = elements }; RefreshItems(next); Changed();
    }
    private void RefreshItems(int selected)
    {
        _selecting = true; _items.Items.Clear(); foreach (var item in _layout.Elements) _items.Items.Add(MetricText.Names[(int)item.Metric] + (item.Title.Length > 0 ? " · " + item.Title : ""));
        _items.SelectedIndex = selected; _selecting = false; SelectItem(selected);
    }
    private void SelectItem(int index)
    {
        if (_selecting) return; _selected = index; ShowProperties();
        ShowSensorSource(index >= 0 && _layout.Elements[index].Metric == ScreenMetric.Sensor);
        _selecting = true;
        try
        {
            if (index >= 0 && _layout.Elements[index].Metric == ScreenMetric.Sensor)
                for (int i = 0; i < _sensor.Items.Count; i++)
                    if (_sensor.Items[i] is AntdUI.SelectItem { Tag: SensorOption option } && option.Id == _layout.Elements[index].SensorId) { _sensor.SelectedIndex = i; break; }
        }
        finally { _selecting = false; }
        _preview.Invalidate();
    }
    private void Replace(ScreenElement element)
    { var items = _layout.Elements.ToArray(); items[_selected] = element; _layout = _layout with { Elements = items }; ShowSensorSource(element.Metric == ScreenMetric.Sensor); Changed(); }
    private void ShowSensorSource(bool visible)
    {
        _sensor.Visible = _sensor.Enabled = visible;
        _sensorRow.Height = visible ? 44 * (IsHandleCreated ? DeviceDpi / 96f : 1) : 0;
    }
    private void Changed() { _canvas.Invalidate(); Redraw(); }
    private void Redraw()
    {
        var settings = _settings with { Screen = _settings.Screen with { Layout = _layout } };
        try
        {
            if (!_canvas.IsDue(settings, _snapshot(), _clock.ElapsedMilliseconds)) return;
            _preview.Image ??= new Bitmap(720, 720, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            _canvas.RenderInto((Bitmap)_preview.Image, settings, _snapshot(), _clock.ElapsedMilliseconds);
            _preview.Invalidate();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OutOfMemoryException) { _message.Text = error.Message; }
    }
    private RectangleF PreviewRectangle()
    { float size = Math.Min(_preview.ClientSize.Width, _preview.ClientSize.Height); return new((_preview.Width - size) / 2, (_preview.Height - size) / 2, size, size); }
    private Point Logical(Point point)
    { var rect = PreviewRectangle(); return new((int)((point.X - rect.X) * 720 / rect.Width), (int)((point.Y - rect.Y) * 720 / rect.Height)); }
    private void MouseDownPreview(object? sender, MouseEventArgs args)
    {
        _preview.Focus(); var point = Logical(args.Location);
        if (_selected >= 0)
        {
            var item = _layout.Elements[_selected];
            if (Math.Abs(point.X - item.X - item.Width) <= 18 && Math.Abs(point.Y - item.Y - item.Height) <= 18) { _resize = true; BeginDrag(point, item); return; }
        }
        _resize = false;
        for (int i = _layout.Elements.Length - 1; i >= 0; i--)
        {
            var item = _layout.Elements[i]; if (!new Rectangle(item.X, item.Y, item.Width, item.Height).Contains(point)) continue;
            _items.SelectedIndex = i; BeginDrag(point, item); return;
        }
        _items.SelectedIndex = -1; SelectItem(-1);
    }
    private void BeginDrag(Point point, ScreenElement item) { _mouseOrigin = point; _dragOrigin = item; _preview.Capture = true; }
    private void MouseMovePreview(object? sender, MouseEventArgs args)
    {
        if (_dragOrigin is not { } item || _selected < 0) return;
        var point = Logical(args.Location); int dx = point.X - _mouseOrigin.X, dy = point.Y - _mouseOrigin.Y;
        Replace(_resize ? item with { Width = Math.Clamp(item.Width + dx, _layout.MinimumElementSize, 720 - item.X), Height = Math.Clamp(item.Height + dy, _layout.MinimumElementSize, 720 - item.Y) }
            : item with { X = Math.Clamp(item.X + dx, 0, 720 - item.Width), Y = Math.Clamp(item.Y + dy, 0, 720 - item.Height) });
    }
    private void DrawSelection(Graphics graphics)
    {
        if (_selected < 0) return; var item = _layout.Elements[_selected]; var preview = PreviewRectangle(); float scale = preview.Width / 720;
        var rectangle = new RectangleF(preview.X + item.X * scale, preview.Y + item.Y * scale, item.Width * scale, item.Height * scale);
        using var pen = new Pen(UiTheme.Accent, 2); using var brush = new SolidBrush(UiTheme.Accent);
        graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height); graphics.FillRectangle(brush, rectangle.Right - 6, rectangle.Bottom - 6, 12, 12);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { _timer.Dispose(); _preview.Image?.Dispose(); _canvas.Dispose(); } base.Dispose(disposing); }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
