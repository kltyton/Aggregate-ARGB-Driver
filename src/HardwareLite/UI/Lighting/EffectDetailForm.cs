using HardwareLite.Configuration;
using HardwareLite.UI.Shell;
namespace HardwareLite.UI.Lighting;

internal sealed class EffectDetailForm : OptionsForm<LightingParameters>
{
    public EffectDetailForm(LightingParameters value) : base("灯效 · 色带与触发", value, v => v.Validate())
    {
        UiTheme.Section(LeftFields, "动画曲线");
        TextField(LeftFields, "自定义色带", v => string.Join(", ", v.Gradient), (v, text) => v with { Gradient = text.Split([',', '，', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) });
        Choice(LeftFields, "呼吸波形", ["余弦", "三角", "锯齿", "方波"], v => (int)v.Waveform, (v, n) => v with { Waveform = (EffectWaveform)n });
        Choice(LeftFields, "尾部曲线", ["线性", "平滑", "二次"], v => (int)v.TailCurve, (v, n) => v with { TailCurve = (TailCurve)n });
        Decimal(LeftFields, "星空密度", .01m, 1, 2, v => v.StarDensity, (v, n) => v with { StarDensity = n });
        Number(LeftFields, "星光锐度", 1, 64, v => v.StarSharpness, (v, n) => v with { StarSharpness = n });
        Number(LeftFields, "星空随机种子", 0, int.MaxValue, v => v.StarSeed, (v, n) => v with { StarSeed = n });
        UiTheme.Section(RightFields, "键鼠触发");
        Trigger("按键 / 鼠标按钮", InputTriggers.Buttons); Trigger("鼠标移动", InputTriggers.MouseMove); Trigger("鼠标滚轮", InputTriggers.MouseWheel);
        TextField(RightFields, "虚拟键码列表", v => string.Join(", ", v.TriggerKeys), (v, text) => v with { TriggerKeys = ParseKeys(text) });
        Toggle(RightFields, "触发时轮换颜色", v => v.TriggerColorCycle, (v, n) => v with { TriggerColorCycle = n });
        UiTheme.Section(RightFields, "填写说明", "色带填写 #RRGGBB，用逗号分隔。键码留空表示所有按键；例如 65, 68 表示 A、D。应用音频包含目标进程的子进程。");
        Finish();
        void Trigger(string label, InputTriggers flag) => Toggle(RightFields, label, v => v.Triggers.HasFlag(flag), (v, on) => v with { Triggers = on ? v.Triggers | flag : v.Triggers & ~flag });
    }
    private static int[] ParseKeys(string text)
    {
        var values = text.Split([',', '，', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Any(value => !int.TryParse(value, out _))) return [0];
        return values.Select(int.Parse).Distinct().ToArray();
    }
}
