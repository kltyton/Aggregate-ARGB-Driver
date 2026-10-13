using System.Runtime.InteropServices;

namespace HardwareLite.Runtime;

internal sealed class InstanceWindow : NativeWindow, IDisposable
{
    private const string Name = "HardwareLite.SettingsWindow";
    private static readonly uint ShowMessage = RegisterWindowMessage(Name);
    private static readonly uint ExitMessage = RegisterWindowMessage("HardwareLite.Exit");
    private readonly Action _show;
    private readonly Action _exit;
    public InstanceWindow(Action show, Action exit) { _show = show; _exit = exit; CreateHandle(new CreateParams { Caption = Name }); }
    public static void ShowExisting()
    {
        IntPtr window = FindWindow(null, Name);
        if (window != IntPtr.Zero) PostMessage(window, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }
    public static void ExitExisting()
    {
        IntPtr window = FindWindow(null, Name);
        if (window != IntPtr.Zero) PostMessage(window, ExitMessage, IntPtr.Zero, IntPtr.Zero);
    }
    protected override void WndProc(ref Message message)
    { if ((uint)message.Msg == ShowMessage) _show(); else if ((uint)message.Msg == ExitMessage) _exit(); else base.WndProc(ref message); }
    public void Dispose() => DestroyHandle();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
