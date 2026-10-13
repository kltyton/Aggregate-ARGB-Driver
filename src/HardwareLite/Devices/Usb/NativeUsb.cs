using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HardwareLite.Devices.Usb;

internal static class NativeUsb
{
    internal static readonly Guid HidInterface = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    internal static readonly Guid WinUsbInterface = new("DEE824EF-729B-4A0E-9C14-B7117D33A817");

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClass;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct InterfaceDescriptor
    {
        public byte Length, DescriptorType, Number, AlternateSetting, EndpointCount, Class, Subclass, Protocol, Description;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SetupPacket
    {
        public byte RequestType, Request;
        public ushort Value, Index, Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipeInformation
    {
        public int Type;
        public byte Id;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidCapabilities
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }

    internal static string? Find(Guid interfaceClass, string hardwareId)
    {
        IntPtr list = SetupDiGetClassDevs(ref interfaceClass, null, IntPtr.Zero, 0x12);
        if (list == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var info = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
            for (uint i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref interfaceClass, i, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) return null;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetail(list, ref info, IntPtr.Zero, 0, out uint bytes, IntPtr.Zero);
                if (bytes < 6) throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr detail = Marshal.AllocHGlobal(checked((int)bytes));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref info, detail, bytes, out _, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4))!;
                    if (path.Contains(hardwareId, StringComparison.OrdinalIgnoreCase)) return path;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }
    }

    internal static SafeFileHandle Open(string path, bool overlapped)
    {
        var handle = CreateFile(path, 0xC0000000, 0, IntPtr.Zero, 3, overlapped ? 0x40000080u : 0x80u, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return handle;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr list, IntPtr device, ref Guid guid, uint index, ref DeviceInterfaceData info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr list, ref DeviceInterfaceData info, IntPtr detail, uint size, out uint needed, IntPtr device);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint count, out uint written, IntPtr overlapped);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle file, out IntPtr data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, out HidCapabilities caps);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_Initialize(SafeFileHandle file, out IntPtr usb);
    [DllImport("winusb.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_Free(IntPtr usb);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_QueryInterfaceSettings(IntPtr usb, byte alternate, out InterfaceDescriptor descriptor);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_QueryPipe(IntPtr usb, byte alternate, byte index, out PipeInformation info);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_SetPipePolicy(IntPtr usb, byte pipe, uint policy, uint length, ref uint value);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_ControlTransfer(IntPtr usb, SetupPacket packet, byte[] bytes, uint count, out uint transferred, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_WritePipe(IntPtr usb, byte pipe, byte[] bytes, uint count, out uint written, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinUsb_ReadPipe(IntPtr usb, byte pipe, byte[] bytes, uint count, out uint read, IntPtr overlapped);
}
