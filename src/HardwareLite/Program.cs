using HardwareLite.Runtime;
using HardwareLite.Communication;
using HardwareLite.Configuration;
using HardwareLite.UI;
using HardwareLite.UI.Screen;
using NAudio.MediaFoundation;

namespace HardwareLite;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try
        {
            if (args.Contains("--tray")) { BackendClient.StartService(); return; }
            if (args.Contains("--exit"))
            {
                InstanceWindow.ExitExisting();
                try { new BackendClient().Send(new("stop")); }
                catch (TimeoutException) { }
                return;
            }
            using var singleton = new Mutex(true, @"Local\HardwareLite.Gui." + BackendProtocol.InstanceId, out bool first);
            if (!first) { InstanceWindow.ShowExisting(); return; }
            var client = new BackendClient();
            BackendClient.StartService();
            client.SendAsync(new("status"), connectTimeoutMs: 15000).GetAwaiter().GetResult();
            var store = new SettingsStore(Path.Combine(AppContext.BaseDirectory, "config"));
            Settings settings = store.Load();
            using var cancellation = new CancellationTokenSource();
            Task poll = client.PollAsync(cancellation.Token);
            using var form = new SettingsForm(() => settings, value => { client.Send(new("apply", value)); settings = value; }, client,
                () => client.State.Error.Length == 0 ? client.State.Devices : client.State.Devices with { Case = client.State.Error },
                () => client.State.Connected, () => client.Send(new("toggle")), () => client.State.Autostart, value => client.Send(new("autostart", Autostart: value)),
                () =>
                {
                    using var dialog = new FirmwareForm(() => client.State,
                        package => client.Send(new("firmware", FirmwarePath: package.FilePath, FirmwareSha256: package.Sha256)));
                    dialog.ShowDialog();
                }, () => client.Send(new("stop")), () => client.State.DeviceMediaCapacityBytes, () => client.State.DeviceNativeVideoSupported);
            using var instance = new InstanceWindow(() =>
            {
                if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                form.Show(); form.Activate();
            }, form.Close);
            try { Application.Run(form); }
            finally { cancellation.Cancel(); poll.GetAwaiter().GetResult(); }
        }
        catch (Exception error)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "fatal-error.txt"), error.ToString());
            MessageBox.Show(error.GetBaseException().Message, "聚合argb驱动 运行失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
        finally { MediaFoundationApi.Shutdown(); }
    }
}
