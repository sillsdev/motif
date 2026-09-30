using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Installation;

/// <summary>Coordinates package updates with work that uses the installed application files.</summary>
public static class MotifUpdateGate
{
    private const string GateName = "SIL.Motif.UpdateActivity";
    internal const string TestNamespaceVariable = "MOTIF_TEST_UPDATE_GATE_NAMESPACE";

    /// <summary>Acquires the activity lease, or returns <see langword="null"/> when work is active.</summary>
    public static IDisposable? TryAcquire()
    {
        return TryAcquire(GetGateName(), WorkerLockAccess.Shared);
    }

    /// <summary>Acquires exclusive access to apply a package update.</summary>
    /// <returns>A lease when no activity is using the installation; otherwise, <see langword="null"/>.</returns>
    public static IDisposable? TryAcquireForUpdate()
    {
        return TryAcquire(GetGateName(), WorkerLockAccess.Exclusive);
    }

    internal static IDisposable? TryAcquire(string gateName) =>
        TryAcquire(gateName, WorkerLockAccess.Shared);

    internal static IDisposable? TryAcquireForUpdate(string gateName) =>
        TryAcquire(gateName, WorkerLockAccess.Exclusive);

    private static IDisposable? TryAcquire(string gateName, WorkerLockAccess access)
    {
        var owner = new WorkerMutexOwner(gateName, machineWide: false, access);
        if (owner.TryAcquire())
            return owner;

        owner.Dispose();
        return null;
    }

    private static string GetGateName()
    {
        var testNamespace = Environment.GetEnvironmentVariable(TestNamespaceVariable);
        return string.IsNullOrWhiteSpace(testNamespace) ? GateName : GateName + ".Test." + testNamespace;
    }
}
