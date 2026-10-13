using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HardwareLite.Display.Media;

internal sealed class VideoReadback : IDisposable
{
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private readonly nint _device;
    private nint _context, _texture;
    private TextureDescription _description;
    private bool _mapped;
    private readonly MapTexture _map;
    private readonly UnmapTexture _unmap;
    private readonly CopyRegion _copy;
    private readonly CreateTexture _create;

    public VideoReadback(nint device)
    {
        _device = device;
        Function<GetContext>(device, 40)(device, out _context);
        _map = Function<MapTexture>(_context, 14);
        _unmap = Function<UnmapTexture>(_context, 15);
        _copy = Function<CopyRegion>(_context, 46);
        _create = Function<CreateTexture>(device, 5);
    }

    public Bitmap Read(IMfDxgiBuffer buffer, int width, int height)
    {
        var iid = TextureId;
        buffer.GetResource(ref iid, out nint source);
        try
        {
            buffer.GetSubresourceIndex(out uint subresource);
            Function<GetDescription>(source, 10)(source, out var description);
            if (description.MipLevels == 0 || subresource >= (ulong)description.MipLevels * description.ArraySize
                || description.SampleCount != 1 || description.Format is not (87 or 88))
                throw new InvalidDataException("硬件视频帧不是可读取的 BGRA32 纹理。");
            uint mip = subresource % description.MipLevels;
            if (mip >= 32 || Math.Max(1u, description.Width >> (int)mip) < width
                || Math.Max(1u, description.Height >> (int)mip) < height)
                throw new InvalidDataException("硬件视频纹理小于输出帧尺寸。");
            if (_texture == 0 || _description.Width != width || _description.Height != height || _description.Format != description.Format)
            {
                Unmap();
                if (_texture != 0) { Marshal.Release(_texture); _texture = 0; }
                _description = new TextureDescription
                {
                    Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                    Format = description.Format, SampleCount = 1, Usage = 3, CpuAccessFlags = 0x20000
                };
                Marshal.ThrowExceptionForHR(_create(_device, ref _description, 0, out _texture));
            }
            var box = new SourceBox { Right = (uint)width, Bottom = (uint)height, Back = 1 };
            _copy(_context, _texture, 0, 0, 0, 0, source, subresource, ref box);
            Marshal.ThrowExceptionForHR(_map(_context, _texture, 0, 1, 0, out var mapped));
            _mapped = true;
            try
            {
                if (mapped.Data == 0 || mapped.RowPitch < checked(width * 4) || mapped.RowPitch > int.MaxValue)
                    throw new InvalidDataException("硬件视频读回缓冲的行长度无效。");
                return new Bitmap(width, height, (int)mapped.RowPitch, PixelFormat.Format32bppRgb, mapped.Data);
            }
            catch { Unmap(); throw; }
        }
        finally { Marshal.Release(source); }
    }

    public void Unmap()
    {
        if (!_mapped) return;
        _unmap(_context, _texture, 0);
        _mapped = false;
    }

    public void Dispose()
    {
        Unmap();
        if (_texture != 0) { Marshal.Release(_texture); _texture = 0; }
        if (_context != 0) { Marshal.Release(_context); _context = 0; }
    }

    private static T Function<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MappedTexture { public nint Data; public uint RowPitch, DepthPitch; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SourceBox { public uint Left, Top, Front, Right, Bottom, Back; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetContext(nint device, out nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture(nint device, ref TextureDescription description, nint initialData, out nint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDescription(nint texture, out TextureDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyRegion(nint context, nint destination, uint destinationSubresource, uint x, uint y, uint z,
        nint source, uint sourceSubresource, ref SourceBox box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapTexture(nint context, nint resource, uint subresource, uint mapType, uint flags, out MappedTexture mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapTexture(nint context, nint resource, uint subresource);
}

[ComImport, Guid("e7174cfa-1c9e-48b1-8866-626226bfc258"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMfDxgiBuffer
{
    void GetResource(ref Guid iid, out nint resource);
    void GetSubresourceIndex(out uint index);
    void GetUnknown(ref Guid key, ref Guid iid, out nint value);
    void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object? value);
}
