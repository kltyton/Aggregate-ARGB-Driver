using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareLite.Configuration;

public enum TemperatureUnit { Celsius, Fahrenheit }
public enum CasePage { Cycle, Cpu, Gpu, Off, CpuFrequency, CpuFan }
public enum LightingMode { Static, Off, Breathe, Rainbow, ColorCycle, StarrySky, Comet, Wave, Smart, Audio, InputReactive }
public enum ScreenMode { Monitor, Media, MediaAndMonitor }
public enum MediaPlayback { Single, Cycle, Shuffle }

public sealed record Settings
{
    public int Version { get; init; } = 2;
    public bool OriginalSettingsImported { get; init; }
    public int SampleIntervalMs { get; init; } = 1000;
    public TemperatureUnit TemperatureUnit { get; init; }
    public string GpuId { get; init; } = "";
    public string CpuFanId { get; init; } = "";
    public string AioPumpId { get; init; } = "";
    public string SystemFanId { get; init; } = "";
    public int ResumeDelayMs { get; init; } = 15000;
    public string SystemTemperatureId { get; init; } = "";
    public CaseSettings Case { get; init; } = new();
    public LightingSettings Lighting { get; init; } = new();
    public ScreenSettings Screen { get; init; } = new();
    public MonitorOptions Monitoring { get; init; } = new();
    public RuntimeOptions Runtime { get; init; } = new();

    public void Validate()
    {
        if (Version is not (1 or 2)) throw new InvalidDataException("配置版本不受支持。");
        if (Case is null || Lighting is null || Screen is null || Lighting.Color is null || Screen.MediaPath is null)
            throw new InvalidDataException("配置缺少设备设置。");
        if (Monitoring is null || Runtime is null || Screen.Options is null || Lighting.Options is null || Lighting.Audio is null || Case.Options is null)
            throw new InvalidDataException("高级配置缺少必要参数。");
        if (SampleIntervalMs is < 100 or > 10000) throw new InvalidDataException("采样间隔应在 100–10000 毫秒之间。");
        if (ResumeDelayMs is < 0 or > 60000) throw new InvalidDataException("唤醒延迟应在 0–60 秒之间。");
        if (Case.SwitchSeconds is < 1 or > 60) throw new InvalidDataException("机箱轮播间隔应在 1–60 秒之间。");
        if (Lighting.Brightness is < 0 or > 100 || Screen.Brightness is < 0 or > 100) throw new InvalidDataException("亮度应在 0–100 之间。");
        if (Screen.MaxFps is < 1 or > 60) throw new InvalidDataException("屏幕发送帧率上限应在 1–60 FPS 之间。");
        if (Lighting.Color.Length != 7 || Lighting.Color[0] != '#' || !int.TryParse(Lighting.Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
            throw new InvalidDataException("灯光颜色应为 #RRGGBB。");
        if (!Enum.IsDefined(TemperatureUnit) || !Enum.IsDefined(Case.Page) || !Enum.IsDefined(Lighting.Mode) || !Enum.IsDefined(Screen.Mode))
            throw new InvalidDataException("配置包含未知模式。");
        if (Screen.Rotation is < 0 or > 3 || Screen.Layout is null) throw new InvalidDataException("屏幕旋转或布局无效。");
        if (Screen.FontFiles is null || Screen.MediaEdits is null || Screen.BuiltInBackgrounds is null) throw new InvalidDataException("屏幕字体或媒体编辑配置无效。");
        if (Screen.FontFiles.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("字体文件路径不能为空。");
        foreach (var edit in Screen.MediaEdits.Values) edit.Validate();
        Screen.Layout.Validate();
        if (Screen.MediaFiles is null || Screen.MediaFiles.Length > Screen.Options.MediaCapacity || Screen.MediaFiles.Any(string.IsNullOrWhiteSpace)
            || !Enum.IsDefined(Screen.Playback) || Screen.MediaIntervalSeconds is < 1 or > 3600)
            throw new InvalidDataException("媒体库、播放模式或轮播间隔无效。");
        if (Lighting.Group is < -1 or > 4 || Lighting.Profiles is null) throw new InvalidDataException("灯光分组或配置无效。");
        foreach (var profile in Lighting.Profiles) { if (!Enum.IsDefined(profile.Key) || profile.Value is null) throw new InvalidDataException("灯效配置无效。"); profile.Value.Validate(); }
        Monitoring.Validate(); Runtime.Validate(); Screen.Options.Validate(); Lighting.Options.Validate(); Lighting.Audio.Validate(); Case.Options.Validate();
    }
}

public sealed record CaseSettings
{
    public bool Enabled { get; init; } = true;
    public CasePage Page { get; init; } = CasePage.Cycle;
    public int SwitchSeconds { get; init; } = 3;
    public CaseOptions Options { get; init; } = new();
}

public sealed record LightingSettings
{
    public bool Enabled { get; init; } = true;
    public LightingMode Mode { get; init; }
    public string Color { get; init; } = "#39C5BB";
    public int Brightness { get; init; } = 70;
    public int Group { get; init; } = -1;
    public Dictionary<LightingMode, LightingParameters> Profiles { get; init; } = [];
    public LightingOptions Options { get; init; } = new();
    public AudioOptions Audio { get; init; } = new();
    [JsonIgnore] public LightingParameters Parameters => Profiles.TryGetValue(Mode, out var value) ? value : new() { Color = Color };
}

public sealed record ScreenSettings
{
    public bool Enabled { get; init; } = true;
    public ScreenMode Mode { get; init; } = ScreenMode.Monitor;
    public string MediaPath { get; init; } = "";
    public string DeviceMediaPath { get; init; } = "";
    public string DeviceUiPath { get; init; } = "";
    public int Brightness { get; init; } = 80;
    public int MaxFps { get; init; } = 60;
    public int Rotation { get; init; }
    public bool SleepWithDisplay { get; init; }
    public ScreenLayout Layout { get; init; } = new();
    public string[] MediaFiles { get; init; } = [];
    public MediaPlayback Playback { get; init; }
    public int MediaIntervalSeconds { get; init; } = 10;
    public string[] FontFiles { get; init; } = [];
    public Dictionary<string, string> BuiltInBackgrounds { get; init; } = [];
    public Dictionary<string, MediaEdit> MediaEdits { get; init; } = [];
    public ScreenOptions Options { get; init; } = new();
}

public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath { get; } = Path.Combine(directory, "settings.json");
    public bool? ImportedAutostart { get; private set; }
    public bool HasPendingMigration { get; private set; }

    public Settings Load()
    {
        var settings = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? throw new InvalidDataException("配置为空。")
            : new Settings();
        settings.Validate();
        if (settings.Version == 1) { settings = settings with { Version = 2 }; HasPendingMigration = true; }
        if (!settings.OriginalSettingsImported)
        {
            var imported = OriginalSettingsImport.Apply(settings, directory);
            settings = imported.Settings;
            ImportedAutostart = imported.Autostart;
            HasPendingMigration = settings.OriginalSettingsImported;
        }
        if (settings.Screen.BuiltInBackgrounds.Count == 0)
        {
            var backgrounds = OriginalSettingsImport.ImportBuiltInBackgrounds(directory);
            if (backgrounds.Count > 0)
            {
                settings = settings with { Screen = settings.Screen with { BuiltInBackgrounds = backgrounds } };
                HasPendingMigration = true;
            }
        }
        settings.Validate();
        return settings;
    }

    public void Save(Settings settings)
    {
        settings.Validate();
        settings = settings with { Version = 2 };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, FilePath, true);
    }
}
