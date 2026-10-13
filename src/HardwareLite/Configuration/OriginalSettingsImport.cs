using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using HardwareLite.Display.Media;

namespace HardwareLite.Configuration;

internal static class OriginalSettingsImport
{
    public static (Settings Settings, bool? Autostart) Apply(Settings existing, string directory)
    {
        var settings = existing;
        bool? autostart = null;
        bool found = false;
        string maxsun = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Maxsun", "MaxsunSync2", "config.json");
        if (File.Exists(maxsun))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(maxsun));
            var global = document.RootElement.GetProperty("global");
            var profiles = new Dictionary<LightingMode, LightingParameters>();
            foreach (var effect in document.RootElement.GetProperty("effects").EnumerateObject())
            {
                if (!Mode(effect.Name, out var mode)) continue;
                var value = effect.Value;
                profiles[mode] = new()
                {
                    Color = Rgb(value, "Foreground", "#FFFFFF"), Background = Rgb(value, "Background", "#000000"),
                    Speed = (int)Math.Clamp(Number(value, "Speed", 50), 1, 100),
                    Brightness = (int)Number(value, "Brightness", Number(global, "brightness", 100)),
                    Reverse = Number(value, "LedDirection", 1) != 1, ColorReverse = Number(value, "ColorDirection", 1) != 1,
                    TailLength = (int)Math.Round(Number(value, "LangthRatio", 0.3) * 100),
                    Palette = (int)Number(value, "ColorStrip", 0), SegmentSize = (int)Number(value, "SegmentSize", 8),
                    RandomColor = Boolean(value, "Random", false), BackgroundOff = Boolean(value, "BgDeactive", false),
                    Minimum = (int)Number(value, "ThresholdL", 30), Maximum = (int)Number(value, "ThresholdH", 80),
                    SmartSource = (SmartSource)((int)Number(value, "Usage", 256) - 256),
                    AudioMode = (AudioMode)((int)Number(value, "AudioMode", 768) - 768),
                    AudioBoost = (int)Math.Round(Number(value, "Boost", 1) * 100),
                    LowColor = Rgb(value, "LowBandColor", "#FF0000"), MidColor = Rgb(value, "MidBandColor", "#00FF00"), HighColor = Rgb(value, "HighBandColor", "#0000FF"),
                    InputSource = (InputSource)((int)Number(value, "Input", 514) - 512)
                };
            }
            if (!global.TryGetProperty("effect", out var effectName) || effectName.ValueKind != JsonValueKind.String || !Mode(effectName.GetString()!, out var selected))
                throw new InvalidDataException("铭瑄配置缺少有效的 global.effect 灯效名称。");
            settings = settings with { ResumeDelayMs = (int)Number(global, "resumeDelay", 15000), Lighting = new() { Mode = selected, Color = profiles.GetValueOrDefault(selected)?.Color ?? "#39C5BB", Brightness = (int)Number(global, "brightness", 100), Profiles = profiles } };
            autostart = Boolean(global, "autoStart", false); found = true;
        }
        string? ch270 = Ch270Directory();
        if (ch270 is not null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ch270, "config.json")));
            var value = document.RootElement;
            if (!value.TryGetProperty("mode", out var selectedPage)) throw new InvalidDataException("CH270 主程序配置缺少显示页面。");
            var page = selectedPage.GetString()!.ToUpperInvariant() switch
            { "CYCLE" => CasePage.Cycle, "CPU" => CasePage.Cpu, "GPU" => CasePage.Gpu, "OFF" => CasePage.Off, var unknown => throw new InvalidDataException("原厂机箱页面无法识别：" + unknown) };
            settings = settings with
            {
                TemperatureUnit = value.TryGetProperty("temp_unit", out var temperatureUnit) && temperatureUnit.ValueKind == JsonValueKind.String
                    ? temperatureUnit.GetString() == "F" ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius
                    : throw new InvalidDataException("CH270 配置缺少有效的 temp_unit 温度单位。"),
                Case = new() { Enabled = Boolean(value, "enable_display", true), Page = page, SwitchSeconds = (int)Number(value, "interval_seconds", 3) }
            };
            autostart = (autostart ?? false) || Boolean(value, "auto_start", false); found = true;
        }
        string? asus = AsusDirectory();
        if (asus is not null && File.Exists(Path.Combine(asus, "bgConfig.ini")))
        {
            var ini = File.ReadLines(Path.Combine(asus, "bgConfig.ini")).Where(line => line.Contains('='))
                .Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.Ordinal);
            string Get(string key, string fallback = "") => ini.GetValueOrDefault(key, fallback);
            double Num(string key, double fallback = 0) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
            string Color(string key, string fallback) { string value = Get(key); return value.Length == 8 ? "#" + value[2..].ToUpperInvariant() : fallback; }
            var files = new List<string>();
            var edits = new Dictionary<string, MediaEdit>();
            string selectedPath = "";
            int selected = (int)Num("ApplyBgID");
            for (int i = 0; i < Num("bgMaxCount", 30); i++)
            {
                string filename = Get("bgPath" + i);
                if (filename.Length == 0) continue;
                if (Path.GetFileName(filename) != filename) throw new InvalidDataException("原厂媒体路径必须指向背景库文件。");
                string source = Path.Combine(asus, "Background", filename);
                string target = Path.Combine(directory, "media", "Asus", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) File.Copy(source, target);
                files.Add(target);
                using var image = MediaFile.OpenPreview(target);
                double ratio = Num("bgScaleRt" + i);
                double cw = Num("bgScaleCW" + i), ch = Num("bgScaleCH" + i);
                if (ratio <= 0 || cw <= 0 || ch <= 0) throw new InvalidDataException("原厂媒体裁剪参数无效。");
                double x = -Num("bgScaleX" + i) / ratio / image.Width, y = -Num("bgScaleY" + i) / ratio / image.Height;
                edits[target] = new()
                {
                    X = Math.Clamp(x, 0, 1), Y = Math.Clamp(y, 0, 1),
                    Width = Math.Min(cw / ratio / image.Width, 1 - Math.Clamp(x, 0, 1)), Height = Math.Min(ch / ratio / image.Height, 1 - Math.Clamp(y, 0, 1)),
                    Rotation = (int)Num("bgScaleRo" + i), Start = Num("bgScaleSp" + i), End = Num("bgScaleEp" + i, 1)
                };
                if (i == selected) selectedPath = target;
            }
            var layout = new ScreenLayout
            {
                BackgroundColor = "#000000", BackgroundShade = 0, ImageFit = ImageFit.Stretch,
                Elements =
                [
                    Element(Get("ApplyTopHwType"), 55, 120, 610, 250, 160, 64, 80),
                    Element(Get("ApplyMidHwType"), 55, 480, 295, 190, 85, 48, 42),
                    Element(Get("ApplyBottomHwType"), 380, 480, 310, 190, 85, 48, 42)
                ]
            };
            ScreenElement Element(string metric, int x, int y, int width, int height, int fontSize, int titleSize, int unitSize)
            {
                var kind = metric switch
                {
                    "Usage//CPU" => ScreenMetric.CpuUsage, "Usage//GPU" => ScreenMetric.GpuUsage,
                    "Temperature//CPU" => ScreenMetric.CpuTemperature, "Temperature//GPU" => ScreenMetric.GpuTemperature,
                    "Temperature//System" => ScreenMetric.SystemTemperature,
                    var unknown => throw new InvalidDataException("原厂监控项目尚未映射：" + unknown)
                };
                return new() { Metric = kind, Title = metric.Split("//")[1], X = x, Y = y, Width = width, Height = height, FontSize = fontSize,
                    FontFamily = "Kairos Sans W1G Cn Bold", Bold = false, TitleFontFamily = "DIN Pro Cond", TitleFontSize = titleSize,
                    UnitFontFamily = "Kairos Sans W1G Cn Medium", UnitFontSize = unitSize,
                    Color = Color("ApplyContentColor", "#39C5BB"), TitleColor = Color("ApplyTitleColor", "#99D300") };
            }
            settings = settings with
            {
                TemperatureUnit = Num("temperUnit") == 0 ? TemperatureUnit.Celsius : TemperatureUnit.Fahrenheit,
                Screen = settings.Screen with
                {
                    Enabled = Get("screenSwitch", "true") == "true", Mode = selectedPath.Length > 0 ? ScreenMode.MediaAndMonitor : ScreenMode.Monitor,
                    MediaPath = selectedPath, MediaFiles = files.ToArray(), MediaEdits = edits, Brightness = (int)Num("ApplyBacklight", 100),
                    SleepWithDisplay = Num("SleepMode") != 0, Rotation = (int)Num("screenRotate"),
                    Playback = (MediaPlayback)(int)Num("ApplyBgCycleMode"), Layout = layout,
                    FontFiles = LocalFontImport.Import(Path.Combine(asus, "ASUS_InfoHub.exe"), Path.Combine(directory, "fonts", "Asus"))
                }
            };
            found = true;
        }
        return (settings with { OriginalSettingsImported = found }, autostart);
    }

    private static bool Mode(string value, out LightingMode mode) => Enum.TryParse(value == "Breath" ? "Breathe" : value, out mode);
    public static Dictionary<string, string> ImportBuiltInBackgrounds(string directory)
    {
        var backgrounds = new Dictionary<string, string>();
        string? asus = AsusDirectory();
        if (asus is null) return backgrounds;
        foreach (var (name, filename) in new[] { ("硬件监控 · 背景一", "defaultbgHW01H.mp4"), ("硬件监控 · 背景二", "defaultbgHW02H.mp4"), ("时钟背景", "defaultbgClockH.mp4") })
        {
            string source = Path.Combine(asus, "Default", filename);
            if (!File.Exists(source)) continue;
            string target = Path.Combine(directory, "media", "Asus", "default", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(source, target);
            backgrounds.Add(name, target);
        }
        return backgrounds;
    }
    private static double Number(JsonElement element, string key, double fallback) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;
    private static bool Boolean(JsonElement element, string key, bool fallback) => element.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static string Rgb(JsonElement element, string key, string fallback)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return fallback;
        byte[] parts = value.GetString()!.Split(',').Select(part => byte.Parse(part.Trim(), CultureInfo.InvariantCulture)).ToArray();
        if (parts.Length != 3) throw new InvalidDataException("原厂灯光颜色无效。");
        return $"#{parts[0]:X2}{parts[1]:X2}{parts[2]:X2}";
    }
    private static string? AsusDirectory()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ASUS_InfoHub");
            if (key?.GetValue("InstallLocation") is string location && Directory.Exists(location)) return Path.GetFullPath(location);
        }
        return null;
    }
    private static string? Ch270Directory()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\CH270_SensorService");
        if (key?.GetValue("ImagePath") is not string command) return null;
        int end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        string executable = Environment.ExpandEnvironmentVariables(command[..(end + 4)].TrimStart('"'));
        string? directory = Directory.GetParent(executable)?.Parent?.FullName;
        return directory is not null && File.Exists(Path.Combine(directory, "config.json")) ? directory : null;
    }
}
