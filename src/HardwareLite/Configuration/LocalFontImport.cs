using System.Buffers.Binary;
using System.Text;

namespace HardwareLite.Configuration;

internal static class LocalFontImport
{
    public static string[] Import(string executable, string directory)
    {
        byte[] data = File.ReadAllBytes(executable);
        var files = new List<string>();
        for (int start = 0; start <= data.Length - 12; start++)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start, 4)) != 0x00010000) continue;
            int tables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(start + 4, 2));
            if (tables is < 8 or > 40 || start + 12 + tables * 16 > data.Length) continue;
            int length = 0, names = 0, namesLength = 0;
            bool head = false, cmap = false, valid = true;
            for (int table = 0; table < tables; table++)
            {
                int entry = start + 12 + table * 16;
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 8, 4));
                uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 12, 4));
                if ((ulong)offset + size > (ulong)(data.Length - start)) { valid = false; break; }
                string tag = Encoding.ASCII.GetString(data, entry, 4);
                length = Math.Max(length, (int)(offset + size));
                if (tag == "head") head = true;
                if (tag == "cmap") cmap = true;
                if (tag == "name") { names = start + (int)offset; namesLength = (int)size; }
            }
            if (!valid || !head || !cmap || namesLength < 6) continue;
            int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(names + 2, 2));
            int strings = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(names + 4, 2));
            if (6 + count * 12 > namesLength) continue;
            string? name = null;
            for (int record = 0; record < count; record++)
            {
                int entry = names + 6 + record * 12;
                if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 6, 2)) != 6) continue;
                int platform = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry, 2));
                int size = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 8, 2));
                int offset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 10, 2));
                if (strings + offset + size > namesLength) continue;
                name = (platform is 0 or 3 ? Encoding.BigEndianUnicode : Encoding.Latin1).GetString(data, names + strings + offset, size);
                if (name is "DINPro-CondBold" or "KairosSansW1G-CnBold" or "KairosSansW1G-CnMedium") break;
            }
            if (name is not ("DINPro-CondBold" or "KairosSansW1G-CnBold" or "KairosSansW1G-CnMedium")) continue;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".ttf");
            if (!File.Exists(path)) File.WriteAllBytes(path, data.AsSpan(start, length).ToArray());
            files.Add(path);
            start += length - 1;
        }
        if (files.Count != 3) throw new InvalidDataException("未能读取原厂屏幕使用的三个字体。");
        return files.ToArray();
    }
}
