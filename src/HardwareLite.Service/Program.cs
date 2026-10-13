using HardwareLite.Communication;
using HardwareLite.Service.Runtime;
using NAudio.MediaFoundation;

namespace HardwareLite.Service;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        using var singleton = new Mutex(true, @"Local\" + BackendProtocol.PipeName, out bool first);
        if (!first) return;
        try
        {
            using var host = new ServiceHost();
            await host.RunAsync();
        }
        catch (Exception error)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "service-error.txt"), error.ToString());
            Environment.ExitCode = 1;
        }
        finally { MediaFoundationApi.Shutdown(); }
    }
}
