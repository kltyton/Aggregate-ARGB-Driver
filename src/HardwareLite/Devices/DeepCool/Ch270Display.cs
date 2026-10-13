using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using HardwareLite.Configuration;
using HardwareLite.Devices.Usb;
using HardwareLite.Monitoring;
using Microsoft.Win32.SafeHandles;

namespace HardwareLite.Devices.DeepCool;

internal sealed class Ch270Display : IDisposable
{
    private readonly SafeFileHandle _file;
    private byte[]? _last;
    private readonly byte[] _report = new byte[64];
    private static readonly CaseSubPage[] CpuPages = [CaseSubPage.CpuFrequency];
    private static readonly CaseSubPage[] CpuFanPages = [CaseSubPage.CpuFrequency, CaseSubPage.CpuFan];
    private static readonly CaseSubPage[] AllPages = [CaseSubPage.CpuFrequency, CaseSubPage.CpuFan, CaseSubPage.Gpu];
    private static readonly CaseSubPage[] NoFanPages = [CaseSubPage.CpuFrequency, CaseSubPage.Gpu];

    public Ch270Display()
    {
        string path = NativeUsb.Find(NativeUsb.HidInterface, "vid_3633&pid_0016")
            ?? throw new IOException("未找到 CH270 屏幕。");
        _file = NativeUsb.Open(path, false);
        try
        {
            if (!NativeUsb.HidD_GetPreparsedData(_file, out var data)) throw new IOException("无法读取 CH270 HID 描述符。");
            try
            {
                int status = NativeUsb.HidP_GetCaps(data, out var caps);
                if (status != 0x110000 || caps.OutputLength != 64)
                    throw new IOException($"CH270 HID 描述符不符：状态 0x{status:X8}，输出长度 {caps.OutputLength}。");
            }
            finally { NativeUsb.HidD_FreePreparsedData(data); }
        }
        catch { _file.Dispose(); throw; }
    }

    public bool Update(SensorSnapshot snapshot, Settings settings, long elapsedMs)
    {
        var config = settings.Case;
        float? fan = config.Options.FanSensorId.Length == 0 ? snapshot.CpuFan
            : snapshot.Sensors.GetValueOrDefault(config.Options.FanSensorId)?.Value;
        byte mode;
        if (config.Page == CasePage.Off) mode = 0;
        else if (config.Page == CasePage.Gpu) mode = 4;
        else if (config.Page == CasePage.CpuFrequency) mode = 2;
        else if (config.Page == CasePage.CpuFan) mode = 3;
        else
        {
            CaseSubPage[] pages = config.Page == CasePage.Cpu
                ? fan.HasValue ? CpuFanPages : CpuPages
                : config.Options.CyclePages.Length > 0 ? config.Options.CyclePages
                : fan.HasValue ? AllPages : NoFanPages;
            int duration = 0;
            foreach (var page in pages) duration += config.Options.PageDurationsMs.GetValueOrDefault(page, config.SwitchSeconds * 1000);
            long position = elapsedMs % duration;
            mode = (byte)pages[^1];
            foreach (var page in pages)
            {
                int time = config.Options.PageDurationsMs.GetValueOrDefault(page, config.SwitchSeconds * 1000);
                if (position < time) { mode = (byte)page; break; }
                position -= time;
            }
        }
        bool gpu = mode == 4;
        bool ready = mode == 0 || snapshot.IsFresh && (gpu
            ? snapshot.GpuTemperature.HasValue && snapshot.GpuUsage.HasValue && snapshot.GpuPower.HasValue && snapshot.GpuClock.HasValue
            : snapshot.CpuTemperature.HasValue && snapshot.CpuUsage.HasValue && snapshot.CpuPower.HasValue && (mode == 3 ? fan.HasValue : snapshot.CpuClock.HasValue));
        if (mode == 3 && !fan.HasValue) ready = false;
        if (!ready)
        {
            if (config.Options.MissingData == MissingDataPolicy.KeepLast && _last is not null) return false;
            if (config.Options.MissingData == MissingDataPolicy.Retry) throw new IOException("机箱页面所需数据未读取。");
            if (config.Options.MissingData != MissingDataPolicy.Placeholder || !snapshot.IsFresh) mode = 0;
        }
        byte[] report = _report;
        Encode(report, snapshot, fan, mode, config.Options.TemperatureUnit ?? settings.TemperatureUnit);
        if (_last is not null && report.AsSpan().SequenceEqual(_last)) return ready;
        if (!NativeUsb.WriteFile(_file, report, (uint)report.Length, out uint written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (written != report.Length) throw new IOException($"CH270 报告未完整写入：{written}/{report.Length} 字节。");
        _last ??= new byte[64]; report.CopyTo(_last, 0);
        return ready;
    }

    public void TurnOff() => Update(SensorSnapshot.Empty, new Settings { Case = new() { Page = CasePage.Off } }, 0);

    private static void Encode(byte[] report, SensorSnapshot data, float? fan, byte mode, TemperatureUnit unit)
    {
        Array.Clear(report);
        report[0] = 0x10; report[1] = 0x68; report[2] = 1; report[3] = 6; report[4] = 0x23; report[5] = 1;
        report[6] = mode;
        if (mode != 0)
        {
            bool gpu = mode == 4;
            WriteInteger(report, 7, gpu ? data.GpuPower : data.CpuPower);
            report[9] = unit == TemperatureUnit.Fahrenheit ? (byte)1 : (byte)0;
            WriteTemperature(report, 10, gpu ? data.GpuTemperature : data.CpuTemperature, unit);
            report[14] = (byte)Math.Clamp((int)(gpu ? data.GpuUsage ?? 0 : data.CpuUsage ?? 0), 0, 100);
            WriteInteger(report, 15, mode == 3 ? fan : gpu ? data.GpuClock : data.CpuClock);
            WriteInteger(report, 17, fan);
            WriteInteger(report, 19, data.GpuPower);
            WriteTemperature(report, 21, data.GpuTemperature, unit);
            report[25] = (byte)Math.Clamp((int)(data.GpuUsage ?? 0), 0, 100);
            WriteInteger(report, 26, data.GpuClock);
        }
        for (int i = 1; i < 40; i++) report[40] = unchecked((byte)(report[40] + report[i]));
        report[41] = 0x16;
    }

    private static void WriteInteger(byte[] report, int offset, float? value) =>
        BinaryPrimitives.WriteUInt16BigEndian(report.AsSpan(offset, 2), (ushort)Math.Clamp((int)(value ?? 0), 0, ushort.MaxValue));

    private static void WriteTemperature(byte[] report, int offset, float? value, TemperatureUnit unit)
    {
        float temperature = value ?? 0;
        if (unit == TemperatureUnit.Fahrenheit && value.HasValue) temperature = temperature * 1.8f + 32;
        BinaryPrimitives.WriteInt32BigEndian(report.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(temperature));
    }

    public void Dispose() => _file.Dispose();
}
