using System.Runtime.InteropServices;

namespace Scry.Client;

/// <summary>
/// Stops a spawned child from inheriting scry's standard handles (Windows).
///
/// When scry spawns the detached scryd, Windows propagates scry's stdout/stderr
/// handle values to the child even with <c>CreateNoWindow</c>. The daemon then
/// holds those handles open past scry's exit, so any caller that reads scry's
/// stdout to EOF (a shell pipe, <c>subprocess.run(capture_output=True)</c>, ...)
/// hangs until the daemon dies. Clearing the inherit flag on our standard handles
/// before spawning prevents that. On Unix the runtime already sets close-on-exec,
/// so this is a no-op there.
///
/// This is ordinary managed P/Invoke — it does not require <c>unsafe</c> code.
/// </summary>
internal static class StdHandle
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const int HandleFlagInherit = 0x1;

    public static void MakeNonInheritable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var id in new[] { StdInputHandle, StdOutputHandle, StdErrorHandle })
        {
            var handle = GetStdHandle(id);
            if (handle != nint.Zero && handle != new nint(-1))
            {
                SetHandleInformation(handle, HandleFlagInherit, 0);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(nint hObject, int dwMask, int dwFlags);
}
