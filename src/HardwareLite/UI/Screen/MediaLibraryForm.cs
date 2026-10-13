using HardwareLite.UI.Shell;
using HardwareLite.Configuration;
using HardwareLite.Display.Media;

namespace HardwareLite.UI.Screen;

internal sealed class MediaLibraryForm : Form
{
    private readonly List<string> _files;
    private readonly FlowLayoutPanel _gallery = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Background };
    private readonly Label _message = UiTheme.Label("", true, 9);
    private readonly Dictionary<string, MediaEdit> _edits;
    private readonly Dictionary<string, AntdUI.Panel> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private int _selected = -1;
    private readonly int _capacity;
    private readonly ScreenOptions _options;
    public string[] Files => _files.ToArray();
    public Dictionary<string, MediaEdit> Edits => new(_edits);

    public MediaLibraryForm(IEnumerable<string> files, IReadOnlyDictionary<string, MediaEdit> edits, int capacity = 30, ScreenOptions? options = null)
    {
        _capacity = capacity; _options = options ?? new(); _files = files.ToList(); _edits = new(edits); _selected = _files.Count > 0 ? 0 : -1; SuspendLayout(); UiTheme.Configure(this, "水冷屏幕 · 媒体库", new Size(920, 600), new Size(820, 540));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.Controls.Add(UiTheme.Actions(UiTheme.Button("添加媒体", Add), UiTheme.Button("移除", Remove), UiTheme.Button("上移", () => Reorder(-1)),
            UiTheme.Button("下移", () => Reorder(1)), UiTheme.Button("裁剪 / GIF 片段", Edit)), 0, 0);
        root.Controls.Add(_gallery, 0, 1);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.Controls.Add(_message, 0, 0);
        var cancel = UiTheme.Button("取消", () => { DialogResult = DialogResult.Cancel; Close(); }); CancelButton = cancel;
        footer.Controls.Add(UiTheme.Actions(cancel, UiTheme.Button("确定", () => { DialogResult = DialogResult.OK; Close(); }, true)), 1, 0);
        root.Controls.Add(footer, 0, 2); Controls.Add(root); Rebuild(); ResumeLayout(true);
    }
    private void Add()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true, Filter = MediaFile.DialogFilter };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        int rejected = 0;
        foreach (string file in dialog.FileNames)
        {
            if (_files.Contains(file, StringComparer.OrdinalIgnoreCase)) continue;
            if (_files.Count >= _capacity) { rejected++; continue; }
            _files.Add(file);
        }
        Rebuild();
        _message.Text = rejected > 0 ? $"媒体库上限 {_capacity} 项，另有 {rejected} 项未添加。" : $"媒体库 {_files.Count}/{_capacity} 项。";
    }
    private void Remove()
    {
        if (_selected < 0) { _message.Text = "先选择媒体文件。"; return; }
        _edits.Remove(_files[_selected]);
        _files.RemoveAt(_selected);
        _selected = Math.Min(_selected, _files.Count - 1);
        Rebuild();
        _message.Text = _selected >= 0 ? _files[_selected] : "";
    }
    private void Edit()
    {
        if (_selected < 0) { _message.Text = "先选择媒体文件。"; return; }
        string path = _files[_selected];
        try
        {
            using var editor = new MediaCropForm(path, _edits.GetValueOrDefault(path) ?? new(), _options);
            if (editor.ShowDialog(this) == DialogResult.OK) _edits[path] = editor.Result;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OutOfMemoryException)
        { _message.Text = "裁剪无法打开：" + error.Message; }
    }
    private void Reorder(int change)
    {
        int next = _selected + change; if (_selected < 0 || next < 0 || next >= _files.Count) return;
        (_files[_selected], _files[next]) = (_files[next], _files[_selected]); _selected = next; Rebuild();
    }
    private void Rebuild()
    {
        _gallery.SuspendLayout();
        try
        {
            foreach (string path in _tiles.Keys.Where(path => !_files.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray())
            {
                var tile = _tiles[path]; DisposeImages(tile); tile.Dispose(); _tiles.Remove(path);
            }
            foreach (var label in _gallery.Controls.OfType<Label>().ToArray()) label.Dispose();
            _gallery.Controls.Clear();
            for (int index = 0; index < _files.Count; index++)
            {
                string path = _files[index];
                if (!_tiles.TryGetValue(path, out var tile))
                {
                    var picture = new MediaThumbnail { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = Path.GetFileName(path), Cursor = Cursors.Hand };
                    try { picture.Image = MediaFile.OpenThumbnail(path, 180, 104); }
                    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OutOfMemoryException) { _message.Text = "媒体无法读取：" + Path.GetFileName(path); }
                    var label = UiTheme.Label(Path.GetFileName(path), true, 9); label.Dock = DockStyle.Bottom; label.AutoSize = false; label.Height = 28; label.AutoEllipsis = true; label.Cursor = Cursors.Hand;
                    var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }; content.Controls.Add(picture); content.Controls.Add(label);
                    tile = UiTheme.Region(content, 8); tile.Dock = DockStyle.None; tile.Size = new Size(200, 150); tile.Margin = new Padding(0, 0, 12, 12);
                    var selectedTile = tile;
                    void SelectTile() { _selected = (int)selectedTile.Tag!; foreach (var item in _tiles.Values) item.Back = (int)item.Tag! == _selected ? UiTheme.Selected : UiTheme.Surface; _message.Text = path; }
                    picture.Click += (_, _) => SelectTile(); label.Click += (_, _) => { SelectTile(); picture.Focus(); }; picture.DoubleClick += (_, _) => { SelectTile(); Edit(); };
                    picture.KeyDown += (_, args) => { if (args.KeyCode is Keys.Enter or Keys.Space) { SelectTile(); args.Handled = args.SuppressKeyPress = true; } };
                    UiTheme.ScaleDynamic(tile, this); _tiles.Add(path, tile);
                }
                tile.Tag = index; tile.Back = index == _selected ? UiTheme.Selected : UiTheme.Surface; _gallery.Controls.Add(tile);
            }
            if (_files.Count == 0) _gallery.Controls.Add(UiTheme.Label("媒体库为空。添加图片、GIF、MP4 或 AVI 后，可设置顺序与裁剪。", true));
        }
        finally { _gallery.ResumeLayout(true); }
    }
    private void ClearGallery()
    {
        foreach (var tile in _tiles.Values)
        {
            DisposeImages(tile); tile.Dispose();
        }
        _tiles.Clear();
        foreach (var label in _gallery.Controls.OfType<Label>().ToArray()) label.Dispose();
        _gallery.Controls.Clear();
    }
    private static void DisposeImages(Control parent)
    {
        if (parent is PictureBox picture) picture.Image?.Dispose();
        foreach (Control child in parent.Controls) DisposeImages(child);
    }
    protected override void Dispose(bool disposing) { if (disposing) ClearGallery(); base.Dispose(disposing); }
}
