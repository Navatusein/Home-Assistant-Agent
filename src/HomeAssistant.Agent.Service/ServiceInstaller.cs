#if WINDOWS
using System.Diagnostics;
using System.ServiceProcess;

namespace HomeAssistant.Agent.Service;

// Management verbs for the service, run elevated (by the desktop app with the
// runas verb, or by the installer):
//
//   install    create the service — or, when it exists, point it at this
//              executable (an update, or a move from a development build) —
//              and start it
//   uninstall  stop and delete it
//   start      start it
//   stop       stop it and wait until it has stopped, so its files can be
//              replaced right after
//   status     exit code 0 = running, 1 = installed but not running,
//              2 = not installed
//
// Registration goes through sc.exe; state is read and awaited through
// ServiceController, never from sc.exe's localized output.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class ServiceInstaller
{
    // The service-manager registration id, not the assembly name (see
    // BackgroundServiceInfo for why it keeps its old value).
    private const string ServiceName = Core.BackgroundServiceInfo.ServiceName;
    private const string DisplayName = "Home Assistant Agent Service";

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);

    public static int Run(string verb)
    {
        try
        {
            return verb.ToLowerInvariant() switch
            {
                "install" => Install(),
                "uninstall" => Uninstall(),
                "start" => Start(),
                "stop" => Stop(),
                "status" => Status(),
                _ => Unknown(verb),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Install()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve the service executable path.");

        var result = Find() == null
            ? RunSc($"create {ServiceName} binPath= \"{exePath}\" start= auto DisplayName= \"{DisplayName}\"")
            : ReconfigureExisting(exePath);

        if (result != 0)
            return result;

        RunSc($"description {ServiceName} \"Bridges this PC to Home Assistant over MQTT before any user logs in.\"");
        RunSc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000");

        return Start();
    }

    // The binary path is replaced only while the service is stopped.
    private static int ReconfigureExisting(string exePath)
    {
        Stop();
        return RunSc($"config {ServiceName} binPath= \"{exePath}\" start= auto DisplayName= \"{DisplayName}\"");
    }

    private static int Uninstall()
    {
        if (Find() == null)
            return 0;

        Stop();
        return RunSc($"delete {ServiceName}");
    }

    private static int Start()
    {
        using var service = Find();

        if (service == null)
            return 2;

        if (service.Status != ServiceControllerStatus.Running)
        {
            if (service.Status != ServiceControllerStatus.StartPending)
                service.Start();

            service.WaitForStatus(ServiceControllerStatus.Running, StateTimeout);
        }

        return 0;
    }

    private static int Stop()
    {
        using var service = Find();

        if (service == null)
            return 0;

        if (service.Status != ServiceControllerStatus.Stopped)
        {
            if (service.Status != ServiceControllerStatus.StopPending)
                service.Stop();

            service.WaitForStatus(ServiceControllerStatus.Stopped, StateTimeout);
        }

        return 0;
    }

    private static int Status()
    {
        using var service = Find();

        return service == null ? 2 : service.Status == ServiceControllerStatus.Running ? 0 : 1;
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"Unknown verb '{verb}'. Use install | uninstall | start | stop | status.");
        return 1;
    }

    private static ServiceController? Find()
    {
        var services = ServiceController.GetServices();
        ServiceController? match = null;

        foreach (var service in services)
        {
            if (match == null && string.Equals(service.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase))
                match = service;
            else
                service.Dispose();
        }

        return match;
    }

    private static int RunSc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        process.WaitForExit();

        if (process.ExitCode == 5)
            Console.Error.WriteLine("Access denied — run this command from an elevated prompt.");

        return process.ExitCode;
    }
}
#endif
