using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareLite.Configuration;
using HardwareLite.Monitoring;

namespace HardwareLite.Display;

internal static class DeviceUiPreparation
{
    internal static byte[] ConfigurationKey(ScreenSettings screen) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { screen.Mode, screen.Rotation, screen.Layout, screen.FontFiles }));
    private sealed record Face(string Family, int Size, bool Bold);
    private sealed record Glyph(uint Code, float Advance, short X, short Y, ushort Width, ushort Height, byte[] Alpha);

    public static Settings Prepare(Settings settings, SensorSnapshot snapshot, string directory, CancellationToken cancellation)
    {
        var screen = settings.Screen;
        if (!screen.Enabled || !screen.Options.DevicePlayback || screen.Mode == ScreenMode.Media)
            return settings with { Screen = screen with { DeviceUiPath = "" } };
        using var canvas = new ScreenCanvas();
        _ = canvas.PreparedFont(screen, "Segoe UI", 8, false);
        using var measureBitmap = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(measureBitmap);
        measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        using var ordinary = new StringFormat { FormatFlags = StringFormatFlags.MeasureTrailingSpaces };
        var faces = new List<Face>();
        var ids = new (int Value, int Unit)[screen.Layout.Elements.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            var item = screen.Layout.Elements[i];
            ids[i] = item.Metric is ScreenMetric.Text or ScreenMetric.AnalogClock ? (-1, -1)
                : (FaceId(new(item.FontFamily, item.FontSize, item.Bold)),
                   item.UnitFontSize > 0 ? FaceId(new(item.UnitFontFamily.Length > 0 ? item.UnitFontFamily : item.FontFamily, item.UnitFontSize, false)) : -1);
        }
        int FaceId(Face face) { int id = faces.IndexOf(face); if (id < 0) { id = faces.Count; faces.Add(face); } return id; }

        var characters = new SortedSet<uint>();
        for (uint code = 32; code <= 126; code++) characters.Add(code);
        Add("°…−");
        foreach (var item in screen.Layout.Elements) { Add(item.Text); Add(item.Format); Add(item.NumericFormat); Add(item.CustomUnit); }
        foreach (var reading in snapshot.Sensors.Values) Add(reading.Unit);
        var dates = CultureInfo.CurrentCulture.DateTimeFormat;
        foreach (var word in dates.MonthNames.Concat(dates.AbbreviatedMonthNames).Concat(dates.MonthGenitiveNames)
                     .Concat(dates.AbbreviatedMonthGenitiveNames).Concat(dates.DayNames).Concat(dates.AbbreviatedDayNames))
            Add(word);
        Add(dates.AMDesignator + dates.PMDesignator + dates.DateSeparator + dates.TimeSeparator);
        foreach (int era in dates.Calendar.Eras) { Add(dates.GetEraName(era)); Add(dates.GetAbbreviatedEraName(era)); }
        var numbers = CultureInfo.CurrentCulture.NumberFormat;
        Add(numbers.NumberDecimalSeparator + numbers.NumberGroupSeparator + numbers.NegativeSign + numbers.PositiveSign
            + numbers.CurrencySymbol + numbers.PercentSymbol + numbers.NaNSymbol + numbers.PositiveInfinitySymbol + numbers.NegativeInfinitySymbol);
        void Add(string text) { foreach (var rune in text.EnumerateRunes()) if (rune.Value is not ('\r' or '\n')) characters.Add((uint)rune.Value); }

        using var content = new MemoryStream();
        using var writer = new BinaryWriter(content, Encoding.UTF8, true);
        writer.Write(0x49554c48u); // HLUI
        writer.Write(2u);
        writer.Write((uint)screen.Layout.Elements.Length);
        writer.Write((uint)faces.Count);
        writer.Write((uint)screen.Rotation);
        uint background = screen.Mode == ScreenMode.Monitor ? unchecked((uint)ColorTranslator.FromHtml(screen.Layout.BackgroundColor).ToArgb())
            : (uint)(screen.Layout.BackgroundShade * 255 / 100) << 24;
        writer.Write(background);
        writer.Write(ConfigurationKey(screen));
        foreach (var face in faces)
        {
            cancellation.ThrowIfCancellationRequested();
            var font = canvas.PreparedFont(screen, face.Family, face.Size, face.Bold);
            writer.Write((uint)characters.Count);
            writer.Write(Ascent(font));
            writer.Write(font.GetHeight(measure));
            var glyphs = new List<Glyph>();
            foreach (uint code in characters)
            {
                cancellation.ThrowIfCancellationRequested();
                var glyph = Rasterize(font, new Rune((int)code).ToString(), measure, format);
                glyphs.Add(glyph with { Code = code });
                writer.Write(code); writer.Write(glyph.Advance);
                writer.Write(CharacterAdvance(measure, font, new Rune((int)code).ToString(), ordinary));
                writer.Write(glyph.X); writer.Write(glyph.Y); writer.Write(glyph.Width); writer.Write(glyph.Height);
                writer.Write(glyph.Alpha);
            }
            var pairs = new List<(uint Left, uint Right, float Adjustment, float OrdinaryAdjustment)>();
            foreach (var left in glyphs)
            foreach (var right in glyphs)
            {
                string pair = new Rune((int)left.Code).ToString() + new Rune((int)right.Code);
                float adjustment = measure.MeasureString(pair, font, PointF.Empty, format).Width - left.Advance - right.Advance;
                string first = new Rune((int)left.Code).ToString();
                float ordinaryAdjustment = CharacterAdvance(measure, font, pair, ordinary, first.Length)
                    - CharacterAdvance(measure, font, first, ordinary);
                if (Math.Abs(adjustment) > 0.01f || Math.Abs(ordinaryAdjustment) > 0.01f)
                    pairs.Add((left.Code, right.Code, adjustment, ordinaryAdjustment));
            }
            writer.Write((uint)pairs.Count);
            foreach (var pair in pairs) { writer.Write(pair.Left); writer.Write(pair.Right); writer.Write(pair.Adjustment); writer.Write(pair.OrdinaryAdjustment); }
        }
        for (int i = 0; i < screen.Layout.Elements.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var item = screen.Layout.Elements[i];
            writer.Write((uint)item.Metric); writer.Write(item.X); writer.Write(item.Y); writer.Write(item.Width); writer.Write(item.Height);
            writer.Write((uint)item.Alignment); writer.Write(ids[i].Value); writer.Write(ids[i].Unit);
            int titleHeight = item.Title.Length > 0 ? item.TitleFontSize > 0 ? item.TitleFontSize : Math.Max(16, item.FontSize / 2) : 0;
            writer.Write((item.Caption.Length > 0 ? item.CaptionHeight : 0) + titleHeight + (titleHeight > 0 ? 8 : 0));
            writer.Write(unchecked((uint)ColorTranslator.FromHtml(item.Color).ToArgb()));
            writer.Write(item.FontSize / 6f);
            var clock = item.Clock;
            writer.Write(clock.SecondHandVisible ? 1u : 0u);
            writer.Write((float)clock.HourLength); writer.Write((float)clock.MinuteLength); writer.Write((float)clock.SecondLength);
            writer.Write((float)clock.HourWidth); writer.Write((float)clock.MinuteWidth); writer.Write((float)clock.SecondWidth);
            using var bitmap = new Bitmap(item.Width, item.Height, PixelFormat.Format32bppArgb);
            canvas.RenderPreparedElement(bitmap, item);
            var pixels = Pixels(bitmap);
            long countPosition = content.Position; writer.Write(0u);
            uint runs = 0;
            for (int index = 0; index < pixels.Length;)
            {
                uint color = pixels[index]; int end = index + 1;
                while (end < pixels.Length && pixels[end] == color) end++;
                writer.Write((uint)(end - index)); writer.Write(color); runs++; index = end;
            }
            long endPosition = content.Position; content.Position = countPosition; writer.Write(runs); content.Position = endPosition;
        }
        for (int index = 0; index < 3600; index++)
        {
            double angle = index * Math.PI / 1800;
            writer.Write((float)Math.Sin(angle)); writer.Write((float)Math.Cos(angle));
        }
        writer.Flush();
        if (content.Length > 8 * 1024 * 1024) throw new InvalidDataException("当前字体和布局的设备字形包超过 8 MiB，请调整字体或使用主机发送模式。");
        byte[] package = content.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(package));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, hash + ".hlui");
        if (!File.Exists(path)) File.WriteAllBytes(path, package);
        return settings with { Screen = screen with { DeviceUiPath = path } };
    }

    private static float Ascent(Font font) => font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);
    private static float CharacterAdvance(Graphics graphics, Font font, string text, StringFormat format, int firstLength = 0)
    {
        int length = firstLength == 0 ? text.Length : firstLength;
        format.SetMeasurableCharacterRanges(firstLength == 0 ? [new CharacterRange(0, length)]
            : [new CharacterRange(0, length), new CharacterRange(length, text.Length - length)]);
        var regions = graphics.MeasureCharacterRanges(text, font, new RectangleF(0, 0, 16384, 4096), format);
        try
        {
            var first = regions[0].GetBounds(graphics);
            return firstLength == 0 ? first.Width : regions[1].GetBounds(graphics).Left - first.Left;
        }
        finally { foreach (var region in regions) region.Dispose(); }
    }
    private static Glyph Rasterize(Font font, string text, Graphics measure, StringFormat format)
    {
        int pad = Math.Max(8, (int)font.Size / 4), width = Math.Max(1, (int)Math.Ceiling(measure.MeasureString(text, font, PointF.Empty, format).Width));
        int height = (int)Math.Ceiling(font.GetHeight(measure));
        using var bitmap = new Bitmap(width + pad * 2, height + pad * 2, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(text, font, Brushes.White, pad, pad, format);
        }
        uint[] pixels = Pixels(bitmap);
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            if ((pixels[y * bitmap.Width + x] >> 24) != 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        float advance = measure.MeasureString(text, font, PointF.Empty, format).Width;
        if (right < 0) return new(0, advance, 0, 0, 0, 0, []);
        ushort inkWidth = checked((ushort)(right - left + 1)), inkHeight = checked((ushort)(bottom - top + 1));
        byte[] alpha = new byte[inkWidth * inkHeight];
        for (int y = 0; y < inkHeight; y++)
        for (int x = 0; x < inkWidth; x++) alpha[y * inkWidth + x] = (byte)(pixels[(top + y) * bitmap.Width + left + x] >> 24);
        return new(0, advance, checked((short)(left - pad)), checked((short)(top - pad)), inkWidth, inkHeight, alpha);
    }
    private static uint[] Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int[] pixels = new int[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width);
            return Array.ConvertAll(pixels, value => unchecked((uint)value));
        }
        finally { bitmap.UnlockBits(data); }
    }
}
