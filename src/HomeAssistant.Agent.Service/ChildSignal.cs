using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Service;

// "do shell script ... with administrator privileges" starts the elevated
// verbs (setup, install, plugins, ...) with SIGCHLD blocked or ignored, and a
// process inherits both. .NET then never learns that a child exited:
// Process.WaitForExit on chown or launchctl waits forever, and the desktop
// app with it. Putting the default back and unblocking it before the first
// Process.Start makes .NET take SIGCHLD over as usual.
internal static class ChildSignal
{
    // macOS values (signal.h).
    private const int SigChld = 20;
    private const int SigUnblock = 2;

    public static void RestoreDefault()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        signal(SigChld, IntPtr.Zero /* SIG_DFL */);

        var set = 1u << (SigChld - 1);
        sigprocmask(SigUnblock, ref set, IntPtr.Zero);
    }

    [DllImport("libc")]
    private static extern IntPtr signal(int signal, IntPtr handler);

    // sigset_t is a 32-bit mask on macOS.
    [DllImport("libc")]
    private static extern int sigprocmask(int how, ref uint set, IntPtr oldSet);
}
