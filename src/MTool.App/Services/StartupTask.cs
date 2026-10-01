using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;
using MTool.App.ViewModels;

namespace MTool.App.Services;

/// <summary>
/// The sign-in task in Windows Task Scheduler: elevated (no UAC prompt), for this user only, with
/// none of the defaults that would stop a tray app (battery rules, 72-hour limit, low priority).
/// Tested by hand: it needs the real Task Scheduler.
/// </summary>
internal sealed class StartupTask : IStartupTask
{
    public string? QueryRegisteredExe()
    {
        using var service = new TaskService();
        using var task = service.GetTask(StartupTaskSpec.TaskName);
        if (task is null)
        {
            return null;
        }

        // Anyone may create a task by this name; only one shaped like ours counts as ours.
        return IsOurs(task) ? ((ExecAction)task.Definition.Actions[0]).Path : "";
    }

    public void Enable(string exePath)
    {
        // The SID, not the account name: Microsoft and Entra account names may not resolve in Task Scheduler.
        var user = CurrentUserSid;
        using var service = new TaskService();
        using var definition = service.NewTask();
        definition.RegistrationInfo.Description = "M-Tool'u oturum açılışında tepside başlatır.";
        definition.Principal.UserId = user;
        definition.Principal.LogonType = TaskLogonType.InteractiveToken;
        definition.Principal.RunLevel = TaskRunLevel.Highest;
        definition.Triggers.Add(new LogonTrigger { UserId = user, Delay = StartupTaskSpec.SignInDelay });
        definition.Actions.Add(new ExecAction(exePath, StartupTaskSpec.Arguments, Path.GetDirectoryName(exePath)));

        var settings = definition.Settings;
        settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false;
        settings.ExecutionTimeLimit = TimeSpan.Zero;
        settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        settings.Priority = ProcessPriorityClass.Normal;

        service.RootFolder.RegisterTaskDefinition(
            StartupTaskSpec.TaskName, definition, TaskCreation.CreateOrUpdate, user, null, TaskLogonType.InteractiveToken);
    }

    private static string CurrentUserSid =>
        WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Kullanıcı SID'i okunamadı.");

    private static bool IsOurs(Microsoft.Win32.TaskScheduler.Task task)
    {
        var definition = task.Definition;
        return task.Enabled
            && definition.Principal.RunLevel == TaskRunLevel.Highest
            && definition.Triggers.OfType<LogonTrigger>().Any()
            && definition.Actions is [ExecAction { Arguments: StartupTaskSpec.Arguments }];
    }

    public void Disable()
    {
        using var service = new TaskService();
        service.RootFolder.DeleteTask(StartupTaskSpec.TaskName, exceptionOnNotExists: false);
    }
}
