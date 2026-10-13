using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HardwareLite.Runtime;

internal sealed class DeviceConflicts
{
    private static readonly string[] Names = ["MaxsunSync2", "MaxsunSyncService", "CH270", "CH270.Service", "ASUS_InfoHub"];
    private readonly byte[] _entry = new byte[568];
    private int _running;

    public void Refresh()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        BinaryPrimitives.WriteUInt32LittleEndian(_entry, (uint)_entry.Length);
        int running = 0;
        bool found = Process32FirstW(snapshot, _entry);
        if (!found && Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error());
        while (found)
        {
            for (int i = 0; i < Names.Length; i++) if (Matches(Names[i])) running |= 1 << i;
            found = Process32NextW(snapshot, _entry);
            if (!found && Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Volatile.Write(ref _running, running);
    }

    private bool Matches(string name)
    {
        int length = name.Length + 4;
        if (BinaryPrimitives.ReadUInt16LittleEndian(_entry.AsSpan(44 + length * 2)) != 0) return false;
        for (int i = 0; i < length; i++)
        {
            char wanted = i < name.Length ? name[i] : ".exe"[i - name.Length];
            char actual = (char)BinaryPrimitives.ReadUInt16LittleEndian(_entry.AsSpan(44 + i * 2));
            if (char.ToUpperInvariant(actual) != char.ToUpperInvariant(wanted)) return false;
        }
        return true;
    }

    public string? Find(string[] names)
    {
        int running = Volatile.Read(ref _running);
        foreach (string name in names)
        {
            int index = Array.IndexOf(Names, name);
            if (index >= 0 && (running & (1 << index)) != 0) return $"请先退出 {name}，避免同时控制设备";
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeFileHandle snapshot, [In, Out] byte[] entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeFileHandle snapshot, [In, Out] byte[] entry);
}
