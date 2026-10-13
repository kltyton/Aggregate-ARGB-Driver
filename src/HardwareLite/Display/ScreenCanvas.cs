using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using HardwareLite.Configuration;
using HardwareLite.Display.Media;
using HardwareLite.Monitoring;

namespace HardwareLite.Display;

internal sealed class ScreenCanvas : IDisposable
{
    private readonly bool _deviceOutput;
    private long _renderedVideoSequence = -1;
    public ScreenCanvas() { }
    public ScreenCanvas(bool deviceOutput) { _deviceOutput = deviceOutput; }
    private Image? _media;
    private VideoFrameReader? _video;
    public Image? CurrentMediaFrame => _video is null ? _media : _video.ReadFrame(_videoPosition);
    public string VideoDecoderStatus => _video?.DecoderStatus ?? "";
    public long MediaDurationMs => _video?.DurationMs ?? _delays.Sum(value => (long)value);
    public double MediaFrameRate => _video?.FrameRate ?? (_delays.Length > 0 ? 1000d / _delays.Min() : 0);
    public IReadOnlyList<int> MediaFrameDelays => _delays;
    private long _videoPosition;
    private string _path = "";
    private int _frameCount;
    private int[] _delays = [];
    private int _frame;
    private long _nextFrame;
    private readonly Dictionary<(string Family, int Size, bool Bold), Font> _fonts = [];
    private readonly StringFormat _numericFormat = StringFormat.GenericTypographic;
    private PrivateFontCollection _privateFonts = new();
    private string[] _fontFiles = [];
    private FontFamily[] _privateFamilies = [];
    private ScreenSettings? _lastSettings;
    private string _lastMetrics = "";
    private readonly Dictionary<ScreenElement, string> _retainedMetrics = [];
    private long _mediaSlot = -1;
    private int _mediaIndex;
    private (VideoDecoderMode Decoder, string Adapter, int Width, int Height, bool Preserve, int Minimum, int Missing) _mediaOptions;
    private string[] _shuffleFiles = [];
    private int[] _shuffleOrder = [];
    private int _shufflePosition;
    public void Invalidate() { _lastSettings = null; _lastMetrics = ""; ClearFonts(); }
    private void ClearFonts() { foreach (var font in _fonts.Values) font.Dispose(); _fonts.Clear(); }
    private string MediaPath(ScreenSettings settings, long elapsedMs)
    {
        if (settings.Mode == ScreenMode.Monitor) return "";
        if (settings.Playback == MediaPlayback.Single || settings.MediaFiles.Length == 0) return settings.MediaPath;
        long slot = elapsedMs / (settings.MediaIntervalSeconds * 1000L);
        if (slot != _mediaSlot)
        {
            _mediaSlot = slot;
            if (settings.Playback != MediaPlayback.Shuffle) _mediaIndex = (int)(slot % settings.MediaFiles.Length);
            else if (!settings.Options.ShuffleAvoidRepeats) _mediaIndex = Random.Shared.Next(settings.MediaFiles.Length);
            else
            {
                if (!_shuffleFiles.SequenceEqual(settings.MediaFiles) || _shufflePosition >= _shuffleOrder.Length)
                {
                    _shuffleFiles = [.. settings.MediaFiles]; _shuffleOrder = Enumerable.Range(0, settings.MediaFiles.Length).ToArray(); _shufflePosition = 0;
                    Random.Shared.Shuffle(_shuffleOrder);
                    if (_shuffleOrder.Length > 1 && _shuffleOrder[0] == _mediaIndex) (_shuffleOrder[0], _shuffleOrder[1]) = (_shuffleOrder[1], _shuffleOrder[0]);
                }
                _mediaIndex = _shuffleOrder[_shufflePosition++];
            }
        }
        return settings.MediaFiles[_mediaIndex % settings.MediaFiles.Length];
    }

    public bool IsDue(Settings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        string path = MediaPath(settings.Screen, elapsedMs); bool mediaChanged = _path != path; Load(path, settings.Screen.Options);
        bool settingsChanged = _lastSettings != settings.Screen;
        if (settingsChanged) { ClearFonts(); _retainedMetrics.Clear(); }
        string metrics = settings.Screen.Mode == ScreenMode.Media ? "" :
            string.Join("\n", settings.Screen.Layout.Elements.Select(item => FormatMetric(item, settings, snapshot)));
        bool due = settingsChanged || mediaChanged || metrics != _lastMetrics ||
            (settings.Screen.Mode != ScreenMode.Monitor && (_frameCount > 1 || _video is not null) && elapsedMs >= _nextFrame);
        if (_deviceOutput && _video is not null && due)
        {
            _videoPosition = VideoPosition(settings.Screen, checked((long)(elapsedMs * settings.Screen.Options.PlaybackSpeed)));
            _video.ReadFrame(_videoPosition);
            due = settingsChanged || mediaChanged || metrics != _lastMetrics || _video.FrameSequence != _renderedVideoSequence;
        }
        _lastSettings = settings.Screen; _lastMetrics = metrics;
        return due;
    }

    public Bitmap Render(int width, int height, Settings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        try { RenderInto(bitmap, settings, snapshot, elapsedMs); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }

    public void RenderInto(Bitmap bitmap, Settings settings, SensorSnapshot snapshot, long elapsedMs)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var config = settings.Screen;
        Load(MediaPath(config, elapsedMs), config.Options);
        long playbackMs = checked((long)(elapsedMs * config.Options.PlaybackSpeed));
        LoadFonts(config);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(ColorTranslator.FromHtml(config.Layout.BackgroundColor));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = config.Options.Interpolation;
        if (config.Mode != ScreenMode.Monitor && (_media is not null || _video is not null))
        {
            var edit = config.MediaEdits.GetValueOrDefault(_path) ?? new MediaEdit();
            Image media;
            if (_video is not null)
            {
                _videoPosition = VideoPosition(config, playbackMs);
                media = _video.ReadFrame(_videoPosition);
                _renderedVideoSequence = _video.FrameSequence;
                _nextFrame = elapsedMs + Math.Max(1, 1000 / config.MaxFps);
            }
            else
            {
                media = _media!;
            }
            if (_video is null && _frameCount > 1)
            {
                int total = _delays.Sum();
                int start = (int)(total * edit.Start), end = Math.Max(start + 1, (int)(total * edit.End));
                int position = start + (int)(playbackMs % (end - start));
                int cumulative = 0; _frame = 0;
                while (_frame < _frameCount - 1 && cumulative + _delays[_frame] <= position) cumulative += _delays[_frame++];
                media.SelectActiveFrame(FrameDimension.Time, _frame);
                _nextFrame = elapsedMs + Math.Max(1, (long)Math.Ceiling((Math.Min(cumulative + _delays[_frame], end) - position) / config.Options.PlaybackSpeed));
            }
            var source = new RectangleF((float)(edit.X * media.Width), (float)(edit.Y * media.Height), (float)(edit.Width * media.Width), (float)(edit.Height * media.Height));
            float sx = width / source.Width, sy = height / source.Height;
            float scale = config.Layout.ImageFit switch { ImageFit.Fill => Math.Max(sx, sy), ImageFit.Fit => Math.Min(sx, sy), ImageFit.Original => 1, _ => 1 };
            float imageWidth = config.Layout.ImageFit == ImageFit.Stretch ? width : source.Width * scale;
            float imageHeight = config.Layout.ImageFit == ImageFit.Stretch ? height : source.Height * scale;
            imageWidth *= config.Layout.ImageScale / 100f; imageHeight *= config.Layout.ImageScale / 100f;
            graphics.TranslateTransform(width / 2f + config.Layout.ImageX * width / 720f, height / 2f + config.Layout.ImageY * height / 720f);
            graphics.RotateTransform(config.Layout.ImageRotation + edit.Rotation);
            graphics.DrawImage(media, new RectangleF(-imageWidth / 2, -imageHeight / 2, imageWidth, imageHeight), source, GraphicsUnit.Pixel);
            graphics.ResetTransform();
        }
        if (config.Mode != ScreenMode.Media)
        {
            if (config.Mode == ScreenMode.MediaAndMonitor)
            {
                using var shade = new SolidBrush(Color.FromArgb(config.Layout.BackgroundShade * 255 / 100, Color.Black));
                graphics.FillRectangle(shade, 0, 0, width, height);
            }
            graphics.ScaleTransform(width / 720f, height / 720f);
            foreach (var item in config.Layout.Elements)
            {
                string text = FormatMetric(item, settings, snapshot);
                if (text.Length > 0) DrawMetric(graphics, item, text);
            }
        }
        graphics.ResetTransform();
        graphics.Dispose();
        if (config.Rotation != 0) bitmap.RotateFlip(config.Rotation switch { 1 => RotateFlipType.Rotate90FlipNone, 2 => RotateFlipType.Rotate180FlipNone, _ => RotateFlipType.Rotate270FlipNone });
    }

    private long VideoPosition(ScreenSettings settings, long playbackMs)
    {
        var edit = settings.MediaEdits.GetValueOrDefault(_path) ?? new MediaEdit();
        long start = (long)(_video!.DurationMs * edit.Start), end = Math.Max(start + 1, (long)(_video.DurationMs * edit.End));
        return start + playbackMs % (end - start);
    }

    public void RenderOverlayInto(Bitmap bitmap, Settings settings, SensorSnapshot snapshot)
    {
        var config = settings.Screen;
        LoadFonts(config);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(config.Mode == ScreenMode.Monitor ? ColorTranslator.FromHtml(config.Layout.BackgroundColor) : Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (config.Mode == ScreenMode.MediaAndMonitor && config.Layout.BackgroundShade > 0)
            {
                using var shade = new SolidBrush(Color.FromArgb(config.Layout.BackgroundShade * 255 / 100, Color.Black));
                graphics.FillRectangle(shade, 0, 0, bitmap.Width, bitmap.Height);
            }
            graphics.ScaleTransform(bitmap.Width / 720f, bitmap.Height / 720f);
            if (config.Mode != ScreenMode.Media)
                foreach (var item in config.Layout.Elements)
                {
                    string text = FormatMetric(item, settings, snapshot);
                    if (text.Length > 0) DrawMetric(graphics, item, text);
                }
        }
        if (config.Rotation != 0) bitmap.RotateFlip(config.Rotation switch { 1 => RotateFlipType.Rotate90FlipNone, 2 => RotateFlipType.Rotate180FlipNone, _ => RotateFlipType.Rotate270FlipNone });
    }

    private void LoadFonts(ScreenSettings config)
    {
        if (_fontFiles.SequenceEqual(config.FontFiles)) return;
        ClearFonts(); foreach (var family in _privateFamilies) family.Dispose(); _privateFonts.Dispose(); _privateFonts = new();
        foreach (string file in config.FontFiles) _privateFonts.AddFontFile(file);
        _privateFamilies = _privateFonts.Families;
        _fontFiles = [..config.FontFiles];
    }

    private string FormatMetric(ScreenElement item, Settings settings, SensorSnapshot snapshot)
    {
        string text = MetricText.Format(item, snapshot, settings.Screen.Options.TemperatureUnit ?? settings.TemperatureUnit);
        if (text != "--") { _retainedMetrics[item] = text; return text; }
        return settings.Screen.Options.ExpiredData switch
        {
            MissingDataPolicy.KeepLast => _retainedMetrics.GetValueOrDefault(item, "--"),
            MissingDataPolicy.Off => "",
            MissingDataPolicy.Retry => throw new IOException("屏幕监控数据未读取：" + MetricText.Names[(int)item.Metric]),
            _ => text
        };
    }

    private void DrawMetric(Graphics graphics, ScreenElement item, string value)
    {
        if (item.Metric == ScreenMetric.AnalogClock) { DrawClock(graphics, item); return; }
        using var titleBrush = new SolidBrush(ColorTranslator.FromHtml(item.TitleColor));
        using var valueBrush = new SolidBrush(ColorTranslator.FromHtml(item.Color));
        using var format = new StringFormat { Alignment = item.Alignment switch { TextAlignment.Center => StringAlignment.Center, TextAlignment.Right => StringAlignment.Far, _ => StringAlignment.Near }, Trimming = StringTrimming.EllipsisCharacter };
        var state = graphics.Save();
        graphics.SetClip(new Rectangle(item.X, item.Y, item.Width, item.Height));
        int captionHeight = item.Caption.Length > 0 ? item.CaptionHeight : 0;
        if (captionHeight > 0)
        {
            using var captionBrush = new SolidBrush(ColorTranslator.FromHtml(item.CaptionColor));
            graphics.DrawString(item.Caption, Font(item.CaptionFontFamily.Length > 0 ? item.CaptionFontFamily : item.FontFamily, item.CaptionFontSize, false), captionBrush, new RectangleF(item.X, item.Y, item.Width, captionHeight), format);
        }
        int titleHeight = item.Title.Length > 0 ? (item.TitleFontSize > 0 ? item.TitleFontSize : Math.Max(16, item.FontSize / 2)) : 0;
        if (titleHeight > 0) graphics.DrawString(item.Title, Font(item.TitleFontFamily.Length > 0 ? item.TitleFontFamily : item.FontFamily, titleHeight, false), titleBrush, new RectangleF(item.X, item.Y + captionHeight, item.Width, titleHeight + 10), format);
        int valueOffset = captionHeight + titleHeight + (titleHeight > 0 ? 8 : 0);
        if (item.UnitFontSize > 0 && value.LastIndexOf(' ') is int separator && separator > 0)
        {
            var numericFormat = _numericFormat;
            var numberFont = Font(item.FontFamily, item.FontSize, item.Bold);
            var unitFont = Font(item.UnitFontFamily.Length > 0 ? item.UnitFontFamily : item.FontFamily, item.UnitFontSize, false);
            string number = value[..separator], unit = value[(separator + 1)..];
            float numberWidth = graphics.MeasureString(number, numberFont, PointF.Empty, numericFormat).Width;
            float unitWidth = graphics.MeasureString(unit, unitFont, PointF.Empty, numericFormat).Width;
            float x = item.X + (item.Alignment switch { TextAlignment.Center => (item.Width - numberWidth - unitWidth) / 2, TextAlignment.Right => item.Width - numberWidth - unitWidth, _ => 0 });
            float baseline = item.Y + valueOffset + Ascent(numberFont);
            graphics.DrawString(number, numberFont, valueBrush, x, item.Y + valueOffset, numericFormat);
            graphics.DrawString(unit, unitFont, valueBrush, x + numberWidth + 3, baseline - Ascent(unitFont), numericFormat);
        }
        else graphics.DrawString(value, Font(item.FontFamily, item.FontSize, item.Bold), valueBrush, new RectangleF(item.X, item.Y + valueOffset, item.Width, item.Height - valueOffset), format);
        graphics.Restore(state);
    }

    private static float Ascent(Font font) => font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);

    internal Font PreparedFont(ScreenSettings settings, string family, int size, bool bold)
    {
        LoadFonts(settings);
        return Font(family, size, bold);
    }

    internal void RenderPreparedElement(Bitmap bitmap, ScreenElement item)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.TranslateTransform(-item.X, -item.Y);
        if (item.Metric == ScreenMetric.AnalogClock) DrawClock(graphics, item, false);
        else DrawMetric(graphics, item, item.Metric == ScreenMetric.Text ? item.Text : "");
    }

    private void DrawClock(Graphics graphics, ScreenElement item, bool hands = true)
    {
        float radius = Math.Min(item.Width, item.Height) / 2f - 8;
        float x = item.X + item.Width / 2f, y = item.Y + item.Height / 2f;
        var clock = item.Clock;
        using var face = new Pen(ColorTranslator.FromHtml(item.TitleColor), clock.FaceWidth);
        using var hand = new Pen(ColorTranslator.FromHtml(item.Color), clock.HourWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (clock.FaceVisible) graphics.DrawEllipse(face, x - radius, y - radius, radius * 2, radius * 2);
        for (int hour = 1; hour <= 12; hour++)
        {
            double angle = hour * Math.PI / 6;
            if (clock.HourMarksVisible) graphics.DrawLine(face, x + (float)Math.Sin(angle) * radius * 0.88f, y - (float)Math.Cos(angle) * radius * 0.88f, x + (float)Math.Sin(angle) * radius, y - (float)Math.Cos(angle) * radius);
            if (clock.HourNumbersVisible)
            {
                using var brush = new SolidBrush(face.Color);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                graphics.DrawString(hour.ToString(), Font(item.FontFamily, Math.Max(8, (int)(radius / 6)), false), brush,
                    x + (float)Math.Sin(angle) * radius * 0.78f, y - (float)Math.Cos(angle) * radius * 0.78f, format);
            }
        }
        if (!hands) return;
        var now = DateTime.Now;
        Draw((now.Hour % 12 + now.Minute / 60.0) * Math.PI / 6, (float)clock.HourLength);
        hand.Width = clock.MinuteWidth; Draw((now.Minute + now.Second / 60.0) * Math.PI / 30, (float)clock.MinuteLength);
        if (clock.SecondHandVisible) { hand.Width = clock.SecondWidth; Draw(now.Second * Math.PI / 30, (float)clock.SecondLength); }
        void Draw(double angle, float length) => graphics.DrawLine(hand, x, y, x + (float)Math.Sin(angle) * radius * length, y - (float)Math.Cos(angle) * radius * length);
    }

    private Font Font(string family, int size, bool bold)
    {
        var key = (family, size, bold);
        if (_fonts.TryGetValue(key, out var value)) return value;
        var imported = _privateFamilies.FirstOrDefault(item => item.Name == family);
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        if (imported is not null)
        {
            if (!imported.IsStyleAvailable(style)) style = imported.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
            value = new Font(imported, size, style, GraphicsUnit.Pixel);
        }
        else value = new Font(family, size, style, GraphicsUnit.Pixel);
        _fonts.Add(key, value); return value;
    }

    private void Load(string path, ScreenOptions options)
    {
        var mediaOptions = (options.VideoDecoder, options.VideoAdapterId, options.DecodeWidth, options.DecodeHeight, options.PreserveGifDelays, options.GifMinimumDelayMs, options.GifMissingDelayMs);
        if (_path == path && _mediaOptions == mediaOptions) return;
        Image? replacement = null;
        VideoFrameReader? video = null;
        int frames = 0;
        int[] delays = [];
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (MediaFile.IsVideo(path)) video = new VideoFrameReader(path, options, _deviceOutput);
            else
            {
            try { replacement = Image.FromFile(path); }
            catch (OutOfMemoryException error) { throw new InvalidDataException("图片或 GIF 无法解码，请选择有效且尺寸较小的文件。", error); }
            try
            {
                frames = replacement.FrameDimensionsList.Contains(FrameDimension.Time.Guid) ? replacement.GetFrameCount(FrameDimension.Time) : 1;
                delays = Enumerable.Repeat(options.GifMissingDelayMs, frames).ToArray();
                if (frames > 1 && replacement.PropertyIdList.Contains(0x5100))
                {
                    byte[] values = replacement.GetPropertyItem(0x5100)!.Value!;
                    for (int i = 0; i < frames && i * 4 + 4 <= values.Length; i++)
                    {
                        int delay = checked(BitConverter.ToInt32(values, i * 4) * 10);
                        delays[i] = delay <= 0 ? options.GifMissingDelayMs : Math.Max(options.PreserveGifDelays ? 1 : options.GifMinimumDelayMs, delay);
                    }
                }
                if (frames > 1 && replacement.RawFormat.Guid == ImageFormat.Gif.Guid)
                {
                    // Drawing needs the native frames, not Image.FromFile's animation re-save byte cache.
                    var loaded = replacement;
                    replacement = (Image)loaded.Clone();
                    loaded.Dispose();
                }
            }
            catch { replacement.Dispose(); throw; }
            }
        }
        _media?.Dispose();
        _video?.Dispose();
        _media = replacement; _video = video; _path = path; _frameCount = frames; _delays = delays; _frame = 0; _nextFrame = 0;
        _mediaOptions = mediaOptions;
        _renderedVideoSequence = -1;
    }

    public void Dispose() { _media?.Dispose(); _video?.Dispose(); ClearFonts(); _numericFormat.Dispose(); foreach (var family in _privateFamilies) family.Dispose(); _privateFonts.Dispose(); }
}
