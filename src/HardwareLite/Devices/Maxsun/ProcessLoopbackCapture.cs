using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace HardwareLite.Devices.Maxsun;

internal sealed class ProcessLoopbackCapture : IWaveIn
{
    private readonly AudioClient _client;
    private readonly Process _target;
    private readonly AutoResetEvent _ready = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    public WaveFormat WaveFormat { get; set; }
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public ProcessLoopbackCapture(string application, bool exclude, int sampleRate)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            throw new InvalidOperationException("应用音频采集需要 Windows 内部版本 20348 或更新版本。");
        int pid = 0;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(application)))
        {
            using (process) { if (pid == 0 || process.Id < pid) pid = process.Id; }
        }
        if (pid == 0) throw new IOException("等待应用启动：" + application);
        _target = Process.GetProcessById(pid);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        nint parameters = Marshal.AllocCoTaskMem(12);
        try
        {
            Marshal.WriteInt32(parameters, 0, 1);
            Marshal.WriteInt32(parameters, 4, pid);
            Marshal.WriteInt32(parameters, 8, exclude ? 1 : 0);
            var variant = new ActivationBlob { Type = 65, Size = 12, Data = parameters };
            var completion = new Completion();
            Guid iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", ref iid, ref variant, completion, out var operation));
            try { _client = new AudioClient((IAudioClient)completion.Result.Task.GetAwaiter().GetResult()); }
            finally { Marshal.ReleaseComObject(operation); GC.KeepAlive(completion); }
        }
        catch { _target.Dispose(); throw; }
        finally { Marshal.FreeCoTaskMem(parameters); }
    }
    public void StartRecording()
    {
        if (_thread is not null) throw new InvalidOperationException("应用音频采集已启动。");
        _client.Initialize(AudioClientShareMode.Shared,
            AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm,
            0, 0, WaveFormat, Guid.Empty);
        _client.SetEventHandle(_ready.SafeWaitHandle.DangerousGetHandle());
        _stop.Reset();
        _thread = new Thread(Capture) { IsBackground = true, Name = "Application audio capture" };
        _thread.Start();
    }
    private void Capture()
    {
        Exception? failure = null;
        bool started = false;
        try
        {
            var capture = _client.AudioCaptureClient;
            byte[] buffer = new byte[_client.BufferSize * WaveFormat.BlockAlign];
            _client.Start(); started = true;
            WaitHandle[] events = [_stop, _ready];
            while (true)
            {
                int signal = WaitHandle.WaitAny(events, 1000);
                if (signal == 0) break;
                if (_target.HasExited) throw new IOException("目标音频应用已退出。");
                if (signal == WaitHandle.WaitTimeout) continue;
                while (capture.GetNextPacketSize() > 0)
                {
                    nint data = capture.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                    try
                    {
                        int bytes = checked(frames * WaveFormat.BlockAlign);
                        if (buffer.Length < bytes) Array.Resize(ref buffer, bytes);
                        if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, bytes);
                        else Marshal.Copy(data, buffer, 0, bytes);
                        DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                    }
                    finally { capture.ReleaseBuffer(frames); }
                }
            }
        }
        catch (Exception error) when (error is COMException or IOException or InvalidOperationException or ArgumentException)
        { failure = error; }
        finally
        {
            if (started) { try { _client.Stop(); } catch (COMException error) { failure ??= error; } }
            RecordingStopped?.Invoke(this, new StoppedEventArgs(failure));
        }
    }
    public void StopRecording() { _stop.Set(); _thread?.Join(); }
    public void Dispose() { StopRecording(); _client.Dispose(); _target.Dispose(); _ready.Dispose(); _stop.Dispose(); }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct ActivationBlob
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint Size;
        [FieldOffset(16)] public nint Data;
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly TaskCompletionSource<object> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try { operation.GetActivateResult(out int result, out object client); Marshal.ThrowExceptionForHR(result); Result.TrySetResult(client); }
            catch (COMException error) { Result.TrySetException(error); }
        }
    }
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string device, ref Guid iid, ref ActivationBlob parameters,
        IActivateAudioInterfaceCompletionHandler completion, out IActivateAudioInterfaceAsyncOperation operation);
}
