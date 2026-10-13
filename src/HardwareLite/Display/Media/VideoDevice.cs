using System.Runtime.InteropServices;
namespace HardwareLite.Display.Media;

internal sealed class VideoDevice : IDisposable
{
    private IntPtr _device;
    internal IntPtr Device => _device;
    public IMfDxgiDeviceManager Manager { get; private set; } = null!;
    public VideoAdapter Adapter { get; private set; } = null!;
    public VideoDevice(string adapterId = "")
    {
        try
        {
            nint adapter = VideoAdapters.Open(adapterId, out var information);
            try
            {
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x820, IntPtr.Zero, 0, 7, out _device, out _, IntPtr.Zero));
                Adapter = information;
            }
            finally { Marshal.Release(adapter); }
            object deviceObject = Marshal.GetObjectForIUnknown(_device);
            try { ((ID3DMultithread)deviceObject).SetMultithreadProtected(true); }
            finally { Marshal.ReleaseComObject(deviceObject); }
            Marshal.ThrowExceptionForHR(MFCreateDXGIDeviceManager(out uint token, out var manager));
            Manager = manager;
            Manager.ResetDevice(_device, token);
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (Manager is not null) { Marshal.ReleaseComObject(Manager); Manager = null!; }
        if (_device != IntPtr.Zero) { Marshal.Release(_device); _device = IntPtr.Zero; }
    }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, IntPtr context);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateDXGIDeviceManager(out uint token, out IMfDxgiDeviceManager manager);
}
[ComImport, Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3DMultithread
{
    [PreserveSig] void Enter();
    [PreserveSig] void Leave();
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.Bool)] bool SetMultithreadProtected([MarshalAs(UnmanagedType.Bool)] bool enabled);
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.Bool)] bool GetMultithreadProtected();
}
[ComImport, Guid("eb533d5d-2db6-40f8-97a9-494692014f07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMfDxgiDeviceManager
{
    void CloseDeviceHandle(IntPtr handle);
    void GetVideoService(IntPtr handle, ref Guid iid, out IntPtr service);
    void LockDevice(IntPtr handle, ref Guid iid, out IntPtr device, [MarshalAs(UnmanagedType.Bool)] bool block);
    void OpenDeviceHandle(out IntPtr handle);
    void ResetDevice(IntPtr device, uint token);
    void TestDevice(IntPtr handle);
    void UnlockDevice(IntPtr handle, [MarshalAs(UnmanagedType.Bool)] bool save);
}
[ComImport, Guid("33ae5ea6-4316-436f-8ddd-d73d22f829ec"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMfBuffer2D
{
    void Lock2D(out IntPtr scanline, out int pitch);
    void Unlock2D();
    void GetScanline0AndPitch(out IntPtr scanline, out int pitch);
    void IsContiguousFormat([MarshalAs(UnmanagedType.Bool)] out bool contiguous);
    void GetContiguousLength(out uint length);
    void ContiguousCopyTo(IntPtr buffer, uint length);
    void ContiguousCopyFrom(IntPtr buffer, uint length);
    void Lock2DSize(uint flags, out IntPtr scanline, out int pitch, out IntPtr start, out uint length);
    void Copy2DTo([MarshalAs(UnmanagedType.Interface)] IMfBuffer2D destination);
}
