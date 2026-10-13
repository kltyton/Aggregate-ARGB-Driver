using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HardwareLite.Devices.Usb;

internal sealed class WinUsbConnection : IDisposable
{
    private readonly SafeFileHandle _file;
    private IntPtr _usb;
    public IReadOnlyList<NativeUsb.PipeInformation> Pipes { get; }

    public WinUsbConnection(string hardwareId)
    {
        string path = NativeUsb.Find(NativeUsb.WinUsbInterface, hardwareId)
            ?? throw new IOException("未找到 USB 设备。");
        _file = NativeUsb.Open(path, true);
        try
        {
            if (!NativeUsb.WinUsb_Initialize(_file, out _usb)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!NativeUsb.WinUsb_QueryInterfaceSettings(_usb, 0, out var descriptor)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var pipes = new List<NativeUsb.PipeInformation>();
            for (byte i = 0; i < descriptor.EndpointCount; i++)
            {
                if (!NativeUsb.WinUsb_QueryPipe(_usb, 0, i, out var pipe)) throw new Win32Exception(Marshal.GetLastWin32Error());
                uint timeout = 1500;
                if (!NativeUsb.WinUsb_SetPipePolicy(_usb, pipe.Id, 3, 4, ref timeout)) throw new Win32Exception(Marshal.GetLastWin32Error());
                pipes.Add(pipe);
            }
            Pipes = pipes;
        }
        catch { Dispose(); throw; }
    }

    public void Write(byte pipe, byte[] buffer) => Write(pipe, buffer, buffer.Length);

    public void Write(byte pipe, byte[] buffer, int length)
    {
        if ((uint)length > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));
        if (!NativeUsb.WinUsb_WritePipe(_usb, pipe, buffer, (uint)length, out uint written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (written != length) throw new IOException("USB 数据未完整写入。");
    }

    public void Control(NativeUsb.SetupPacket packet, byte[] buffer)
    {
        if (packet.Length != buffer.Length) throw new ArgumentException("USB 控制包长度不符。");
        if (!NativeUsb.WinUsb_ControlTransfer(_usb, packet, buffer, (uint)buffer.Length, out uint transferred, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (transferred != buffer.Length) throw new IOException("USB 控制包未完整传输。");
    }

    public byte[] Read(byte pipe, int maximum)
    {
        var bytes = new byte[maximum];
        int read = ReadInto(pipe, bytes, maximum);
        Array.Resize(ref bytes, read);
        return bytes;
    }

    public int ReadInto(byte pipe, byte[] bytes, int maximum)
    {
        if (maximum <= 0 || maximum > bytes.Length) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (!NativeUsb.WinUsb_ReadPipe(_usb, pipe, bytes, (uint)maximum, out uint read, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (read > maximum) throw new IOException("USB 回应长度超过接收缓冲。");
        return checked((int)read);
    }

    public void SetTimeout(byte pipe, uint milliseconds)
    {
        if (!NativeUsb.WinUsb_SetPipePolicy(_usb, pipe, 3, 4, ref milliseconds))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_usb != IntPtr.Zero) { NativeUsb.WinUsb_Free(_usb); _usb = IntPtr.Zero; }
        _file.Dispose();
    }
}
