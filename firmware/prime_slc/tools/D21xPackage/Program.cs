using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 5) throw new ArgumentException("Expected stock package, ELF, toolchain, output package and report paths.");
byte[] stock = File.ReadAllBytes(args[0]);
if (Convert.ToHexString(SHA256.HashData(stock)) != "E5D2826D076F7F0490BA7745C01CD655CBA38BC88391C8755DF254F7A913E386")
    throw new InvalidDataException("Expected the matched white D21x 1.0.4 package.");
int firstLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(stock.AsSpan(14)));
int secondLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(stock.AsSpan(18)));
if (stock.Length != 22 + firstLength + secondLength) throw new InvalidDataException("Stock package length differs.");
var symbols = RunTool("nm", ["--extern-only", "--defined-only", "--format=posix", args[1]]).Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
    .Where(parts => parts.Length >= 3)
    .ToDictionary(parts => parts[0], parts => uint.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
uint extensionStart = symbols["__extension_start"], extensionEnd = symbols["__extension_end"];
if (extensionStart != 0x40238cc8 || symbols["bootstrap"] != extensionStart ||
    extensionEnd <= extensionStart || extensionEnd >= 0x40300000 || (extensionEnd & 7) != 0)
    throw new InvalidDataException("Extension does not fit the verified native memory map.");
string outputPath = Path.GetFullPath(args[3]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
string payloadPath = Path.ChangeExtension(outputPath, ".payload.bin");
RunTool("objcopy", ["-O", "binary", "--gap-fill", "0", "--pad-to", $"0x{extensionEnd:x}", args[1], payloadPath]);
byte[] payload = File.ReadAllBytes(payloadPath);
if (payload.Length != extensionEnd - extensionStart || BinaryPrimitives.ReadUInt32LittleEndian(payload) != 0x30401073)
    throw new InvalidDataException("Extension payload or interrupt masking differs.");

List<ArchiveEntry> entries = ReadArchive(stock.AsSpan(22, firstLength));
ArchiveEntry info = entries.Single(entry => entry.Name == "ota_info.bin");
ArchiveEntry image = entries.Single(entry => entry.Name == "d21x_os.itb");
if (info.Data.Length != 512 || image.Data.Length != 0x100000 ||
    BinaryPrimitives.ReadUInt32BigEndian(image.Data) != 0xd00dfeed || ReadBe(image.Data, 4) != 0x800)
    throw new InvalidDataException("Stock CPIO/FIT layout differs.");
var properties = FitProperties(image.Data);
int oldSize = ReadBe(image.Data, properties["/images/seg0/data-size"]);
if (oldSize != 0xff058 || properties.ContainsKey("/images/seg1/data-size") ||
    Convert.ToHexString(SHA256.HashData(image.Data.AsSpan(0x800, oldSize))) !=
    "F1D8C3EB03ACAC018EE68A39BC5CDD8A4746B855E195346706B48600D1C89437")
    throw new InvalidDataException("Stock D21x executable differs.");
int newSize = checked(oldSize + payload.Length);
int fitLength = Math.Max(image.Data.Length, Align(checked(0x800 + newSize), 4096));
if (fitLength > 0x200000) throw new InvalidDataException("Firmware exceeds the existing OS partition.");
byte[] fit = new byte[fitLength];
image.Data.CopyTo(fit, 0);
var program = fit.AsSpan(0x800, newSize);
RequireBytes(program, 4, "73104030");
RequireBytes(program, 0x32bf0, "1171A2F9A6F586FD");
RequireBytes(program, 0x5352a, "17551E001305E579");
RequireBytes(program, 0x33950, "B7575A0D19719387A75A3EE0");
RequireBytes(program, 0x33b04, "EFE0DDD917B71F00032787D3");
RequireBytes(program, 0x2fa80, "1D7186ECA2E8A6E4");
RequireBytes(program, 0xa5680, "011106EC22E826E4");
RequireBytes(program, 0xa5150, "797126EC4AE84EE4");
RequireBytes(program, 0x33830, "130101DE814517450A001305A5FC");
RequireBytes(program, 0x9a808, "8B1A340783B88A00");
RequireBytes(program, 0xd7f18, "02D0312E302E3400");
WriteLe(program, 4, Jump(0x40000004, 0x400ff058));
WriteAddress(program, 0x32bf0, symbols["extension_usb"], 5, true);
WriteAddress(program, 0x5352a, extensionEnd, 10, false);
WriteAddress(program, 0x33950, symbols["extension_file_thread"], 5, true);
WriteLe(program, 0x33958, 0x00000013);
WriteAddress(program, 0x33b04, symbols["extension_file_timer"], 5, true);
WriteLe(program, 0x33b0c, 0x00000013);
WriteAddress(program, 0x2fa80, symbols["extension_flush"], 5, true);
WriteAddress(program, 0xa5680, symbols["extension_video_render"], 5, true);
WriteAddress(program, 0xa5150, symbols["extension_stream_open"], 5, true);
WriteAddress(program, 0x33830, symbols["extension_default_start"], 5, true);
WriteLe(program, 0x33838, 0x00000013);
BinaryPrimitives.WriteUInt16LittleEndian(program[0x3383c..], 0x0001);
WriteAddress(program, 0x9a808, symbols["extension_default_round"], 10, true);
program[0xd7f1f] = (byte)'H';
payload.CopyTo(program[oldSize..]);
BinaryPrimitives.WriteUInt32BigEndian(fit.AsSpan(properties["/images/seg0/data-size"]), (uint)newSize);
foreach (var property in properties.Where(pair => pair.Key.StartsWith("/images/seg0/hash", StringComparison.Ordinal) && pair.Key.EndsWith("/algo", StringComparison.Ordinal)))
{
    int value = properties[property.Key[..^4] + "value"];
    switch (ReadString(fit, property.Value))
    {
    case "crc32": BinaryPrimitives.WriteUInt32BigEndian(fit.AsSpan(value), Crc(program, uint.MaxValue) ^ uint.MaxValue); break;
    case "md5": MD5.HashData(program).CopyTo(fit, value); break;
    default: throw new InvalidDataException("Unsupported stock FIT hash.");
    }
}
image.Data = fit;
int archiveLength = ArchiveLength(entries);
int infoEnd = Array.IndexOf(info.Data, (byte)0xff, 4);
if (infoEnd < 4) throw new InvalidDataException("Stock OTA metadata padding differs.");
string infoText = Encoding.ASCII.GetString(info.Data, 4, infoEnd - 4);
string oldDeclaration = "size = \"1049600\";";
if (infoText.IndexOf(oldDeclaration, StringComparison.Ordinal) < 0) throw new InvalidDataException("Stock OTA size declaration differs.");
infoText = infoText.Replace(oldDeclaration, $"size = \"{archiveLength}\";", StringComparison.Ordinal);
byte[] infoBytes = Enumerable.Repeat((byte)0xff, 512).ToArray();
info.Data.AsSpan(0, 4).CopyTo(infoBytes);
byte[] textBytes = Encoding.ASCII.GetBytes(infoText);
if (textBytes.Length > 508) throw new InvalidDataException("OTA metadata exceeds its existing size.");
textBytes.CopyTo(infoBytes, 4);
info.Data = infoBytes;
byte[] archive = WriteArchive(entries, archiveLength);
byte[] package = new byte[checked(22 + archive.Length + secondLength)];
stock.AsSpan(0, 22).CopyTo(package);
BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(14), (uint)archive.Length);
archive.CopyTo(package, 22);
stock.AsSpan(22 + firstLength, secondLength).CopyTo(package.AsSpan(22 + archive.Length));
BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(10), Crc(package.AsSpan(14), 0));
if (!package.AsSpan(22 + archive.Length).SequenceEqual(stock.AsSpan(22 + firstLength)))
    throw new InvalidDataException("Unselected platform changed.");
File.WriteAllBytes(outputPath, package);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[4]))!);
File.WriteAllText(args[4], JsonSerializer.Serialize(new {
    PackageSha256 = Convert.ToHexString(SHA256.HashData(package)), ExtensionLength = payload.Length,
    ExtensionStart = extensionStart.ToString("X8"), HeapStart = extensionEnd.ToString("X8"),
    ProgramLength = newSize, FitLength = fit.Length, FirstPlatformLength = archive.Length,
    SecondPlatformChanged = false, DeviceBootVerified = false
}, new JsonSerializerOptions { WriteIndented = true }));

string RunTool(string name, string[] arguments)
{
    var start = new ProcessStartInfo(Path.Combine(args[2], "bin", $"riscv64-unknown-elf-{name}.exe"))
    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("Firmware tool did not start.");
    string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException($"{name} failed: {error}");
    return output;
}
static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);
static uint Crc(ReadOnlySpan<byte> bytes, uint seed)
{
    uint crc = seed;
    foreach (byte value in bytes) {
        crc ^= value;
        for (int i = 0; i < 8; ++i) crc = crc >> 1 ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
    }
    return crc;
}
static void RequireBytes(ReadOnlySpan<byte> bytes, int offset, string expected)
{
    if (!bytes.Slice(offset, expected.Length / 2).SequenceEqual(Convert.FromHexString(expected)))
        throw new InvalidDataException($"Original instruction differs at {offset:x}.");
}
static void WriteLe(Span<byte> bytes, int offset, uint instruction) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], instruction);
static void WriteAddress(Span<byte> bytes, int offset, uint target, int register, bool jump)
{
    uint pc = checked(0x40000000u + (uint)offset);
    int delta = checked((int)((long)target - pc)), upper = (delta + 0x800) >> 12, lower = delta - (upper << 12);
    WriteLe(bytes, offset, unchecked((uint)(upper << 12)) | (uint)(register << 7) | 0x17);
    WriteLe(bytes, offset + 4, (uint)(lower & 0xfff) << 20 | (uint)(register << 15) |
        (jump ? 0u : (uint)(register << 7)) | (jump ? 0x67u : 0x13u));
}
static uint Jump(uint pc, uint target)
{
    int delta = checked((int)((long)target - pc));
    if ((delta & 1) != 0 || delta < -0x100000 || delta >= 0x100000) throw new InvalidDataException("Startup jump is out of range.");
    uint bits = unchecked((uint)delta);
    return (bits >> 20 & 1) << 31 | (bits >> 1 & 0x3ff) << 21 | (bits >> 11 & 1) << 20 | (bits & 0xff000) | 0x6f;
}
static int ReadBe(ReadOnlySpan<byte> bytes, int offset) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]));
static string ReadString(byte[] bytes, int offset) => Encoding.ASCII.GetString(bytes, offset, Array.IndexOf(bytes, (byte)0, offset) - offset);
static Dictionary<string, int> FitProperties(byte[] bytes)
{
    int offset = ReadBe(bytes, 8), names = ReadBe(bytes, 12);
    var stack = new List<string>(); var properties = new Dictionary<string, int>();
    while (true) {
        int token = ReadBe(bytes, offset); offset += 4;
        switch (token) {
        case 1: string name = ReadString(bytes, offset); stack.Add(name); offset = Align(offset + name.Length + 1, 4); break;
        case 2: stack.RemoveAt(stack.Count - 1); break;
        case 3:
            int length = ReadBe(bytes, offset), nameOffset = ReadBe(bytes, offset + 4); offset += 8;
            properties.Add(string.Join('/', stack) + "/" + ReadString(bytes, names + nameOffset), offset);
            offset = Align(offset + length, 4); break;
        case 4: break;
        case 9: return properties;
        default: throw new InvalidDataException("Unexpected FDT token.");
        }
    }
}
static List<ArchiveEntry> ReadArchive(ReadOnlySpan<byte> bytes)
{
    var entries = new List<ArchiveEntry>(); int offset = 0;
    while (true) {
        byte[] header = bytes.Slice(offset, 110).ToArray();
        if (Encoding.ASCII.GetString(header, 0, 6) != "070702") throw new InvalidDataException("Expected CRC CPIO.");
        int size = int.Parse(Encoding.ASCII.GetString(header, 54, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int nameSize = int.Parse(Encoding.ASCII.GetString(header, 94, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte[] name = bytes.Slice(offset + 110, nameSize).ToArray();
        int dataOffset = Align(offset + 110 + nameSize, 4);
        var entry = new ArchiveEntry(header, name, bytes.Slice(dataOffset, size).ToArray());
        entries.Add(entry);
        if (entry.Name == "TRAILER!!!") return entries;
        offset = Align(dataOffset + size, 4);
    }
}
static int ArchiveLength(List<ArchiveEntry> entries)
{
    int size = 0;
    foreach (var entry in entries) size = Align(checked(Align(size + 110 + entry.NameBytes.Length, 4) + entry.Data.Length), 4);
    return Align(size, 512);
}
static byte[] WriteArchive(List<ArchiveEntry> entries, int length)
{
    byte[] result = new byte[length]; int offset = 0;
    foreach (var entry in entries) {
        byte[] header = (byte[])entry.Header.Clone();
        uint sum = 0; foreach (byte value in entry.Data) sum += value;
        Encoding.ASCII.GetBytes(entry.Data.Length.ToString("x8", CultureInfo.InvariantCulture)).CopyTo(header, 54);
        Encoding.ASCII.GetBytes(sum.ToString("x8", CultureInfo.InvariantCulture)).CopyTo(header, 102);
        header.CopyTo(result, offset); entry.NameBytes.CopyTo(result, offset + 110);
        offset = Align(offset + 110 + entry.NameBytes.Length, 4);
        entry.Data.CopyTo(result, offset); offset = Align(offset + entry.Data.Length, 4);
    }
    return result;
}
sealed class ArchiveEntry(byte[] header, byte[] nameBytes, byte[] data)
{
    public byte[] Header { get; } = header;
    public byte[] NameBytes { get; } = nameBytes;
    public string Name { get; } = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
    public byte[] Data { get; set; } = data;
}
