using HardwareLite.Configuration;
using HardwareLite.Display;
using HardwareLite.UI.Shell;

namespace HardwareLite.UI.Screen;

internal sealed partial class LayoutEditor
{
    private void ShowProperties()
    {
        foreach (Control control in _properties.Controls.Cast<Control>().ToArray()) control.Dispose();
        _properties.Controls.Clear(); var fields = UiTheme.Fields(104); fields.Padding = new Padding(8, 12, 12, 0);
        _properties.SuspendLayout();
        fields.SuspendLayout();
        _properties.Controls.Add(fields);
        try
        {
            if (_selected < 0)
            {
                UiTheme.Section(fields, "背景"); Color("背景颜色", _layout.BackgroundColor, value => { _layout = _layout with { BackgroundColor = value }; Changed(); });
                Select("图片适配", ["填充", "适应", "拉伸", "原始尺寸"], (int)_layout.ImageFit, value => { _layout = _layout with { ImageFit = (ImageFit)value }; Changed(); });
                Number("缩放 %", 10, 500, _layout.ImageScale, value => { _layout = _layout with { ImageScale = value }; Changed(); });
                Number("水平偏移", -1440, 1440, _layout.ImageX, value => { _layout = _layout with { ImageX = value }; Changed(); });
                Number("垂直偏移", -1440, 1440, _layout.ImageY, value => { _layout = _layout with { ImageY = value }; Changed(); });
                Number("旋转角度", 0, 359, _layout.ImageRotation, value => { _layout = _layout with { ImageRotation = value }; Changed(); });
                Number("背景压暗 %", 0, 100, _layout.BackgroundShade, value => { _layout = _layout with { BackgroundShade = value }; Changed(); });
                UiTheme.Section(fields, "布局容量与范围");
                Number("最多项目数", 1, 256, _layout.MaxElements, value => { _layout = _layout with { MaxElements = value }; Changed(); });
                Number("最小尺寸", 1, 720, _layout.MinimumElementSize, value => { _layout = _layout with { MinimumElementSize = value }; Changed(); });
                Number("最小字号", 1, 1024, _layout.MinimumFontSize, value => { _layout = _layout with { MinimumFontSize = value }; Changed(); });
                Number("最大字号", 1, 1024, _layout.MaximumFontSize, value => { _layout = _layout with { MaximumFontSize = value }; Changed(); });
                return;
            }
            ScreenElement Item() => _layout.Elements[_selected]; var item = Item();
            UiTheme.Section(fields, "内容");
            Select("监控项目", MetricText.Names, (int)item.Metric, value => Replace(Item() with { Metric = (ScreenMetric)value }));
            Text("标题", item.Title, value => Replace(Item() with { Title = value }));
            Text("项目说明", item.Caption, value => Replace(Item() with { Caption = value }));
            Text("自定义文字", item.Text, value => Replace(Item() with { Text = value }));
            Text("日期时间格式", item.Format, value => Replace(Item() with { Format = value }));
            Text("数值格式", item.NumericFormat, value => Replace(Item() with { NumericFormat = value }));
            Number("小数位", -1, 6, item.DecimalPlaces, value => Replace(Item() with { DecimalPlaces = value }));
            Text("自定义单位", item.CustomUnit, value => Replace(Item() with { CustomUnit = value }));
            var hideUnit = UiTheme.Switch(); hideUnit.Checked = item.HideUnit; hideUnit.CheckedChanged += (_, _) => Replace(Item() with { HideUnit = hideUnit.Checked }); UiTheme.Field(fields, "隐藏单位", hideUnit);
            Select("独立温度单位", ["跟随屏幕", "摄氏 °C", "华氏 °F"], item.TemperatureUnit.HasValue ? (int)item.TemperatureUnit.Value + 1 : 0,
                value => Replace(Item() with { TemperatureUnit = value == 0 ? null : (TemperatureUnit)(value - 1) }));
            UiTheme.Section(fields, "位置与尺寸");
            Number("水平位置", 0, 720 - item.Width, item.X, value => Replace(Item() with { X = value }));
            Number("垂直位置", 0, 720 - item.Height, item.Y, value => Replace(Item() with { Y = value }));
            Number("宽度", _layout.MinimumElementSize, 720 - item.X, item.Width, value => Replace(Item() with { Width = value }));
            Number("高度", _layout.MinimumElementSize, 720 - item.Y, item.Height, value => Replace(Item() with { Height = value }));
            Select("文字对齐", ["左对齐", "居中", "右对齐"], (int)item.Alignment, value => Replace(Item() with { Alignment = (TextAlignment)value }));
            UiTheme.Section(fields, "字体与颜色");
            Text("数值字体", item.FontFamily, value => Replace(Item() with { FontFamily = value }));
            Number("数值字号", _layout.MinimumFontSize, _layout.MaximumFontSize, item.FontSize, value => Replace(Item() with { FontSize = value }));
            var bold = UiTheme.Switch(); bold.Checked = item.Bold; bold.CheckedChanged += (_, _) => Replace(Item() with { Bold = bold.Checked }); UiTheme.Field(fields, "加粗", bold);
            Color("数值颜色", item.Color, value => Replace(Item() with { Color = value }));
            Color("标题颜色", item.TitleColor, value => Replace(Item() with { TitleColor = value }));
            Text("标题字体", item.TitleFontFamily, value => Replace(Item() with { TitleFontFamily = value }));
            Number("标题字号", 0, _layout.MaximumFontSize, item.TitleFontSize, value => Replace(Item() with { TitleFontSize = value }));
            Text("单位字体", item.UnitFontFamily, value => Replace(Item() with { UnitFontFamily = value }));
            Number("单位字号", 0, _layout.MaximumFontSize, item.UnitFontSize, value => Replace(Item() with { UnitFontSize = value }));
            UiTheme.Section(fields, "项目说明样式");
            Text("说明字体", item.CaptionFontFamily, value => Replace(Item() with { CaptionFontFamily = value }));
            Number("说明字号", _layout.MinimumFontSize, _layout.MaximumFontSize, item.CaptionFontSize, value => Replace(Item() with { CaptionFontSize = value }));
            Number("说明区域高度", 0, 720, item.CaptionHeight, value => Replace(Item() with { CaptionHeight = value }));
            Color("说明颜色", item.CaptionColor, value => Replace(Item() with { CaptionColor = value }));
            if (item.Metric == ScreenMetric.AnalogClock)
            {
                UiTheme.Section(fields, "指针时钟");
                ClockToggle("显示表盘", item.Clock.FaceVisible, value => Replace(Item() with { Clock = Item().Clock with { FaceVisible = value } }));
                ClockToggle("显示秒针", item.Clock.SecondHandVisible, value => Replace(Item() with { Clock = Item().Clock with { SecondHandVisible = value } }));
                ClockToggle("小时刻度", item.Clock.HourMarksVisible, value => Replace(Item() with { Clock = Item().Clock with { HourMarksVisible = value } }));
                ClockToggle("小时数字", item.Clock.HourNumbersVisible, value => Replace(Item() with { Clock = Item().Clock with { HourNumbersVisible = value } }));
                Number("表盘线宽", 1, 20, item.Clock.FaceWidth, value => Replace(Item() with { Clock = Item().Clock with { FaceWidth = value } }));
                Number("时针线宽", 1, 20, item.Clock.HourWidth, value => Replace(Item() with { Clock = Item().Clock with { HourWidth = value } }));
                Number("分针线宽", 1, 20, item.Clock.MinuteWidth, value => Replace(Item() with { Clock = Item().Clock with { MinuteWidth = value } }));
                Number("秒针线宽", 1, 20, item.Clock.SecondWidth, value => Replace(Item() with { Clock = Item().Clock with { SecondWidth = value } }));
                Number("时针长度 %", 10, 100, (int)Math.Round(item.Clock.HourLength * 100), value => Replace(Item() with { Clock = Item().Clock with { HourLength = value / 100d } }));
                Number("分针长度 %", 10, 100, (int)Math.Round(item.Clock.MinuteLength * 100), value => Replace(Item() with { Clock = Item().Clock with { MinuteLength = value / 100d } }));
                Number("秒针长度 %", 10, 100, (int)Math.Round(item.Clock.SecondLength * 100), value => Replace(Item() with { Clock = Item().Clock with { SecondLength = value / 100d } }));
            }
            void ClockToggle(string title, bool value, Action<bool> changed) { var toggle = UiTheme.Switch(); toggle.Checked = value; toggle.CheckedChanged += (_, _) => changed(toggle.Checked); UiTheme.Field(fields, title, toggle); }

            void Text(string title, string value, Action<string> changed) { var input = UiTheme.Input(); input.Text = value; input.TextChanged += (_, _) => changed(input.Text); UiTheme.Field(fields, title, input); }
            void Number(string title, int min, int max, int value, Action<int> changed) { var input = UiTheme.Number(min, max); input.Value = value; input.ValueChanged += (_, _) => changed((int)input.Value); UiTheme.Field(fields, title, input); }
            void Select(string title, string[] values, int value, Action<int> changed) { var input = UiTheme.Choice(values); input.SelectedIndex = value; input.SelectedIndexChanged += (_, _) => { if (input.SelectedIndex >= 0) changed(input.SelectedIndex); }; UiTheme.Field(fields, title, input); }
            void Color(string title, string value, Action<string> changed) { var input = UiTheme.ColorField(); input.Value = ColorTranslator.FromHtml(value); input.ValueChanged += (_, _) => changed(Hex(input.Value)); UiTheme.Field(fields, title, input); }
        }
        finally
        {
            UiTheme.ScaleDynamic(fields, this);
            fields.ResumeLayout(true);
            _properties.ResumeLayout(true);
        }
    }
}
