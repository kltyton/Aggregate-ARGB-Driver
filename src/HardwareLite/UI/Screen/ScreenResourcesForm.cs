using System.Drawing.Text;
using HardwareLite.Configuration;
using HardwareLite.Display.Media;
using HardwareLite.UI.Shell;
namespace HardwareLite.UI.Screen;

internal sealed class ScreenResourcesForm : OptionsForm<ScreenSettings>
{
    private readonly ListBox _fonts = List(), _backgrounds = List();
    private readonly ToolTip _fontTip = new();
    public ScreenResourcesForm(ScreenSettings value) : base("水冷屏幕 · 字体与背景", value, ValidateResources)
    {
        UiTheme.Section(LeftFields, "字体文件");
        UiTheme.Field(LeftFields, "已添加字体", _fonts); LeftFields.RowStyles[LeftFields.RowCount - 1].Height = 220;
        UiTheme.Field(LeftFields, "字体管理", UiTheme.Actions(UiTheme.Button("添加", AddFont), UiTheme.Button("移除", RemoveFont)));
        UiTheme.Section(LeftFields, "字体使用", "添加后可在自定义布局中选择字体。移除只取消引用，不删除原文件；已有项目请改选可用字体。");
        UiTheme.Section(RightFields, "内置 / 自定义背景");
        UiTheme.Field(RightFields, "背景列表", _backgrounds); RightFields.RowStyles[RightFields.RowCount - 1].Height = 220;
        var backgroundActions = UiTheme.Actions(UiTheme.Button("添加 / 编辑", EditBackground), UiTheme.Button("移除", RemoveBackground));
        backgroundActions.WrapContents = true;
        UiTheme.Field(RightFields, "背景管理", backgroundActions); RightFields.RowStyles[RightFields.RowCount - 1].Height = 88;
        UiTheme.Section(RightFields, "背景使用", "这里管理背景名称与媒体路径。主窗口的背景选择器会同步更新，媒体原文件不会删除。");
        _fonts.SelectedIndexChanged += (_, _) => _fontTip.SetToolTip(_fonts, _fonts.SelectedIndex >= 0 ? Value.FontFiles[_fonts.SelectedIndex] : "");
        RefreshLists(); Finish();
    }
    private static ListBox List() => new() { BackColor = UiTheme.Surface, ForeColor = UiTheme.Text, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, HorizontalScrollbar = true };
    private void RefreshLists()
    {
        _fonts.Items.Clear(); _fonts.Items.AddRange(Value.FontFiles.Select(path => Path.GetFileName(path)!).ToArray());
        _backgrounds.Items.Clear(); _backgrounds.Items.AddRange(Value.BuiltInBackgrounds.Keys.ToArray());
    }
    private void AddFont()
    {
        using var dialog = new OpenFileDialog { Filter = "字体文件|*.ttf;*.otf", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Update(v => v with { FontFiles = v.FontFiles.Concat(dialog.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }); RefreshLists();
    }
    private void RemoveFont()
    {
        if (_fonts.SelectedIndex < 0) return;
        string path = Value.FontFiles[_fonts.SelectedIndex];
        Update(v => v with { FontFiles = v.FontFiles.Where(file => !string.Equals(file, path, StringComparison.OrdinalIgnoreCase)).ToArray() }); RefreshLists();
    }
    private void EditBackground()
    {
        string? selected = _backgrounds.SelectedItem as string;
        using var dialog = new BackgroundForm(selected ?? "", selected is null ? "" : Value.BuiltInBackgrounds[selected]);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.Result.Name != selected && Value.BuiltInBackgrounds.ContainsKey(dialog.Result.Name))
        { MessageBox.Show(this, "背景名称已存在，请使用其他名称。", "背景设置"); return; }
        Update(v => { var backgrounds = new Dictionary<string, string>(v.BuiltInBackgrounds); if (selected is not null) backgrounds.Remove(selected); backgrounds[dialog.Result.Name] = dialog.Result.Path; return v with { BuiltInBackgrounds = backgrounds }; }); RefreshLists();
    }
    private void RemoveBackground()
    {
        if (_backgrounds.SelectedItem is not string name) return;
        Update(v => { var backgrounds = new Dictionary<string, string>(v.BuiltInBackgrounds); backgrounds.Remove(name); return v with { BuiltInBackgrounds = backgrounds }; }); RefreshLists();
    }
    private static void ValidateResources(ScreenSettings value)
    {
        using var fonts = new PrivateFontCollection();
        foreach (string path in value.FontFiles)
        {
            if (!File.Exists(path)) throw new InvalidDataException("字体文件不存在：" + Path.GetFileName(path));
            try { fonts.AddFontFile(path); } catch (ArgumentException error) { throw new InvalidDataException("字体文件无法读取：" + Path.GetFileName(path), error); }
        }
        if (value.BuiltInBackgrounds.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || !File.Exists(pair.Value))) throw new InvalidDataException("背景名称或文件路径无效。");
    }
    protected override void Dispose(bool disposing) { if (disposing) _fontTip.Dispose(); base.Dispose(disposing); }
    private sealed record BackgroundValue(string Name, string Path);
    private sealed class BackgroundForm : OptionsForm<BackgroundValue>
    {
        public BackgroundForm(string name, string path) : base("背景名称与文件", new(name, path), v =>
        { if (string.IsNullOrWhiteSpace(v.Name) || !File.Exists(v.Path)) throw new InvalidDataException("请输入名称并选择有效媒体文件。"); })
        {
            TextField(LeftFields, "背景名称", v => v.Name, (v, text) => v with { Name = text.Trim() });
            TextField(LeftFields, "媒体文件", v => v.Path, (v, text) => v with { Path = text.Trim() });
            UiTheme.Field(LeftFields, "选择文件", UiTheme.Button("浏览", () =>
            {
                using var dialog = new OpenFileDialog { Filter = MediaFile.DialogFilter, CheckFileExists = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Update(v => v with { Path = dialog.FileName });
                var input = LeftFields.Controls.OfType<AntdUI.Input>().Last(); input.Text = dialog.FileName;
            }));
            Finish();
        }
    }
}
