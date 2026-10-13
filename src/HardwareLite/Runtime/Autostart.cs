using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HardwareLite.Runtime;

internal static class Autostart
{
    private const string TaskName = "HardwareLite";
    public static bool Enabled => WithScheduler((service, folder) =>
    {
        dynamic task;
        try { task = folder.GetTask(TaskName); }
        catch (FileNotFoundException) { return false; }
        catch (COMException error) when (error.HResult == unchecked((int)0x80070002)) { return false; }
        try { Verify(task); return (bool)task.Enabled; }
        finally { Marshal.FinalReleaseComObject(task); }
    });

    public static void SetEnabled(bool enabled) => WithScheduler((service, folder) =>
    {
        dynamic? existing = null;
        try { existing = folder.GetTask(TaskName); }
        catch (FileNotFoundException) { }
        catch (COMException error) when (error.HResult == unchecked((int)0x80070002)) { }
        if (existing is not null)
        {
            try { Verify(existing); }
            finally { Marshal.FinalReleaseComObject(existing); }
        }
        if (!enabled)
        {
            if (existing is not null) folder.DeleteTask(TaskName, 0);
            return true;
        }
        string user = WindowsIdentity.GetCurrent().User!.Value;
        dynamic definition = service.NewTask(0);
        dynamic? trigger = null, execution = null, registered = null;
        try
        {
            definition.RegistrationInfo.Author = WindowsIdentity.GetCurrent().Name;
            definition.RegistrationInfo.Description = "HardwareLite 统一设备控制与共享监控";
            definition.Principal.UserId = user;
            definition.Principal.LogonType = 3;
            definition.Principal.RunLevel = 1;
            definition.Settings.Enabled = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT0S";
            definition.Settings.MultipleInstances = 2;
            trigger = definition.Triggers.Create(9);
            trigger.UserId = user;
            execution = definition.Actions.Create(0);
            execution.Path = Path.Combine(AppContext.BaseDirectory, "HardwareLite.Service.exe");
            execution.Arguments = "";
            execution.WorkingDirectory = AppContext.BaseDirectory;
            registered = folder.RegisterTaskDefinition(TaskName, definition, 6, user, null, 3, null);
            return true;
        }
        finally
        {
            if (registered is not null) Marshal.FinalReleaseComObject(registered);
            if (execution is not null) Marshal.FinalReleaseComObject(execution);
            if (trigger is not null) Marshal.FinalReleaseComObject(trigger);
            Marshal.FinalReleaseComObject(definition);
        }
    });

    private static void Verify(dynamic task)
    {
        dynamic definition = task.Definition;
        dynamic actions = definition.Actions;
        dynamic action = actions[1];
        try
        {
            string service = Path.Combine(AppContext.BaseDirectory, "HardwareLite.Service.exe");
            string legacy = Path.Combine(AppContext.BaseDirectory, "HardwareLite.exe");
            bool serviceTask = string.Equals((string)action.Path, service, StringComparison.OrdinalIgnoreCase);
            bool legacyTask = string.Equals((string)action.Path, legacy, StringComparison.OrdinalIgnoreCase) && (string)action.Arguments == "--tray";
            if (actions.Count != 1 || !serviceTask && !legacyTask)
                throw new InvalidOperationException("同名启动任务属于其他程序，未修改。");
        }
        finally { Marshal.FinalReleaseComObject(action); Marshal.FinalReleaseComObject(actions); Marshal.FinalReleaseComObject(definition); }
    }

    private static T WithScheduler<T>(Func<dynamic, dynamic, T> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        dynamic? folder = null;
        try { service.Connect(); folder = service.GetFolder(@"\"); return action((object)service, (object)folder); }
        finally { if (folder is not null) Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(service); }
    }
}
