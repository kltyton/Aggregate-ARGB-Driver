using System.Diagnostics;
using HardwareLite.Configuration;
using HardwareLite.Display;
using HardwareLite.Display.Media;
using HardwareLite.Monitoring;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI.Screen;

internal sealed class MediaCropForm : Form
{
    private readonly Size _sourceSize;
    private readonly PictureBox _photo = new() { Name = "CropSource", Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 30, 34), TabStop = true };
    private readonly PictureBox _preview = new() { Name = "CropPreview", Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly AntdUI.InputNumber _start = UiTheme.Number(0, 100), _end = UiTheme.Number(0, 100), _rotation = UiTheme.Number(0, 359);
    private readonly AntdUI.Switch _square = UiTheme.Switch();
    private readonly AntdUI.InputNumber _zoom = UiTheme.Number(100, 800);
    private float _zoomFactor = 1;
    private PointF _pan, _panStart;
    private Point? _panOrigin, _zoomAnchor;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ScreenCanvas _canvas = new();
    private readonly string _path;
    private readonly Label _message = UiTheme.Label("", true, 9);
    private PointF? _origin;
    private MediaEdit? _dragEdit;
    private bool _moving, _resizing;
    private MediaEdit _edit;
    private Settings _previewSettings = new();
    private readonly ScreenOptions _options;
    private bool _syncing;
    public MediaEdit Result => _edit;

    public MediaCropForm(string path, MediaEdit edit, ScreenOptions? options = null)
    {
        _path = path; _edit = edit; _options = options ?? new();
        _timer.Interval = (int)Math.Ceiling(1000d / _options.PreviewFps);
        try
        {
            Changed();
            _sourceSize = _canvas.CurrentMediaFrame?.Size ?? throw new InvalidDataException("媒体中没有可读取的图像帧。");
        }
        catch { Dispose(); throw; }
        SuspendLayout(); UiTheme.Configure(this, MediaFile.IsVideo(path) ? "水冷屏幕 · 视频裁剪" : "水冷屏幕 · 图片 / GIF 裁剪", new Size(1100, 630), new Size(980, 580));
        _start.DecimalPlaces = _end.DecimalPlaces = 6; _start.Maximum = 99.999999m; _end.Minimum = 0.000001m; _square.Checked = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var help = UiTheme.Label("滚轮放大，右键拖动视图；拖动裁剪框移动，拖动右下角调整大小；Shift 重新选区。", true, 9);
        root.Controls.Add(help, 0, 0); root.SetColumnSpan(help, 2); var sourceRegion = UiTheme.Region(_photo, 8); sourceRegion.Margin = new Padding(0, 0, 12, 0);
        root.Controls.Add(sourceRegion, 0, 1); root.Controls.Add(UiTheme.Region(_preview, 8), 1, 1);
        _zoom.Value = 100;
        var cropFields = UiTheme.Fields(108); cropFields.Padding = new Padding(0, 12, 12, 0); UiTheme.Field(cropFields, "正方形裁剪", _square); UiTheme.Field(cropFields, "旋转角度", _rotation); UiTheme.Field(cropFields, "预览缩放 %", _zoom);
        var trimFields = UiTheme.Fields(108); trimFields.Padding = new Padding(0, 12, 0, 0); UiTheme.Field(trimFields, "动画起点 %", _start); UiTheme.Field(trimFields, "动画终点 %", _end);
        root.Controls.Add(cropFields, 0, 2); root.Controls.Add(trimFields, 1, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0), BackColor = UiTheme.Background };
        Button("完整图片", () => { _edit = _edit with { X = 0, Y = 0, Width = 1, Height = 1 }; Changed(); });
        Button("居中正方形", () => { double size = Math.Min(_sourceSize.Width, _sourceSize.Height); _edit = _edit with { X = (_sourceSize.Width - size) / 2 / _sourceSize.Width, Y = (_sourceSize.Height - size) / 2 / _sourceSize.Height, Width = size / _sourceSize.Width, Height = size / _sourceSize.Height }; Changed(); });
        Button("适应窗口", () => { _zoom.Value = 100; _pan = default; _photo.Invalidate(); });
        Button("精确设置", () =>
        {
            _timer.Stop();
            try
            {
                using var dialog = new CropCoordinatesForm(_edit, _sourceSize, _canvas.MediaDurationMs, _canvas.MediaFrameDelays);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _edit = dialog.Result; _syncing = true;
                try { _start.Value = (decimal)(_edit.Start * 100); _end.Value = (decimal)(_edit.End * 100); _rotation.Value = _edit.Rotation; }
                finally { _syncing = false; }
                Changed();
            }
            finally { if (_options.PreviewEnabled) _timer.Start(); }
        });
        Button("保存裁剪", () => { try { if (_start.Value >= _end.Value) throw new InvalidDataException("动画终点必须晚于起点。"); _edit.Validate(); DialogResult = DialogResult.OK; Close(); } catch (InvalidDataException error) { _message.Text = error.Message; } });
        Button("取消", () => { DialogResult = DialogResult.Cancel; Close(); }); actions.Controls.Add(_message);
        root.Controls.Add(actions, 0, 3); root.SetColumnSpan(actions, 2); Controls.Add(root);
        _start.Value = (decimal)(edit.Start * 100); _end.Value = (decimal)(edit.End * 100); _rotation.Value = edit.Rotation;
        _start.ValueChanged += (_, _) => UpdatePlayback(start: true); _end.ValueChanged += (_, _) => UpdatePlayback(end: true); _rotation.ValueChanged += (_, _) => UpdatePlayback();
        _zoom.ValueChanged += (_, _) => Zoom();
        _photo.Paint += (_, args) =>
        {
            args.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            if (_canvas.CurrentMediaFrame is { } frame) args.Graphics.DrawImage(frame, PhotoRectangle());
            DrawSelection(args.Graphics);
        };
        _photo.MouseWheel += (_, args) =>
        {
            _zoomAnchor = args.Location;
            try { _zoom.Value = Math.Clamp(_zoom.Value + args.Delta / 120m * 25, 100, 800); }
            finally { _zoomAnchor = null; }
        };
        _photo.Resize += (_, _) => { ClampPan(); _photo.Invalidate(); };
        _photo.MouseDown += (_, args) =>
        {
            _photo.Focus();
            if (args.Button == MouseButtons.Right)
            {
                _panOrigin = args.Location; _panStart = _pan;
                _photo.Cursor = Cursors.Hand; _photo.Capture = true; return;
            }
            if (args.Button != MouseButtons.Left || !PhotoRectangle().Contains(args.Location)) return;
            var crop = CropRectangle(); _origin = Position(args.Location); _dragEdit = _edit;
            _resizing = (ModifierKeys & Keys.Shift) == 0 && Math.Abs(args.X - crop.Right) <= 12 && Math.Abs(args.Y - crop.Bottom) <= 12;
            _moving = !_resizing && (ModifierKeys & Keys.Shift) == 0 && crop.Contains(args.Location); _photo.Capture = true;
        };
        _photo.MouseMove += (_, args) =>
        {
            if (_panOrigin is Point panOrigin)
            {
                _pan = new PointF(_panStart.X + args.X - panOrigin.X, _panStart.Y + args.Y - panOrigin.Y);
                ClampPan(); _photo.Invalidate(); return;
            }
            if (_origin is not PointF origin) return;
            PointF point = Position(args.Location);
            if (_dragEdit is { } initial && _moving)
            {
                _edit = initial with { X = Math.Clamp(initial.X + point.X - origin.X, 0, 1 - initial.Width), Y = Math.Clamp(initial.Y + point.Y - origin.Y, 0, 1 - initial.Height) }; Changed(); return;
            }
            if (_dragEdit is { } original && _resizing)
            {
                double w = Math.Clamp(original.Width + point.X - origin.X, 1.0 / _sourceSize.Width, 1 - original.X);
                double h = Math.Clamp(original.Height + point.Y - origin.Y, 1.0 / _sourceSize.Height, 1 - original.Y);
                if (_square.Checked) { double pixels = Math.Min(w * _sourceSize.Width, h * _sourceSize.Height); w = pixels / _sourceSize.Width; h = pixels / _sourceSize.Height; }
                _edit = original with { Width = w, Height = h }; Changed(); return;
            }
            double x = Math.Min(origin.X, point.X), y = Math.Min(origin.Y, point.Y), width = Math.Abs(point.X - origin.X), height = Math.Abs(point.Y - origin.Y);
            if (_square.Checked) { double pixels = Math.Min(width * _sourceSize.Width, height * _sourceSize.Height); width = pixels / _sourceSize.Width; height = pixels / _sourceSize.Height; }
            if (width < 1.0 / _sourceSize.Width || height < 1.0 / _sourceSize.Height) return;
            _edit = _edit with { X = x, Y = y, Width = Math.Min(width, 1 - x), Height = Math.Min(height, 1 - y) }; Changed();
        };
        _photo.MouseUp += (_, _) => { _origin = null; _dragEdit = null; _panOrigin = null; _photo.Cursor = Cursors.Default; _photo.Capture = false; };
        _photo.MouseCaptureChanged += (_, _) =>
        {
            if (_photo.Capture) return;
            _origin = null; _dragEdit = null; _panOrigin = null; _photo.Cursor = Cursors.Default;
        };
        _timer.Tick += (_, _) => Redraw(); if (_options.PreviewEnabled) _timer.Start(); Changed(); ResumeLayout(true);
        void Button(string title, Action action) { actions.Controls.Add(UiTheme.Button(title, action, title == "保存裁剪")); }
    }

    private RectangleF PhotoRectangle()
    {
        float scale = _zoomFactor * Math.Min(_photo.ClientSize.Width / (float)_sourceSize.Width, _photo.ClientSize.Height / (float)_sourceSize.Height);
        float width = _sourceSize.Width * scale, height = _sourceSize.Height * scale;
        return new((_photo.ClientSize.Width - width) / 2 + _pan.X, (_photo.ClientSize.Height - height) / 2 + _pan.Y, width, height);
    }
    private void Zoom()
    {
        var before = PhotoRectangle();
        if (before.Width <= 0 || before.Height <= 0) { _zoomFactor = (float)_zoom.Value / 100; return; }
        var anchor = _zoomAnchor ?? new Point(_photo.ClientSize.Width / 2, _photo.ClientSize.Height / 2);
        var position = new PointF((anchor.X - before.X) / before.Width, (anchor.Y - before.Y) / before.Height);
        _zoomFactor = (float)_zoom.Value / 100;
        var after = PhotoRectangle();
        _pan = new PointF(_pan.X + anchor.X - after.X - position.X * after.Width, _pan.Y + anchor.Y - after.Y - position.Y * after.Height);
        ClampPan(); _photo.Invalidate();
    }
    private void ClampPan()
    {
        var rectangle = PhotoRectangle();
        float x = Math.Max(0, (rectangle.Width - _photo.ClientSize.Width) / 2);
        float y = Math.Max(0, (rectangle.Height - _photo.ClientSize.Height) / 2);
        _pan = new PointF(Math.Clamp(_pan.X, -x, x), Math.Clamp(_pan.Y, -y, y));
    }
    private PointF Position(Point point)
    { var rectangle = PhotoRectangle(); return new(Math.Clamp((point.X - rectangle.X) / rectangle.Width, 0, 1), Math.Clamp((point.Y - rectangle.Y) / rectangle.Height, 0, 1)); }
    private void DrawSelection(Graphics graphics)
    {
        var crop = CropRectangle(); using var pen = new Pen(UiTheme.Accent, 3); using var brush = new SolidBrush(UiTheme.Accent);
        graphics.DrawRectangle(pen, crop.X, crop.Y, crop.Width, crop.Height); graphics.FillRectangle(brush, crop.Right - 6, crop.Bottom - 6, 12, 12);
    }
    private RectangleF CropRectangle()
    {
        var rectangle = PhotoRectangle(); return new(rectangle.X + (float)_edit.X * rectangle.Width, rectangle.Y + (float)_edit.Y * rectangle.Height,
            (float)_edit.Width * rectangle.Width, (float)_edit.Height * rectangle.Height);
    }
    private void UpdatePlayback(bool start = false, bool end = false)
    {
        if (_syncing) return;
        if (_start.Value >= _end.Value) { _message.ForeColor = UiTheme.Error; _message.Text = "动画终点必须晚于起点。"; return; }
        _message.Text = "";
        _edit = _edit with { Start = start ? (double)_start.Value / 100 : _edit.Start, End = end ? (double)_end.Value / 100 : _edit.End, Rotation = (int)_rotation.Value }; Changed();
    }
    private void Changed()
    {
        _previewSettings = new Settings { Screen = new() { Mode = ScreenMode.Media, MediaPath = _path, Options = _options, MediaEdits = new() { [_path] = _edit }, Layout = new() { BackgroundColor = "#000000", ImageFit = ImageFit.Stretch } } };
        _canvas.Invalidate(); _photo.Invalidate(); Redraw();
    }
    private void Redraw()
    {
        var settings = _previewSettings;
        try
        {
            if (!_canvas.IsDue(settings, SensorSnapshot.Empty, _clock.ElapsedMilliseconds)) return;
            _preview.Image ??= new Bitmap(720, 720, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            _canvas.RenderInto((Bitmap)_preview.Image, settings, SensorSnapshot.Empty, _clock.ElapsedMilliseconds);
            _preview.Invalidate(); _photo.Invalidate();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OutOfMemoryException) { _message.Text = error.Message; }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { _timer.Dispose(); _preview.Image?.Dispose(); _photo.Image = null; _canvas.Dispose(); } base.Dispose(disposing); }
}
