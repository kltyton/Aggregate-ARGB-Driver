using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareLite.Configuration;

namespace HardwareLite.Display.Media;

internal static class DeviceMediaPreparation
{
    public static string Key(ScreenSettings screen)
    {
        string[] paths = screen.Playback == MediaPlayback.Single ? [screen.MediaPath] : screen.MediaFiles;
        var files = paths.Select(path =>
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("媒体文件不存在。", file.FullName);
            return new { file.FullName, file.Length, file.LastWriteTimeUtc, Edit = screen.MediaEdits.GetValueOrDefault(path) ?? new MediaEdit() };
        }).ToArray();
        var layout = screen.Layout;
        var options = screen.Options;
        string parameters = JsonSerializer.Serialize(new
        {
            Version = 6, Files = files, screen.Rotation, options.DeviceEncoding, options.DeviceVideoBitrate,
            layout.ImageFit, layout.ImageScale, layout.ImageX, layout.ImageY, layout.ImageRotation, layout.BackgroundColor,
            screen.MaxFps, options.PlaybackSpeed, options.Interpolation,
            options.PreserveGifDelays, options.GifMinimumDelayMs, options.GifMissingDelayMs,
            options.VideoDecoder, options.VideoAdapterId, options.DecodeWidth, options.DecodeHeight
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parameters)));
    }

    public static Settings Prepare(Settings settings, string directory, long maximumBytes, Action<int> progress, CancellationToken cancellation)
    {
        var screen = settings.Screen;
        if (!screen.Enabled || !screen.Options.DevicePlayback || screen.Mode == ScreenMode.Monitor) return settings;
        string key = Key(screen);
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, key + ".hlm");
        if (File.Exists(destination) && new FileInfo(destination).Length > maximumBytes)
            throw new DeviceMediaCapacityException(new FileInfo(destination).Length, maximumBytes);
        if (!File.Exists(destination))
        {
            string pending = Path.Combine(directory, key + ".pending.hlm");
            try
            {
                DeviceMediaWriter.Write(pending, settings, maximumBytes, progress, cancellation);
                File.Move(pending, destination, true);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
        return settings with { Screen = screen with { DeviceMediaPath = destination } };
    }

    public static void ValidatePrepared(ScreenSettings screen)
    {
        if (screen.Mode == ScreenMode.Monitor) return;
        if (!File.Exists(screen.DeviceMediaPath) ||
            !string.Equals(Path.GetFileNameWithoutExtension(screen.DeviceMediaPath), Key(screen), StringComparison.Ordinal))
            throw new InvalidDataException("设备内媒体与当前裁剪或播放设置不匹配，请打开 GUI 重新保存。");
    }

    public static bool IsNativeVideo(ScreenSettings screen)
    {
        if (!screen.Enabled || !screen.Options.DevicePlayback || screen.Mode == ScreenMode.Monitor) return false;
        using var media = File.OpenRead(screen.DeviceMediaPath);
        Span<byte> magic = stackalloc byte[4]; media.ReadExactly(magic);
        return magic.SequenceEqual("HLV1"u8);
    }
}
