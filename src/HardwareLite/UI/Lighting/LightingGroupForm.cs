using HardwareLite.Configuration;
using HardwareLite.UI.Shell;
namespace HardwareLite.UI.Lighting;
internal sealed class LightingGroupForm : OptionsForm<LightingGroupProfile>
{
    public LightingGroupForm(int group, LightingGroupProfile value) : base($"灯光分组 {group + 1} · 独立配置", value, v => v.Validate())
    {
        UiTheme.Section(LeftFields, "分组灯效");
        Choice(LeftFields, "灯效", ["常亮", "关闭", "呼吸", "彩虹", "循环", "星空", "彗星", "波浪", "智能监控", "音乐", "键鼠联动"], v => (int)v.Mode, (v, n) => v with { Mode = (LightingMode)n });
        Number(LeftFields, "亮度 %", 0, 100, v => v.Brightness, (v, n) => v with { Brightness = n });
        UiTheme.Field(LeftFields, "当前灯效参数", UiTheme.Button("编辑颜色与联动", () =>
        {
            using var dialog = new EffectSettingsForm(Value.Profiles.GetValueOrDefault(Value.Mode) ?? new());
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            Update(v => { var profiles = new Dictionary<LightingMode, LightingParameters>(v.Profiles) { [v.Mode] = dialog.Result }; return v with { Profiles = profiles }; });
        }));
        UiTheme.Section(RightFields, "独立设置", "每个分组分别保留各灯效的参数；音频采集设备与后台更新间隔使用共享设置。保存主窗口后应用到设备。");
        Finish();
    }
}
