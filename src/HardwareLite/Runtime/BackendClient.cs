using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using HardwareLite.Communication;
using HardwareLite.Monitoring;

namespace HardwareLite.Runtime;

internal sealed class BackendClient : ISensorSource
{
    private BackendState _state = BackendState.Offline;
    private readonly SemaphoreSlim _requests = new(1, 1);
    public BackendState State => Volatile.Read(ref _state);
    public SensorSnapshot Snapshot => State.Snapshot;
    public IReadOnlyList<SensorOption> Gpus => State.Gpus;
    public IReadOnlyList<SensorOption> Fans => State.Fans;
    public IReadOnlyList<SensorOption> BoardTemperatures => State.BoardTemperatures;
    public static void StartService()
    {
        if (Mutex.TryOpenExisting(@"Local\" + BackendProtocol.PipeName, out var running))
        {
            running.Dispose();
            return;
        }
        string path = Path.Combine(AppContext.BaseDirectory, "HardwareLite.Service.exe");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
    }
    public async Task<BackendState> SendAsync(BackendRequest request, CancellationToken cancellation = default, int connectTimeoutMs = 1000)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(10000, connectTimeoutMs)));
        await _requests.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            using var pipe = new NamedPipeClientStream(".", BackendProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(connectTimeoutMs, deadline.Token).ConfigureAwait(false);
            using var identity = WindowsIdentity.GetCurrent();
            if (!identity.User!.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
                throw new UnauthorizedAccessException("后台管道不属于当前用户。");
            await BackendProtocol.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
            var state = await BackendProtocol.ReadAsync<BackendState>(pipe, deadline.Token).ConfigureAwait(false);
            Volatile.Write(ref _state, state);
            if (state.Error.Length > 0 && request.Command != "status") throw new InvalidOperationException(state.Error);
            return state;
        }
        finally { _requests.Release(); }
    }
    public void Send(BackendRequest request) => SendAsync(request).GetAwaiter().GetResult();
    public async Task PollAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try { await SendAsync(new("status"), cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
            { Volatile.Write(ref _state, BackendState.Offline with { Error = "后台连接失败：" + error.Message }); }
            try { await Task.Delay(1000, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
        }
    }
}
