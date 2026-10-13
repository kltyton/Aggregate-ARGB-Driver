using System.Runtime.InteropServices;

namespace HardwareLite.Display.Media;

internal sealed record VideoAdapter(string Id, string Name, uint VendorId, uint DeviceId, ulong Luid);

internal static class VideoAdapters
{
    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
    public static IReadOnlyList<VideoAdapter> List()
    {
        var result = new List<VideoAdapter>();
        Enumerate((_, description) => { if ((description.Flags & 2) == 0) result.Add(Info(description)); return false; });
        return result;
    }
    public static nint Open(string id, out VideoAdapter information)
    {
        VideoAdapter? selected = null;
        nint pointer = Enumerate((_, description) =>
        {
            var info = Info(description);
            if ((description.Flags & 2) != 0 || id.Length > 0 && !string.Equals(id, info.Id, StringComparison.OrdinalIgnoreCase)) return false;
            selected = info; return true;
        });
        if (pointer == 0 || selected is null) throw new InvalidOperationException("未找到指定的视频适配器：" + id);
        information = selected; return pointer;
    }
    private static VideoAdapter Info(AdapterDescription description) => new(
        $"{description.VendorId:X4}:{description.DeviceId:X4}:{description.SubsystemId:X8}",
        description.Name, description.VendorId, description.DeviceId, ((ulong)(uint)description.LuidHigh << 32) | description.LuidLow);
    private static nint Enumerate(Func<nint, AdapterDescription, bool> select)
    {
        Guid iid = FactoryId;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out nint factory));
        try
        {
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0; ; index++)
            {
                int result = enumerate(factory, index, out nint adapter);
                if (result == unchecked((int)0x887A0002)) return 0;
                Marshal.ThrowExceptionForHR(result);
                bool retained = false;
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out var description));
                    if (select(adapter, description)) { retained = true; return adapter; }
                }
                finally { if (!retained) Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
    }
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters(nint factory, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescription(nint adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }
}
