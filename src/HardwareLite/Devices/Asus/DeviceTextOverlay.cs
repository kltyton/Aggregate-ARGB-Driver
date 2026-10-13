using System.Buffers.Binary;
using System.Text;
using HardwareLite.Configuration;
using HardwareLite.Display;
using HardwareLite.Monitoring;

namespace HardwareLite.Devices.Asus;

internal sealed class DeviceTextOverlay
{
    private readonly string[] _shown;
    private readonly string[] _retained;
    private const int MaximumPacketLength = 0x32000;
    private byte[] _packet = new byte[256];
    private ScreenSettings _screen;
    private readonly bool _clock;
    private long _lastFrame = -1;
    private int _lastSecond = -1;
    public DeviceTextOverlay(DevicePlaybackProtocol native, ScreenSettings screen, DeviceTextOverlay? previous = null)
    {
        if (!File.Exists(screen.DeviceUiPath)) throw new InvalidDataException("请在 GUI 保存一次配置，生成设备字形和布局包。");
        _screen = screen;
        _clock = screen.Layout.Elements.Any(item => item.Metric == ScreenMetric.AnalogClock);
        _shown = new string[screen.Layout.Elements.Length];
        _retained = new string[_shown.Length];
        if (previous is not null)
            for (int index = 0; index < _retained.Length; index++)
            {
                int oldIndex = Array.FindIndex(previous._screen.Layout.Elements, item => item == screen.Layout.Elements[index]);
                if (oldIndex >= 0) _retained[index] = previous._retained[oldIndex];
            }
        native.LoadUi(screen.DeviceUiPath, DeviceUiPreparation.ConfigurationKey(screen));
    }
    public void Update(DevicePlaybackProtocol native, Settings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        long slot = elapsedMs * settings.Screen.MaxFps / 1000;
        if (slot == _lastFrame) return;
        var now = DateTime.Now;
        bool changed = _lastFrame < 0 || _clock && _lastSecond != now.Second;
        int offset = 16, count = 0;
        for (int i = 0; i < _shown.Length; i++)
        {
            var item = _screen.Layout.Elements[i];
            string text = MetricText.Format(item, snapshot, _screen.Options.TemperatureUnit ?? settings.TemperatureUnit);
            if (text == "--")
            {
                text = _screen.Options.ExpiredData switch
                {
                    MissingDataPolicy.KeepLast => _retained[i] ?? "--",
                    MissingDataPolicy.Off => "",
                    MissingDataPolicy.Retry => throw new IOException("屏幕监控数据未读取：" + MetricText.Names[(int)item.Metric]),
                    _ => text
                };
            }
            else _retained[i] = text;
            changed |= text != _shown[i];
            _shown[i] = text;
            if (text.Length > 0 && item.Metric is ScreenMetric.Text or ScreenMetric.AnalogClock) text = " ";
            int runes = text.EnumerateRunes().Count();
            long required = offset + 8L + runes * 4L;
            if (required > MaximumPacketLength) throw new InvalidDataException("监控文字超过单次设备传输上限。");
            if (required > _packet.Length)
                Array.Resize(ref _packet, Math.Min(MaximumPacketLength, Math.Max((int)required, _packet.Length * 2)));
            Put((uint)i); Put((uint)runes);
            foreach (var rune in text.EnumerateRunes()) Put((uint)rune.Value);
            count++;
        }
        if (!changed) return;
        BinaryPrimitives.WriteUInt32LittleEndian(_packet, (uint)now.Hour);
        BinaryPrimitives.WriteUInt32LittleEndian(_packet.AsSpan(4), (uint)now.Minute);
        BinaryPrimitives.WriteUInt32LittleEndian(_packet.AsSpan(8), (uint)now.Second);
        BinaryPrimitives.WriteUInt32LittleEndian(_packet.AsSpan(12), (uint)count);
        native.UpdateUi(_packet.AsSpan(0, offset));
        _lastSecond = now.Second;
        _lastFrame = slot;
        void Put(uint value) { BinaryPrimitives.WriteUInt32LittleEndian(_packet.AsSpan(offset), value); offset += 4; }
    }

    public bool Reconfigure(ScreenSettings screen)
    {
        if (screen.DeviceUiPath != _screen.DeviceUiPath) return false;
        _screen = screen;
        return true;
    }
}
