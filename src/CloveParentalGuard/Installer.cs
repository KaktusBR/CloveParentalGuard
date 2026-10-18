using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using CloveParentalGuard.Core;
using CloveParentalGuard.Service;

namespace CloveParentalGuard.App;

/// <summary>
/// Self-install logic. The single exe copies itself into Program Files (admin-only,
/// so a child cannot delete it), registers itself as the SYSTEM service and as a
/// per-logon tray task, then removes all of that on uninstall.
/// </summary>
[SupportedOSPlatform("windows")]
static class Installer
{
    public const string ServiceName = "CloveParentalGuard";
    public const string TaskName = "CloveParentalGuardAgent";

    private static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CloveParentalGuard");
    public static string InstalledExe => Path.Combine(InstallDir, "CloveParentalGuard.exe");
    private static string ProgramDataDir => Path.GetDirectoryName(Constants.ConfigPath)!;

    // ---- queries ----------------------------------------------------------

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool ServiceExists() => Run("sc.exe", $"query {ServiceName}") == 0;

    // The service is the source of truth: uninstall leaves the exe behind, so a
    // leftover file alone must not read as "installed".
    public static bool IsInstalled() => File.Exists(InstalledExe) && ServiceExists();

    // ---- elevation bridge (called from the unelevated setup window) -------

    /// <summary>Relaunches this exe elevated with the given args; waits and returns its exit code (-1 if UAC declined).</summary>
    public static int RelaunchElevated(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1; // user dismissed the UAC prompt
        }
    }

    /// <summary>Launches the tray agent in the current (unelevated) user session.</summary>
    public static void StartAgentInteractive()
    {
        try { Process.Start(new ProcessStartInfo(InstalledExe, "--agent") { UseShellExecute = false }); }
        catch (Exception ex) { Log("agent start failed: " + ex); }
    }

    // ---- elevated steps ---------------------------------------------------

    /// <param name="codeHashFile">Optional temp file holding the JSON-serialized hashed parent code written by setup.</param>
    public static int RunInstall(string? codeHashFile)
    {
        try
        {
            Directory.CreateDirectory(InstallDir);

            // Free the installed exe before overwriting it: on an update the service
            // and the tray agent are running that image and hold it locked.
            var serviceExisted = ServiceExists();
            if (serviceExisted)
            {
                Run("sc.exe", $"stop {ServiceName}");
                WaitForServiceStopped(TimeSpan.FromSeconds(15));
            }
            KillInstalledAgents();

            var src = Environment.ProcessPath!;
            if (!PathsEqual(src, InstalledExe))
                CopyWithRetry(src, InstalledExe);

            string? codeJson = null;
            if (codeHashFile is { Length: > 0 } && File.Exists(codeHashFile))
            {
                codeJson = File.ReadAllText(codeHashFile).Trim();
                try { File.Delete(codeHashFile); } catch { /* best effort */ }
            }
            WriteConfig(codeJson);

            if (!serviceExisted)
                // Quoting matters: sc needs binPath= followed by a space, then the
                // quoted exe path as a single value.
                Run("sc.exe", $"create {ServiceName} binPath= \"\\\"{InstalledExe}\\\"\" " +
                              "start= auto obj= LocalSystem DisplayName= \"CloveParentalGuard\"");

            Run("sc.exe", $"failure {ServiceName} reset= 0 actions= restart/5000/restart/5000/restart/5000");
            Run("sc.exe", $"start {ServiceName}");

            RegisterLogonTask();
            RegisterAppEntry();
            return 0;
        }
        catch (Exception ex)
        {
            Log("install failed: " + ex);
            return 1;
        }
    }

    /// <summary>Polls sc until the service reports STOPPED (or is gone / times out).</summary>
    private static void WaitForServiceStopped(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var (code, output) = ExecOut("sc.exe", $"query {ServiceName}");
            if (code != 0) return;                      // service no longer exists
            if (output.Contains("STOPPED")) return;     // reached the stopped state
            Thread.Sleep(400);
        }
    }

    /// <summary>Kills CloveParentalGuard processes running from the installed exe (the tray agent),
    /// never this installer, so the image can be replaced.</summary>
    private static void KillInstalledAgents()
    {
        var me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("CloveParentalGuard"))
        {
            try
            {
                if (p.Id == me) continue;
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* access denied / exited */ }
                if (path is not null && PathsEqual(path, InstalledExe))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex) { Log("kill agent failed: " + ex); }
            finally { p.Dispose(); }
        }
    }

    /// <summary>Copies over the installed exe, retrying while the previous image's lock is released.</summary>
    private static void CopyWithRetry(string src, string dest)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { File.Copy(src, dest, overwrite: true); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 12)
            {
                Thread.Sleep(300);
            }
        }
    }

    public static int RunUninstall()
    {
        try
        {
            Run("sc.exe", $"stop {ServiceName}");
            WaitForServiceStopped(TimeSpan.FromSeconds(15));
            Run("sc.exe", $"delete {ServiceName}");
            RunList("schtasks.exe", "/Delete", "/TN", TaskName, "/F");
            KillInstalledAgents(); // remove the tray icon immediately
            CleanHosts();
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); }
            catch (Exception ex) { Log("app entry cleanup failed: " + ex); }
            try { if (Directory.Exists(ProgramDataDir)) Directory.Delete(ProgramDataDir, recursive: true); }
            catch (Exception ex) { Log("config cleanup failed: " + ex); }
            // This process (or the setup window) may be running from InstallDir, so it cannot
            // delete its own image. Hand the folder to a detached helper that retries
            // until the exe is released.
            ScheduleInstallDirDelete();
            return 0;
        }
        catch (Exception ex)
        {
            Log("uninstall failed: " + ex);
            return 1;
        }
    }

    // ---- helpers ----------------------------------------------------------

    private static void ScheduleInstallDirDelete()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c for /l %i in (1,1,30) do @(" +
                            $"rmdir /s /q \"{InstallDir}\" 2>nul & " +
                            $"if not exist \"{InstallDir}\" exit /b & " +
                            "ping -n 3 127.0.0.1 >nul)",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex) { Log("install dir cleanup failed: " + ex); }
    }

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CloveParentalGuard";

    /// <summary>Lists CloveParentalGuard under Settings → Installed apps. Uninstall opens the setup
    /// window from the Program Files copy, so it works even if the original download is gone.</summary>
    private static void RegisterAppEntry()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", "CloveParentalGuard");
            key.SetValue("Publisher", "KaktusBR");
            key.SetValue("DisplayVersion",
                typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) { Log("app entry failed: " + ex); }
    }

    private static void WriteConfig(string? codeJson)
    {
        Directory.CreateDirectory(ProgramDataDir);
        CloveParentalGuardConfig cfg;
        try { cfg = ConfigStore.Load(Constants.ConfigPath); }
        catch { cfg = CloveParentalGuardConfig.Default(); }
        if (!string.IsNullOrWhiteSpace(codeJson))
            cfg.Code = JsonSerializer.Deserialize<HashedCode>(codeJson);
        ConfigStore.Save(Constants.ConfigPath, cfg);
    }

    private static void RegisterLogonTask()
    {
        // Register-ScheduledTask handles the group principal / limited run-level cleanly;
        // single-quoted args are safe for the Program Files path.
        var ps =
            $"$a=New-ScheduledTaskAction -Execute '{InstalledExe}' -Argument '--agent'; " +
            "$t=New-ScheduledTaskTrigger -AtLogOn; " +
            "$p=New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Limited; " +
            $"Register-ScheduledTask -TaskName '{TaskName}' -Action $a -Trigger $t -Principal $p -Force | Out-Null";
        RunList("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps);
    }

    private static void CleanHosts()
    {
        try
        {
            if (!File.Exists(Constants.HostsPath)) return;
            var cleaned = HostsFileEditor.Remove(File.ReadAllText(Constants.HostsPath));
            File.WriteAllText(Constants.HostsPath, cleaned);
        }
        catch (Exception ex) { Log("hosts cleanup failed: " + ex); }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static int Run(string file, string args) =>
        Exec(new ProcessStartInfo(file, args));

    private static int RunList(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Exec(psi);
    }

    private static int Exec(ProcessStartInfo psi)
    {
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static (int code, string output) ExecOut(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "cloveparentalguard-setup.log"),
                $"{DateTime.Now:u} {msg}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
