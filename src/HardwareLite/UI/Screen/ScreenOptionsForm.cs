using HardwareLite.Configuration;
using HardwareLite.UI.Shell;
using System.Drawing.Drawing2D;
using HardwareLite.Display.Media;
namespace HardwareLite.UI.Screen;
internal sealed class ScreenOptionsForm : OptionsForm<ScreenOptions>
{
    public ScreenOptionsForm(ScreenOptions value, int currentMediaCount = 0) : base("水冷屏幕 · 画质与播放", value, options =>
    {
        options.Validate();
        if (options.MediaCapacity < currentMediaCount) throw new InvalidDataException($"媒体库已有 {currentMediaCount} 项，请先移除媒体再下调容量。");
    })
    {
        UiTheme.Section(LeftFields, "画质与播放");
        Toggle(LeftFields, "设备内播放", x => x.DevicePlayback, (x,b) => x with { DevicePlayback=b });
        Choice(LeftFields, "设备媒体编码", ["裁剪视频 · H.264", "无损画面 · DEFLATE"], x => (int)x.DeviceEncoding, (x,n) => x with { DeviceEncoding=(DeviceMediaEncoding)n });
        Number(LeftFields, "设备视频码率 · kbps", 128, 20000, x => x.DeviceVideoBitrate / 1000, (x,n) => x with { DeviceVideoBitrate=n*1000 });
        UiTheme.Section(LeftFields, "设备视频", "只准备裁剪与布局后的画面，最高 60 FPS，不提高源帧率。H.264 为有损编码；DEFLATE 压缩处理后的视频字节时不再损失数据。");
        Number(LeftFields, "JPEG 质量", 0, 100, x => x.JpegQuality, (x,n) => x with { JpegQuality=n });
        Decimal(LeftFields, "播放倍率", 0.125m, 8, 3, x => x.PlaybackSpeed, (x,n) => x with { PlaybackSpeed=n });
        Toggle(LeftFields, "保留 GIF 原始延时", x => x.PreserveGifDelays, (x,b) => x with { PreserveGifDelays=b });
        Number(LeftFields, "GIF 最短延时 · ms", 1, 1000, x => x.GifMinimumDelayMs, (x,n) => x with { GifMinimumDelayMs=n });
        Number(LeftFields, "GIF 缺失延时 · ms", 1, 10000, x => x.GifMissingDelayMs, (x,n) => x with { GifMissingDelayMs=n });
        var modes = new[] { InterpolationMode.Default, InterpolationMode.NearestNeighbor, InterpolationMode.Bilinear, InterpolationMode.Bicubic, InterpolationMode.HighQualityBilinear, InterpolationMode.HighQualityBicubic };
        Choice(LeftFields, "最终缩放算法", ["默认", "最近邻", "双线性", "双三次", "高质量双线性", "高质量双三次"], x => Array.IndexOf(modes,x.Interpolation), (x,n) => x with { Interpolation=modes[n] });
        Toggle(LeftFields, "随机轮播一轮不重复", x => x.ShuffleAvoidRepeats, (x,b) => x with { ShuffleAvoidRepeats=b });
        Number(LeftFields, "媒体库容量", 1, 1024, x => x.MediaCapacity, (x,n) => x with { MediaCapacity=n });
        Number(LeftFields, "静态保活间隔 · ms", 100, 60000, x => x.KeepaliveMs, (x,n) => x with { KeepaliveMs=n });
        Choice(LeftFields, "水冷温度单位", ["跟随共享设置", "摄氏 °C", "华氏 °F"], x => x.TemperatureUnit.HasValue ? (int)x.TemperatureUnit.Value+1 : 0,
            (x,n) => x with { TemperatureUnit=n==0 ? null : (TemperatureUnit)(n-1) });
        UiTheme.Section(RightFields, "预览", "预览仅在 GUI 打开时运行。");
        Toggle(RightFields, "播放预览动画", x => x.PreviewEnabled, (x,b) => x with { PreviewEnabled=b });
        Number(RightFields, "预览帧率 · FPS", 1, 60, x => x.PreviewFps, (x,n) => x with { PreviewFps=n });
        UiTheme.Section(RightFields, "视频处理", "默认保留原尺寸软件处理。加速和缩放可能改变色彩或插值；设置为 0×0 保留源尺寸。");
        Choice(RightFields, "视频加速", ["软件", "启用硬件加速", "自动选择"], x => (int)x.VideoDecoder, (x,n) => x with { VideoDecoder=(VideoDecoderMode)n });
        var adapters = VideoAdapters.List();
        var ids = new List<string> { "" }; var names = new List<string> { "使用系统默认适配器" };
        foreach (var adapter in adapters) { ids.Add(adapter.Id); names.Add(adapter.Name); }
        if (!ids.Contains(value.VideoAdapterId)) { ids.Add(value.VideoAdapterId); names.Add("未连接 · " + value.VideoAdapterId); }
        Choice(RightFields, "视频处理适配器", names.ToArray(), x => ids.IndexOf(x.VideoAdapterId), (x,n) => x with { VideoAdapterId=ids[n] });
        Number(RightFields, "处理尺寸上限 · 宽", 0, 7680, x => x.DecodeWidth, (x,n) => x with { DecodeWidth=n });
        Number(RightFields, "处理尺寸上限 · 高", 0, 7680, x => x.DecodeHeight, (x,n) => x with { DecodeHeight=n });
        UiTheme.Section(RightFields, "数据与停用");
        Choice(RightFields, "监控过期时", ["显示缺失标记", "保留旧数据", "黑屏", "重新连接"], x => (int)x.ExpiredData, (x,n) => x with { ExpiredData=(MissingDataPolicy)n });
        Choice(RightFields, "停用时", ["保持设备状态", "关闭画面"], x => (int)x.OnStop, (x,n) => x with { OnStop=(DeviceStopBehavior)n });
        Finish();
    }
}
