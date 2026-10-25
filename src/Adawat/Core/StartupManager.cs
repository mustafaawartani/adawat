using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Adawat.Core;

/// <summary>
/// Starts Adawat with Windows. Normal mode uses the per-user Run key. Elevated mode uses a
/// scheduled task with "highest privileges", which starts as administrator without a UAC prompt.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string TaskName = "Adawat";
    public const string SetupTaskArgument = "--setup-elevated-task";
    public const string RemoveTaskArgument = "--remove-elevated-task";

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Makes the Run key match the settings (the scheduled task is handled separately).</summary>
    public static void ApplyRunKey(AppSettings settings)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (settings.StartWithWindows && !settings.RunElevated)
                key.SetValue(AppPaths.AppName, $"\"{AppPaths.ExecutablePath}\" --startup");
            else
                key.DeleteValue(AppPaths.AppName, false);
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the startup entry", ex);
        }
    }

    public static bool TaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>
    /// Creates the elevated logon task. When this process is not elevated, an elevated copy of
    /// Adawat is started to do it (one UAC prompt). Returns false if the user cancelled.
    /// </summary>
    public static bool EnableElevatedStartup(bool startWithWindows)
    {
        if (IsElevated) return CreateTask(startWithWindows) == 0;
        return RunElevatedHelper($"{SetupTaskArgument} {(startWithWindows ? "logon" : "nologon")}");
    }

    public static bool DisableElevatedStartup()
    {
        if (!TaskExists()) return true;
        if (IsElevated) return RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;
        return RunElevatedHelper(RemoveTaskArgument);
    }

    /// <summary>Starts the elevated instance through the scheduled task (no UAC prompt).</summary>
    public static bool StartElevatedNow() => RunSchtasks($"/Run /TN \"{TaskName}\"") == 0;

    /// <summary>Entry point used by the elevated helper process.</summary>
    public static int HandleHelperArguments(string[] args)
    {
        if (args.Length > 0 && args[0] == SetupTaskArgument)
            return CreateTask(args.Length < 2 || args[1] != "nologon");
        if (args.Length > 0 && args[0] == RemoveTaskArgument)
            return RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        return 1;
    }

    private static bool RunElevatedHelper(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process == null) return false;
            process.WaitForExit(30000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // The user said no to the UAC prompt.
        }
        catch (Exception ex)
        {
            Log.Error("Elevated helper failed", ex);
            return false;
        }
    }

    private static int CreateTask(bool withLogonTrigger)
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(AppPaths.ExecutablePath);
        var trigger = withLogonTrigger
            ? $"<LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT3S</Delay></LogonTrigger>"
            : "";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Adawat as administrator when you sign in.</Description></RegistrationInfo>
              <Triggers>{trigger}</Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>6</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><Arguments>--startup</Arguments></Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"adawat-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process == null) return -1;
            process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            if (process.ExitCode != 0 && !arguments.StartsWith("/Query"))
                Log.Info($"schtasks {arguments} failed: {error.Trim()}");
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("schtasks failed", ex);
            return -1;
        }
    }
}
