using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HardwareLite.Devices.Maxsun;

internal sealed class MouseWheelActivity : IDisposable
{
    private readonly Thread _thread;
    private readonly Hook _callback;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _threadId;
    private long _sequence;
    public long Sequence => Interlocked.Read(ref _sequence);
    public MouseWheelActivity()
    {
        _callback = Observe;
        _thread = new Thread(Pump) { IsBackground = true, Name = "Mouse wheel observer" };
        _thread.Start(); _started.Task.GetAwaiter().GetResult();
    }
    private void Pump()
    {
        nint hook = 0;
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, 0, 0, 0, 0);
            hook = SetWindowsHookEx(14, _callback, GetModuleHandle(null), 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _started.SetResult();
            while (GetMessage(out var message, 0, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
        catch (Win32Exception error) { _started.TrySetException(error); }
        finally { if (hook != 0) UnhookWindowsHookEx(hook); }
    }
    private nint Observe(int code, nint message, nint data)
    {
        if (code >= 0 && (message == 0x020A || message == 0x020E)) Interlocked.Increment(ref _sequence);
        return CallNextHookEx(0, code, message, data);
    }
    public void Dispose() { if (_thread.IsAlive) { PostThreadMessage(_threadId, 0x0012, 0, 0); _thread.Join(); } }
    private delegate nint Hook(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint minimum, uint maximum, uint flags);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
