using HardwareLite.Configuration;
using HardwareLite.UI.Shell;
using NAudio.CoreAudioApi;

namespace HardwareLite.UI.Lighting;

internal sealed class LightingOptionsForm : OptionsForm<LightingSettings>
{
    public LightingOptionsForm(LightingSettings value) : base("主板灯光 · 更新与音频", value, settings => { settings.Options.Validate(); settings.Audio.Validate(); })
    {
        UiTheme.Section(LeftFields, "更新与数据");
        Number(LeftFields, "动态更新 · ms", 10, 10000, v => v.Options.DynamicIntervalMs, (v, n) => v with { Options = v.Options with { DynamicIntervalMs = n } });
        Number(LeftFields, "静态 / 监控 · ms", 10, 10000, v => v.Options.MonitorIntervalMs, (v, n) => v with { Options = v.Options with { MonitorIntervalMs = n } });
        Choice(LeftFields, "数据失效策略", ["最低阈值颜色", "保持最后颜色", "关闭灯光", "重连并报错"], v => (int)v.Options.ExpiredData, (v, n) => v with { Options = v.Options with { ExpiredData = (MissingDataPolicy)n } });
        Choice(LeftFields, "停用 / 退出时", ["保持设备状态", "关闭灯光"], v => (int)v.Options.OnStop, (v, n) => v with { Options = v.Options with { OnStop = (DeviceStopBehavior)n } });
        UiTheme.Section(LeftFields, "音频来源");
        AntdUI.Select? deviceSelector = null;
        Choice(LeftFields, "采集来源", ["系统输出", "麦克风 / 输入", "指定应用"], v => (int)v.Audio.Source, (v, n) =>
        {
            if (deviceSelector is not null) deviceSelector.SelectedIndex = 0;
            return v with { Audio = v.Audio with { Source = (AudioCaptureSource)n, DeviceId = "" } };
        });
        var ids = new List<string> { "" }; var names = new List<string> { "使用系统默认设备" };
        using (var enumerator = new MMDeviceEnumerator())
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
                using (device) { ids.Add(device.ID); names.Add((device.DataFlow == DataFlow.Capture ? "输入 · " : "输出 · ") + device.FriendlyName); }
        }
        if (value.Audio.DeviceId.Length > 0 && !ids.Contains(value.Audio.DeviceId)) { ids.Add(value.Audio.DeviceId); names.Add("未连接 · " + value.Audio.DeviceId); }
        Choice(LeftFields, "音频设备", names.ToArray(), v => ids.IndexOf(v.Audio.DeviceId), (v, n) => v with { Audio = v.Audio with { DeviceId = ids[n] } });
        deviceSelector = LeftFields.Controls.OfType<AntdUI.Select>().Last();
        TextField(LeftFields, "应用进程名称", v => v.Audio.ApplicationName, (v, n) => v with { Audio = v.Audio with { ApplicationName = n.Trim() } });
        Toggle(LeftFields, "排除该应用及子进程", v => v.Audio.ExcludeApplication, (v, n) => v with { Audio = v.Audio with { ExcludeApplication = n } });
        UiTheme.Section(RightFields, "频谱分析");
        Number(RightFields, "采样率 · Hz", 8000, 96000, v => v.Audio.SampleRate, (v, n) => v with { Audio = v.Audio with { SampleRate = n } });
        int[] fft = [128, 256, 512, 1024, 2048, 4096, 8192];
        Choice(RightFields, "FFT 长度", fft.Select(n => n.ToString()).ToArray(), v => Array.IndexOf(fft, v.Audio.FftSize), (v, n) => v with { Audio = v.Audio with { FftSize = fft[n] } });
        Number(RightFields, "频段数量", 3, 128, v => v.Audio.Bands, (v, n) => v with { Audio = v.Audio with { Bands = n } });
        Choice(RightFields, "窗函数", ["Hann", "Hamming", "矩形"], v => (int)v.Audio.Window, (v, n) => v with { Audio = v.Audio with { Window = (SpectrumWindow)n } });
        Choice(RightFields, "频段聚合", ["峰值", "平均"], v => (int)v.Audio.Aggregation, (v, n) => v with { Audio = v.Audio with { Aggregation = (SpectrumAggregation)n } });
        Number(RightFields, "最低频率 · Hz", 1, 48000, v => v.Audio.MinimumHz, (v, n) => v with { Audio = v.Audio with { MinimumHz = n } });
        Number(RightFields, "最高频率 · Hz", 2, 48000, v => v.Audio.MaximumHz, (v, n) => v with { Audio = v.Audio with { MaximumHz = n } });
        Toggle(RightFields, "使用三段频率分界", v => v.Audio.UseFrequencyBoundaries, (v, n) => v with { Audio = v.Audio with { UseFrequencyBoundaries = n } });
        Number(RightFields, "低 / 中分界 · Hz", 1, 48000, v => v.Audio.LowMidHz, (v, n) => v with { Audio = v.Audio with { LowMidHz = n } });
        Number(RightFields, "中 / 高分界 · Hz", 2, 48000, v => v.Audio.MidHighHz, (v, n) => v with { Audio = v.Audio with { MidHighHz = n } });
        Decimal(RightFields, "分析增益", .01m, 100, 2, v => v.Audio.AnalysisGain, (v, n) => v with { Audio = v.Audio with { AnalysisGain = n } });
        Number(RightFields, "静音超时 · ms", 10, 10000, v => v.Audio.SilenceMs, (v, n) => v with { Audio = v.Audio with { SilenceMs = n } });
        Number(RightFields, "上升平滑 · ms", 0, 10000, v => v.Audio.AttackMs, (v, n) => v with { Audio = v.Audio with { AttackMs = n } });
        Number(RightFields, "衰减平滑 · ms", 0, 10000, v => v.Audio.DecayMs, (v, n) => v with { Audio = v.Audio with { DecayMs = n } });
        Finish();
    }
}
