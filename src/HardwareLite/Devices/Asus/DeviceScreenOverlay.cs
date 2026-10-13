using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HardwareLite.Configuration;
using HardwareLite.Display;
using HardwareLite.Monitoring;

namespace HardwareLite.Devices.Asus;

internal sealed class DeviceScreenOverlay : IDisposable
{
    private const int Rows = 64, Stride = 720 * 4, Bands = (720 + Rows - 1) / Rows;
    private readonly ScreenCanvas _canvas = new();
    private readonly Bitmap _frame = new(720, 720, PixelFormat.Format32bppArgb);
    private readonly byte[] _pixels = new byte[Rows * Stride];
    private readonly byte[] _hashes = new byte[Bands * 32];
    private readonly byte[] _instances = new byte[Bands * 6];
    private Settings? _settings, _metricSettings;
    private bool _initialized;

    public void Update(DevicePlaybackProtocol protocol, Settings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        if (!ReferenceEquals(settings, _settings))
        {
            _settings = settings;
            _metricSettings = settings with { Screen = settings.Screen with { Mode = ScreenMode.Monitor } };
            _initialized = false;
            _canvas.Invalidate();
        }
        if (!_canvas.IsDue(_metricSettings!, snapshot, elapsedMs) && _initialized) return;
        _canvas.RenderOverlayInto(_frame, settings, snapshot);
        var data = _frame.LockBits(new Rectangle(0, 0, 720, 720), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        bool changed = !_initialized;
        try
        {
            Span<byte> hash = stackalloc byte[32];
            for (int band = 0; band < Bands; ++band)
            {
                int firstRow = band * Rows, rows = Math.Min(Rows, 720 - firstRow), count = rows * Stride;
                for (int row = 0; row < rows; ++row)
                    Marshal.Copy(data.Scan0 + (firstRow + row) * data.Stride, _pixels, row * Stride, Stride);
                SHA256.HashData(_pixels.AsSpan(0, count), hash);
                var previous = _hashes.AsSpan(band * 32, 32);
                if (!_initialized || !hash.SequenceEqual(previous))
                {
                    protocol.StoreSprite((ushort)band, 720, (ushort)rows, _pixels.AsSpan(0, count));
                    hash.CopyTo(previous);
                    changed = true;
                }
                BinaryPrimitives.WriteUInt16BigEndian(_instances.AsSpan(band * 6), (ushort)band);
                BinaryPrimitives.WriteUInt16BigEndian(_instances.AsSpan(band * 6 + 2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(_instances.AsSpan(band * 6 + 4), (ushort)firstRow);
            }
        }
        finally { _frame.UnlockBits(data); }
        if (changed) protocol.RenderSprites(_instances);
        _initialized = true;
    }

    public void Dispose() { _canvas.Dispose(); _frame.Dispose(); }
}
