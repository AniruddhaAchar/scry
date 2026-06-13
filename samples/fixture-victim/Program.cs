using System.Threading;
using System.Threading.Tasks;

namespace ScryVictim;

// A multi-mode "victim" that reproduces common .NET failure modes so we can capture fixture
// dumps for the scry skill evals (skills/scry/evals/evals.json) and the issue #1 demo set.
// Run with one of:
//   victim hang   -> contended monitor (one thread holds a lock another waits on) + an async
//                    method parked forever at an await. Diagnosable via syncblk + dumpasync.
//   victim idle   -> a healthy, idle process: no exceptions, no locks, no leak, no pending awaits.
//                    The under-determined fixture — correct diagnosis is "not enough information".
//   victim leak   -> a static collection that grows without bound (byte[] dominate the heap).
//                    Diagnosable via dumpheap --stat + gcroot.
//
// See eng/scripts/make-fixtures.ps1 to build this and snapshot a dump per mode.
internal static class Program
{
    private static readonly object Gate = new();
    private static readonly TaskCompletionSource<int> Never = new();

    // A static, long-lived collection — the retaining root for the leak scenario.
    private static readonly System.Collections.Generic.List<byte[]> LeakyCache = new();

    private static async Task Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "idle";
        switch (mode)
        {
            case "hang":
                await RunHang();
                break;
            case "leak":
                RunLeak();
                break;
            default:
                RunIdle();
                break;
        }

        System.Console.WriteLine("ready");
        Thread.Sleep(Timeout.Infinite);
    }

    private static async Task RunHang()
    {
        // Async hang: this method suspends at its await and never resumes, so the runtime boxes
        // its state machine on the heap → dumpasync shows it "suspended at await 0".
        _ = WorkAsync();
        await Task.Yield();

        // Contended monitor: thread A owns Gate forever; thread B blocks waiting on it. The
        // contention inflates a real sync block → syncblk shows owner + a waiter.
        StartLockThread("holder");
        Thread.Sleep(200); // let the holder take the lock first
        StartLockThread("waiter");
    }

    private static void StartLockThread(string name) =>
        new Thread(() =>
        {
            lock (Gate)
            {
                Thread.Sleep(Timeout.Infinite);
            }
        })
        { IsBackground = true, Name = name }.Start();

    private static async Task WorkAsync()
    {
        await Never.Task; // suspends here permanently
        System.Console.WriteLine("unreachable");
    }

    private static void RunIdle()
    {
        // Deliberately boring: nothing pathological for scry to find.
    }

    private static void RunLeak()
    {
        // Grow a static collection forever; a snapshot taken after a few seconds is dominated
        // by byte[], all rooted through LeakyCache (a static field).
        var pump = new Thread(() =>
        {
            while (true)
            {
                lock (LeakyCache)
                {
                    LeakyCache.Add(new byte[1024 * 1024]);
                }

                Thread.Sleep(5);
            }
        })
        { IsBackground = true, Name = "leak-pump" };
        pump.Start();
    }
}
