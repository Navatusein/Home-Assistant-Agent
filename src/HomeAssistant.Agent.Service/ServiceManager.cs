namespace HomeAssistant.Agent.Service;

// The management verbs, for the system this runs on: the service manager on
// Windows, launchd on macOS. Both take the same verbs and answer status with
// the same exit codes (0 running, 1 installed but stopped, 2 not installed).
internal static class ServiceManager
{
    public static int Run(string verb)
    {
#if WINDOWS
        return ServiceInstaller.Run(verb);
#else
        if (OperatingSystem.IsMacOS())
            return LaunchdServiceInstaller.Run(verb);

        Console.Error.WriteLine("The background service is not supported on this system.");
        return 1;
#endif
    }
}
