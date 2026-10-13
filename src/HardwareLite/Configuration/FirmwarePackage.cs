using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HardwareLite.Configuration;

public sealed record FirmwareUpdateStatus(bool Running, int Progress, string Message)
{
    public static FirmwareUpdateStatus Idle { get; } = new(false, 0, "未开始更新");
}

public sealed record FirmwarePackage(string FilePath, string Sha256, byte[] Application)
{
    private static readonly uint[] ChecksumTable = CreateChecksumTable();
    public int ImageVariant { get; init; } = 1;
    public string Version { get; init; } = "";
    public static FirmwarePackage Load(string path) => Load(path, 1);

    public static FirmwarePackage Load(string path, int imageVariant)
    {
        if (imageVariant is not (0 or 1)) throw new InvalidDataException("固件平台标志无效。");
        byte[] file = File.ReadAllBytes(path);
        if (file.Length < 30 || file[0] != 0xBA || file[1] != 0xAB || file[2] != 'P' || file[3] != 'S' || file[4] != 'W')
            throw new InvalidDataException("请选择 PRIME SLC II LCD 的原厂固件升级包；不能使用其他机型或未封装的 BIN。");
        ReadOnlySpan<byte> payload = file.AsSpan(14);
        if (Checksum(payload) != BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10, 4)))
            throw new InvalidDataException("固件校验和不匹配，文件可能损坏。");
        uint bootLength = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        uint applicationLength = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        if (applicationLength < 8 || 8UL + bootLength + applicationLength != (ulong)payload.Length)
            throw new InvalidDataException("固件内容长度无效。");
        uint selectedLength = imageVariant == 0 ? bootLength : applicationLength;
        if (selectedLength < 8) throw new InvalidDataException("所选平台的固件映像为空或不完整。");
        int selectedOffset = imageVariant == 0 ? 8 : checked((int)(8 + bootLength));
        byte[] application = payload.Slice(selectedOffset, checked((int)selectedLength)).ToArray();
        return new(Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(file)), application)
        { ImageVariant = imageVariant, Version = Encoding.ASCII.GetString(file, 5, 5) };
    }

    public static uint Checksum(ReadOnlySpan<byte> bytes, uint crc = 0)
    {
        foreach (byte value in bytes)
            crc = crc >> 8 ^ ChecksumTable[(crc ^ value) & 255];
        return crc;
    }

    private static uint[] CreateChecksumTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; ++index)
        {
            uint crc = index;
            for (int bit = 0; bit < 8; ++bit) crc = crc >> 1 ^ ((crc & 1) == 0 ? 0 : 0xEDB88320);
            table[index] = crc;
        }
        return table;
    }
}
