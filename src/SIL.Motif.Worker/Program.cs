using SIL.Motif.Host;

namespace SIL.Motif.Worker;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        CrashDialogs.Suppress();
        return WorkerRuntime.RunAsync(args);
    }
}
