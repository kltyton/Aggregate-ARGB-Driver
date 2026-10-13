namespace HardwareLite.Display.Media;

internal static class MediaFile
{
    public const string DialogFilter = "图片、GIF、MP4 和 AVI|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.avi";
    public static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".avi";
    public static Image OpenPreview(string path)
    {
        if (!IsVideo(path)) return Image.FromFile(path);
        using var reader = new VideoFrameReader(path);
        return (Image)reader.ReadFrame(0).Clone();
    }
    public static Image OpenThumbnail(string path, int width, int height)
    {
        if (IsVideo(path))
        {
            using var reader = new VideoFrameReader(path);
            return reader.ReadFrame(0).GetThumbnailImage(width, height, null, IntPtr.Zero);
        }
        using var source = OpenPreview(path);
        return source.GetThumbnailImage(width, height, null, IntPtr.Zero);
    }
}
