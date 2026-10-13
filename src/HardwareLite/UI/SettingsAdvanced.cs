using HardwareLite.Configuration;
using HardwareLite.UI.Screen;
using HardwareLite.UI.Case;
using HardwareLite.UI.Monitoring;
using HardwareLite.UI.Lighting;
namespace HardwareLite.UI;
internal sealed partial class SettingsForm
{
    private Settings _advanced = new();
    private void LoadAdvanced(Settings settings)
    {
        _advanced = settings;
        _interval.DecimalPlaces = _resumeDelay.DecimalPlaces = 3;
        _interval.Minimum = 0.1m;
        _previewTimer.Interval = (int)Math.Ceiling(1000d / settings.Screen.Options.PreviewFps);
    }
    private void EditScreenOptions()
    {
        PausePreview();
        try
        {
            using var dialog = new ScreenOptionsForm(_advanced.Screen.Options, _mediaFiles.Length);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _advanced = _advanced with { Screen = _advanced.Screen with { Options = dialog.Result } };
            _previewTimer.Interval = (int)Math.Ceiling(1000d / dialog.Result.PreviewFps); DraftChanged();
        }
        finally { ResumePreview(); }
    }
    private void EditCaseOptions()
    {
        using var dialog = new CaseOptionsForm(_advanced.Case.Options, (int)_switch.Value, _snapshot());
        if (dialog.ShowDialog(this) == DialogResult.OK) _advanced = _advanced with { Case = _advanced.Case with { Options = dialog.Result } };
    }
    private void EditScreenResources()
    {
        PausePreview();
        try
        {
            using var dialog = new ScreenResourcesForm(_advanced.Screen);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _advanced = _advanced with { Screen = _advanced.Screen with { FontFiles = dialog.Result.FontFiles, BuiltInBackgrounds = dialog.Result.BuiltInBackgrounds } };
            bool loading = _loading; _loading = true;
            try
            {
                _factoryBackground.Items.Clear();
                foreach (var pair in dialog.Result.BuiltInBackgrounds) _factoryBackground.Items.Add(new AntdUI.SelectItem(pair.Key, pair.Value));
                _factoryBackground.Enabled = dialog.Result.BuiltInBackgrounds.Count > 0;
            }
            finally { _loading = loading; }
            SyncBackgroundSelection(); DraftChanged();
        }
        finally { ResumePreview(); }
    }
    private void EditMonitoringOptions()
    {
        using var dialog = new MonitorOptionsForm(_advanced with { Screen = DraftSettings().Screen }, _snapshot());
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _advanced = _advanced with { Monitoring = dialog.Result.Monitoring, Runtime = dialog.Result.Runtime }; DraftChanged();
    }
    private void EditLightingOptions()
    {
        using var dialog = new LightingOptionsForm(_advanced.Lighting);
        if (dialog.ShowDialog(this) == DialogResult.OK) _advanced = _advanced with { Lighting = _advanced.Lighting with { Options = dialog.Result.Options, Audio = dialog.Result.Audio } };
    }
    private void StoreLightingGroup()
    {
        int group = _lightGroup.SelectedIndex - 1;
        if (group < 0) { Report("请先选择具体灯光分组。", true); return; }
        SaveLightProfile();
        var groups = new Dictionary<int, LightingGroupProfile>(_advanced.Lighting.Options.Groups)
        { [group] = new() { Mode = _currentLightMode, Brightness = (int)_lightBrightness.Value, Profiles = new(_lightProfiles) } };
        _advanced = _advanced with { Lighting = _advanced.Lighting with { Options = _advanced.Lighting.Options with { Groups = groups } } };
        Report("该分组的独立设置已记录，保存并应用后生效。");
    }
    private void FollowLightingGroup()
    {
        int group = _lightGroup.SelectedIndex - 1;
        var groups = new Dictionary<int, LightingGroupProfile>(_advanced.Lighting.Options.Groups); groups.Remove(group);
        _advanced = _advanced with { Lighting = _advanced.Lighting with { Options = _advanced.Lighting.Options with { Groups = groups } } };
        Report("该分组已恢复共享设置，保存并应用后生效。");
    }
    private void EditLightingGroup()
    {
        int group = _lightGroup.SelectedIndex - 1;
        if (group < 0) { Report("请先选择具体灯光分组。", true); return; }
        var profile = _advanced.Lighting.Options.Groups.GetValueOrDefault(group) ?? new LightingGroupProfile { Mode = _currentLightMode, Brightness = (int)_lightBrightness.Value, Profiles = new(_lightProfiles) };
        using var dialog = new LightingGroupForm(group, profile);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var groups = new Dictionary<int, LightingGroupProfile>(_advanced.Lighting.Options.Groups) { [group] = dialog.Result };
        _advanced = _advanced with { Lighting = _advanced.Lighting with { Options = _advanced.Lighting.Options with { Groups = groups } } };
    }
}
