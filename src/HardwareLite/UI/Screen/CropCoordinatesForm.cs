using HardwareLite.Configuration;
using HardwareLite.UI.Shell;
namespace HardwareLite.UI.Screen;
internal sealed class CropCoordinatesForm : OptionsForm<MediaEdit>
{
    public CropCoordinatesForm(MediaEdit value, Size source, long durationMs, IReadOnlyList<int> delays) : base("媒体裁剪 · 精确设置", value, v => v.Validate())
    {
        UiTheme.Section(LeftFields, "源图像裁剪", $"当前解码图像：{source.Width} × {source.Height}，坐标单位为像素。");
        Decimal(LeftFields, "水平位置", 0, source.Width - 1, 2, v => v.X * source.Width, (v, n) => v with { X = n / source.Width });
        Decimal(LeftFields, "垂直位置", 0, source.Height - 1, 2, v => v.Y * source.Height, (v, n) => v with { Y = n / source.Height });
        Decimal(LeftFields, "裁剪宽度", .01m, source.Width, 2, v => v.Width * source.Width, (v, n) => v with { Width = n / source.Width });
        Decimal(LeftFields, "裁剪高度", .01m, source.Height, 2, v => v.Height * source.Height, (v, n) => v with { Height = n / source.Height });
        Number(LeftFields, "旋转角度", 0, 359, v => v.Rotation, (v, n) => v with { Rotation = n });
        UiTheme.Section(RightFields, "播放片段");
        if (durationMs > 0)
        {
            double seconds = durationMs / 1000d;
            Decimal(RightFields, "起点 · 秒", 0, (decimal)seconds, 3, v => v.Start * seconds, (v, n) => v with { Start = n / seconds });
            Decimal(RightFields, "终点 · 秒", 0, (decimal)seconds, 3, v => v.End * seconds, (v, n) => v with { End = n / seconds });
        }
        Decimal(RightFields, "起点 · %", 0, 100, 3, v => v.Start * 100, (v, n) => v with { Start = n / 100 });
        Decimal(RightFields, "终点 · %", 0, 100, 3, v => v.End * 100, (v, n) => v with { End = n / 100 });
        if (delays.Count > 1 && durationMs > 0)
        {
            Number(RightFields, "GIF 起始帧 · 从 0", 0, delays.Count - 1, v => Frame(v.Start * durationMs, false), (v, n) => v with { Start = delays.Take(n).Sum(value => (long)value) / (double)durationMs });
            Number(RightFields, "GIF 结束帧 · 不含", 1, delays.Count, v => Frame(v.End * durationMs, true), (v, n) => v with { End = delays.Take(n).Sum(value => (long)value) / (double)durationMs });
        }
        UiTheme.Section(RightFields, "填写说明", "起点必须早于终点，裁剪框必须位于源图像内。不同单位都更新同一个播放片段，以最后编辑的值为准；重新打开可查看换算值。");
        Finish();
        int Frame(double milliseconds, bool end)
        {
            long cumulative = 0;
            for (int frame = 0; frame < delays.Count; frame++) { cumulative += delays[frame]; if (milliseconds < cumulative || end && milliseconds == cumulative) return end ? frame + 1 : frame; }
            return end ? delays.Count : delays.Count - 1;
        }
    }
}
