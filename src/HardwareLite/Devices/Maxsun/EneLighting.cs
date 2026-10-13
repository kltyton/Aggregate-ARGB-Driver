using System.Buffers.Binary;
using HardwareLite.Configuration;
using HardwareLite.Devices.Usb;
using Microsoft.Win32;
using HardwareLite.Monitoring;

namespace HardwareLite.Devices.Maxsun;

internal sealed class EneLighting : IDisposable
{
    private readonly WinUsbConnection _usb;
    private readonly int[] _groupSizes;
    private byte[]? _last;
    private int _lastGroup = -2;
    private readonly LightingEffects _effects = new();
    private readonly LightingEffects[] _groupEffects = Enumerable.Range(0, 5).Select(_ => new LightingEffects()).ToArray();
    private readonly byte[]?[] _groupFrames = new byte[5][];
    private readonly byte[]?[] _groupWork = new byte[5][];
    private byte[]? _work;
    private static readonly byte[] Apply = [1];
    private AudioSpectrum? _audio;
    private AudioOptions? _audioOptions;
    private MouseWheelActivity? _wheel;
    private LightingSettings? _settings;
    private readonly LightingSettings?[] _selected = new LightingSettings?[5];
    private readonly bool[] _constantApplied = new bool[5];
    private bool _constant, _constantAppliedAll, _needsAudio, _needsWheel;

    public EneLighting()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        if (!string.Equals(bios?.GetValue("BaseBoardManufacturer") as string, "MAXSUN", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(bios?.GetValue("BaseBoardProduct") as string, "MS-Terminator B850M PRO", StringComparison.OrdinalIgnoreCase))
            throw new IOException("主板型号与当前支持的铭瑄 B850M PRO 不符。");
        _usb = new WinUsbConnection("vid_0cf2&pid_7750&mi_00");
        try
        {
            byte[] identity = Read(0xB500, 5);
            if (identity[0] != 0x71 || identity[3] >= 2) throw new IOException("ENE 控制器标识不符。");
            int groups = Read(0xE0A1, 1)[0] + Read(0xE7A1, 1)[0];
            if (groups is < 1 or > 8) throw new IOException("ENE 灯光分组数量不受支持。");
            byte[] counts = Read(0xE0A2, groups * 2);
            _groupSizes = Enumerable.Range(0, groups).Select(i => (int)BinaryPrimitives.ReadUInt16BigEndian(counts.AsSpan(i * 2, 2))).ToArray();
            if (!_groupSizes.SequenceEqual(new[] { 128, 128, 1, 1, 6 })) throw new IOException("ENE 灯珠分组与本机已确认的配置不符。");
        }
        catch { _usb.Dispose(); throw; }
    }

    public void Update(LightingSettings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        if (!ReferenceEquals(settings, _settings))
        {
            _settings = settings;
            _constantAppliedAll = false; Array.Clear(_constantApplied);
            _constant = Constant(settings); _needsAudio = settings.Mode == LightingMode.Audio; _needsWheel = Wheel(settings);
            for (int group = 0; group < _selected.Length; group++)
            {
                _selected[group] = settings.Options.Groups.TryGetValue(group, out var profile)
                    ? settings with { Mode = profile.Mode, Brightness = profile.Brightness, Profiles = profile.Profiles }
                    : settings.Group >= 0 && settings.Group != group ? null : settings;
                if (_selected[group] is not { } selected) continue;
                _constant &= Constant(selected); _needsAudio |= selected.Mode == LightingMode.Audio; _needsWheel |= Wheel(selected);
            }
        }
        if (_constant && _constantAppliedAll) return;
        bool needsAudio = _needsAudio;
        if (!needsAudio || _audioOptions != settings.Audio) { _audio?.Dispose(); _audio = null; _audioOptions = null; }
        if (needsAudio && _audio is null) { _audio = new AudioSpectrum(settings.Audio); _audioOptions = settings.Audio; }
        bool needsWheel = _needsWheel;
        if (!needsWheel) { _wheel?.Dispose(); _wheel = null; } else _wheel ??= new MouseWheelActivity();
        if (settings.Options.Groups.Count > 0)
        {
            _last = null; _lastGroup = -2;
            for (int group = 0; group < _groupSizes.Length; group++)
            {
                if (_selected[group] is not { } selected || _constantApplied[group]) continue;
                if (KeepLast(selected, snapshot)) continue;
                var pixels = _groupEffects[group].Render(_groupSizes[group], selected, snapshot, elapsedMs, _audio?.Levels, _wheel?.Sequence ?? 0);
                byte[] bytes = Bytes(pixels, ref _groupWork[group]);
                if (_groupFrames[group] is { } previous && bytes.AsSpan().SequenceEqual(previous)) { _constantApplied[group] = Constant(selected); continue; }
                Write((uint)(0xE300 + _groupSizes.Take(group).Sum() * 3), bytes);
                Write((uint)(0xE021 + group * 16), Apply); Write((uint)(0xE02F + group * 16), Apply);
                _groupFrames[group] ??= new byte[bytes.Length]; bytes.CopyTo(_groupFrames[group]!, 0);
                _constantApplied[group] = Constant(selected);
            }
            _constantAppliedAll = _constant;
            return;
        }
        Array.Clear(_groupFrames);
        if (KeepLast(settings, snapshot)) return;
        int count = settings.Group < 0 ? _groupSizes.Sum() : _groupSizes[settings.Group];
        var frame = _effects.Render(count, settings, snapshot, elapsedMs, _audio?.Levels, _wheel?.Sequence ?? 0);
        var colors = Bytes(frame, ref _work);
        if (_lastGroup == settings.Group && _last is not null && colors.AsSpan().SequenceEqual(_last)) { _constantAppliedAll = _constant; return; }
        int offset = settings.Group < 0 ? 0 : _groupSizes.Take(settings.Group).Sum() * 3;
        Write((uint)(0xE300 + offset), colors);
        for (int group = 0; group < _groupSizes.Length; group++)
        {
            if (settings.Group >= 0 && settings.Group != group) continue;
            Write((uint)(0xE021 + group * 16), Apply);
            Write((uint)(0xE02F + group * 16), Apply);
        }
        if (_last?.Length != colors.Length) _last = new byte[colors.Length];
        colors.CopyTo(_last!, 0); _lastGroup = settings.Group;
        _constantAppliedAll = _constant;
    }
    private static byte[] Bytes(Color[] frame, ref byte[]? storage)
    {
        if (storage?.Length != frame.Length * 3) storage = new byte[frame.Length * 3];
        var bytes = storage;
        for (int i = 0; i < frame.Length; i++) { bytes[i * 3] = frame[i].R; bytes[i * 3 + 1] = frame[i].B; bytes[i * 3 + 2] = frame[i].G; }
        return bytes;
    }
    private static bool KeepLast(LightingSettings settings, SensorSnapshot snapshot) =>
        settings.Mode == LightingMode.Smart && settings.Options.ExpiredData == MissingDataPolicy.KeepLast &&
        (!snapshot.IsFresh || !(settings.Parameters.SmartSource switch { SmartSource.CpuTemperature => snapshot.CpuTemperature, SmartSource.CpuUsage => snapshot.CpuUsage, SmartSource.GpuTemperature => snapshot.GpuTemperature, _ => snapshot.GpuUsage }).HasValue);
    private static bool Constant(LightingSettings settings) => settings.Mode is LightingMode.Static or LightingMode.Off;
    private static bool Wheel(LightingSettings settings) => settings.Mode == LightingMode.InputReactive && settings.Parameters.InputSource != InputSource.Keyboard && settings.Parameters.Triggers.HasFlag(InputTriggers.MouseWheel);
    public void TurnOff()
    {
        _settings = null; _constantAppliedAll = false; Array.Clear(_constantApplied);
        Write(0xE300, new byte[_groupSizes.Sum() * 3]);
        for (int group = 0; group < _groupSizes.Length; group++) { Write((uint)(0xE021 + group * 16), Apply); Write((uint)(0xE02F + group * 16), Apply); }
    }

    private byte[] Read(uint address, int count)
    {
        var data = new byte[count];
        _usb.Control(new NativeUsb.SetupPacket { RequestType = 0xC0, Request = 0x81, Value = (ushort)(address >> 16), Index = (ushort)address, Length = (ushort)count }, data);
        return data;
    }

    private void Write(uint address, byte[] data) => _usb.Control(new NativeUsb.SetupPacket
        { RequestType = 0x40, Request = 0x80, Value = (ushort)(address >> 16), Index = (ushort)address, Length = (ushort)data.Length }, data);

    public void Dispose() { _audio?.Dispose(); _wheel?.Dispose(); _effects.Dispose(); foreach (var effect in _groupEffects) effect.Dispose(); _usb.Dispose(); }
}
