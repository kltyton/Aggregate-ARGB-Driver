using System.Runtime.InteropServices;
using HardwareLite.Configuration;
using HardwareLite.Monitoring;

namespace HardwareLite.Devices.Maxsun;

internal sealed class LightingEffects : IDisposable
{
    private readonly bool[] _keys = new bool[256];
    private long _lastInput = -10000;
    private int _inputCount;
    private Point _cursor;
    private long _wheel;
    private Color[] _colors = [], _audioColors = [];

    public Color[] Render(int count, LightingSettings settings, SensorSnapshot snapshot, long time, float[]? audio = null, long wheel = 0)
    {
        var parameters = settings.Parameters;
        if (settings.Mode == LightingMode.InputReactive) UpdateInput(parameters, time, wheel);
        Color[]? audioColors = audio is not null ? AudioColors(parameters, settings.Audio, audio) : null;
        var foreground = ColorTranslator.FromHtml(parameters.Color);
        var background = ColorTranslator.FromHtml(parameters.Background);
        double duration = (101 - parameters.Speed) * 100;
        double phase = time / duration % 1;
        if (parameters.ColorReverse) phase = 1 - phase;
        float? monitored = parameters.SmartSource switch
        {
            SmartSource.CpuTemperature => snapshot.CpuTemperature, SmartSource.CpuUsage => snapshot.CpuUsage,
            SmartSource.GpuTemperature => snapshot.GpuTemperature, _ => snapshot.GpuUsage
        };
        if (settings.Mode == LightingMode.Smart && (!snapshot.IsFresh || !monitored.HasValue) && settings.Options.ExpiredData == MissingDataPolicy.Retry)
            throw new IOException("智能灯效所选传感器未读取。");
        if (settings.Mode == LightingMode.Smart && (!snapshot.IsFresh || !monitored.HasValue))
        {
            if (settings.Options.ExpiredData == MissingDataPolicy.Off) settings = settings with { Mode = LightingMode.Off };
            monitored = parameters.Minimum;
        }
        if (_colors.Length != count) _colors = new Color[count];
        var colors = _colors;
        for (int i = 0; i < count; i++)
        {
            int led = parameters.Reverse ? count - 1 - i : i;
            double position = led / (double)count;
            double tail = parameters.TailLength / 100.0;
            double distance = (phase - position + 1) % 1;
            var color = settings.Mode switch
            {
                LightingMode.Off => Color.Black,
                LightingMode.Static => foreground,
                LightingMode.Breathe => Mix(background, foreground, Wave(parameters.Waveform, phase)),
                LightingMode.ColorCycle => Palette(parameters, phase),
                LightingMode.Rainbow => Palette(parameters, (phase + led / (double)parameters.SegmentSize) % 1),
                LightingMode.StarrySky => Mix(parameters.BackgroundOff ? Color.Black : background,
                    parameters.RandomColor ? Hue((led * 0.618 + Math.Floor(time / duration)) % 1) : foreground,
                     Star(parameters, led, phase)),
                LightingMode.Comet => Mix(background, foreground, Tail(parameters.TailCurve, distance < tail ? 1 - distance / tail : 0)),
                LightingMode.Wave => Mix(background, foreground, Math.Max(0, 1 - Math.Abs(Math.Sin((position - phase) * Math.PI)) / tail)),
                LightingMode.Smart => Palette(parameters, Math.Clamp((monitored!.Value - parameters.Minimum) / (parameters.Maximum - (double)parameters.Minimum), 0, 1)),
                LightingMode.Audio => audioColors![parameters.AudioMode == AudioMode.TriBand ? led % 3 : led % parameters.SegmentSize * audioColors.Length / parameters.SegmentSize],
                LightingMode.InputReactive => Mix(background, parameters.TriggerColorCycle ? Palette(parameters, _inputCount * 0.618 % 1) : foreground, Tail(parameters.TailCurve, Math.Clamp(1 - (time - _lastInput) / duration, 0, 1))),
                _ => throw new ArgumentOutOfRangeException(nameof(settings.Mode))
            };
            colors[i] = Mix(Color.Black, color, settings.Brightness / 100.0);
        }
        return colors;
    }

    private Color[] AudioColors(LightingParameters settings, AudioOptions analysis, float[] levels)
    {
        int size = settings.AudioMode is AudioMode.PeakWave or AudioMode.Spectrum ? levels.Length : 3;
        if (_audioColors.Length != size) _audioColors = new Color[size];
        if (settings.AudioMode == AudioMode.PeakWave)
        {
            float peak = Math.Clamp(levels.Max() * settings.AudioBoost / 100, 0, 1);
            var colors = _audioColors; var foreground = ColorTranslator.FromHtml(settings.Color);
            double middle = (colors.Length - 1) / 2d;
            for (int band = 0; band < colors.Length; band++) colors[band] = Mix(Color.Black, foreground, Math.Clamp(peak * 2 - Math.Abs(band - middle) / middle, 0, 1));
            return colors;
        }
        if (settings.AudioMode == AudioMode.Spectrum)
        {
            var colors = _audioColors;
            for (int band = 0; band < colors.Length; band++) colors[band] = Mix(Color.Black, Palette(settings, band / (double)colors.Length), Math.Clamp(levels[band] * settings.AudioBoost / 100, 0, 1));
            return colors;
        }
        float low = 0, mid = 0, high = 0; int lowCount = 0, midCount = 0, highCount = 0;
        for (int band = 0; band < levels.Length; band++)
        {
            double frequency = analysis.MinimumHz * Math.Pow(analysis.MaximumHz / (double)analysis.MinimumHz, (band + 0.5) / levels.Length);
            bool first = analysis.UseFrequencyBoundaries ? frequency < analysis.LowMidHz : band < levels.Length * 10 / 32;
            bool second = analysis.UseFrequencyBoundaries ? frequency < analysis.MidHighHz : band < levels.Length * 21 / 32;
            float Merge(float current) => analysis.Aggregation == SpectrumAggregation.Peak ? Math.Max(current, levels[band]) : current + levels[band];
            if (first) { low = Merge(low); lowCount++; } else if (second) { mid = Merge(mid); midCount++; } else { high = Merge(high); highCount++; }
        }
        if (analysis.Aggregation == SpectrumAggregation.Average) { low /= Math.Max(1, lowCount); mid /= Math.Max(1, midCount); high /= Math.Max(1, highCount); }
        _audioColors[0] = Mix(Color.Black, ColorTranslator.FromHtml(settings.LowColor), Math.Clamp(low * settings.AudioBoost / 100, 0, 1));
        _audioColors[1] = Mix(Color.Black, ColorTranslator.FromHtml(settings.MidColor), Math.Clamp(mid * settings.AudioBoost / 100, 0, 1));
        _audioColors[2] = Mix(Color.Black, ColorTranslator.FromHtml(settings.HighColor), Math.Clamp(high * settings.AudioBoost / 100, 0, 1));
        return _audioColors;
    }
    private void UpdateInput(LightingParameters settings, long time, long wheel)
    {
        bool triggered = false;
        for (int key = 1; key < 256; key++)
        {
            bool mouse = key <= 6;
            if (mouse && settings.InputSource == InputSource.Keyboard || !mouse && settings.InputSource == InputSource.Mouse) continue;
            if (!settings.Triggers.HasFlag(InputTriggers.Buttons) || settings.TriggerKeys.Length > 0 && !settings.TriggerKeys.Contains(key)) continue;
            bool down = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (down && !_keys[key]) triggered = true;
            _keys[key] = down;
        }
        if (settings.InputSource != InputSource.Keyboard)
        {
            if (settings.Triggers.HasFlag(InputTriggers.MouseMove) && GetCursorPos(out var point)) { if (point != _cursor) triggered = true; _cursor = point; }
            if (settings.Triggers.HasFlag(InputTriggers.MouseWheel) && wheel != _wheel) triggered = true;
        }
        _wheel = wheel;
        if (triggered) { _lastInput = time; _inputCount++; }
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    private static double Wave(EffectWaveform waveform, double phase) => waveform switch
    { EffectWaveform.Triangle => 1 - Math.Abs(phase * 2 - 1), EffectWaveform.Sawtooth => phase, EffectWaveform.Square => phase < .5 ? 0 : 1, _ => (1 - Math.Cos(phase * Math.PI * 2)) / 2 };
    private static double Tail(TailCurve curve, double value) => curve switch
    { TailCurve.Smooth => value * value * (3 - 2 * value), TailCurve.Quadratic => value * value, _ => value };
    private static double Star(LightingParameters settings, int led, double phase)
    {
        uint hash = unchecked((uint)(led * 2654435761u + (uint)settings.StarSeed)); hash ^= hash >> 16;
        if ((hash & 65535) / 65536d >= settings.StarDensity) return 0;
        return Math.Pow(Math.Max(0, Math.Sin(phase * Math.PI * 2 + led * 2.399 + settings.StarSeed)), settings.StarSharpness);
    }
    private static Color Palette(LightingParameters parameters, double position)
    {
        if (parameters.Gradient.Length == 0) return Palette(parameters.Palette, position);
        if (parameters.Gradient.Length == 1) return ColorTranslator.FromHtml(parameters.Gradient[0]);
        double scaled = Math.Clamp(position, 0, 1) * (parameters.Gradient.Length - 1);
        int index = Math.Min((int)scaled, parameters.Gradient.Length - 2);
        return Mix(ColorTranslator.FromHtml(parameters.Gradient[index]), ColorTranslator.FromHtml(parameters.Gradient[index + 1]), scaled - index);
    }
    private static Color Palette(int palette, double position) => palette switch
    {
        1 => Mix(Color.DeepSkyBlue, Color.MediumPurple, (Math.Sin(position * Math.PI * 2) + 1) / 2),
        2 => Mix(Color.Red, Color.Gold, (Math.Sin(position * Math.PI * 2) + 1) / 2),
        3 => Mix(Color.LimeGreen, Color.DeepSkyBlue, (Math.Sin(position * Math.PI * 2) + 1) / 2),
        _ => Hue(position % 1)
    };
    private static Color Mix(Color from, Color to, double ratio) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * ratio), (int)Math.Round(from.G + (to.G - from.G) * ratio), (int)Math.Round(from.B + (to.B - from.B) * ratio));
    private static Color Hue(double hue)
    {
        double position = hue * 6; int section = (int)position, rising = (int)((position - section) * 255), falling = 255 - rising;
        return section switch { 0 => Color.FromArgb(255, rising, 0), 1 => Color.FromArgb(falling, 255, 0), 2 => Color.FromArgb(0, 255, rising), 3 => Color.FromArgb(0, falling, 255), 4 => Color.FromArgb(rising, 0, 255), _ => Color.FromArgb(255, 0, falling) };
    }
    public void Dispose() { }
}
