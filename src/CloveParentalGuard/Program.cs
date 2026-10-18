using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using CloveParentalGuard.Admin;
using CloveParentalGuard.Service;

namespace CloveParentalGuard.App;

/// <summary>
/// One executable, several roles. How it was launched decides what it does:
///   (run as a Windows service)  -> the SYSTEM enforcement worker
///   --agent                     -> the user-session tray app
///   --admin                     -> the settings window
///   --install / --uninstall     -> the (elevated) installer steps
///   (double-click, no args)     -> the setup window
/// </summary>
[SupportedOSPlatform("windows")]
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The Service Control Manager launches us with no special args, so detect the
        // service context first — that frees the installer from quoting a flag into binPath.
        if (WindowsServiceHelpers.IsWindowsService())
            return RunService(args);

        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        switch (mode)
        {
            case "--service":
                return RunService(args);

            case "--agent":
                ApplicationConfiguration.Initialize();
                TrayApp.Run();
                return 0;

            case "--preview":
                ApplicationConfiguration.Initialize();
                var preview = new CloveParentalGuard.Agent.UnlockForm(
                    new CloveParentalGuard.Agent.PipeClient(Constants.PipeName),
                    new[] { 30, 60, 120, 180 },
                    "Minecraft.exe",
                    previewMode: true)
                {
                    TopMost = true,
                };
                Application.Run(preview);
                return 0;

            case "--admin":
                ApplicationConfiguration.Initialize();
                Application.Run(new AdminForm());
                return 0;

            case "--install":
                return Installer.RunInstall(args.Length > 1 ? args[1] : null);

            case "--uninstall":
                return Installer.RunUninstall();

            default:
                ApplicationConfiguration.Initialize();
                Application.Run(new SetupForm());
                return 0;
        }
    }

    private static int RunService(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(o => o.ServiceName = Installer.ServiceName);
        builder.Services.AddHostedService<Worker>();
        builder.Build().Run();
        return 0;
    }
}
