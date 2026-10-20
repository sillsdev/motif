using System.Collections.Concurrent;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The warm parser sessions behind Try a Word: one resident PanGloss per open project (ADR 0044 amendment). A long-lived host (the MCP
/// server, the App) enables them; a one-shot CLI process keeps the cold path because it would exit before a second word could use the load.
/// </summary>
public static class TryWordSessions
{
    private static readonly ConcurrentDictionary<string, WarmPanGlossPool> Pools = new(StringComparer.OrdinalIgnoreCase);
    private static int _enabled = Environment.GetEnvironmentVariable("MOTIF_WARM_PARSER") == "0" ? -1 : 0;

    /// <summary>Whether Try a Word uses resident sessions in this process.</summary>
    public static bool Enabled => _enabled > 0;

    /// <summary>Turns sessions on for a long-lived host. A <c>MOTIF_WARM_PARSER=0</c> environment keeps them off.</summary>
    public static void Enable()
    {
        if (Interlocked.CompareExchange(ref _enabled, 1, 0) == 0)
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
    }

    /// <summary>Raised off the UI thread when any project's session changes state; the App marshals it.</summary>
    public static event EventHandler<WarmSessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Call when the Try a Word page opens. Starts loading the project's Baseline in the background and returns at
    /// once; idempotent. The task completes with Ready or Failed (code and message) and never faults. A project with no
    /// Baseline, or a parser without serve, is Failed and its words run cold.
    /// </summary>
    /// <param name="projectPath">The Motif project path, as the trace request names it.</param>
    /// <param name="parserPath">The parser to run, or null for the located one.</param>
    public static Task<WarmSessionState> Prewarm(string projectPath, string? parserPath = null)
    {
        Enable();
        return Task.Run(async () =>
        {
            var target = Resolve(projectPath);
            if (target is null)
                return new WarmSessionState(WarmSessionPhase.Failed, "wordtrace.no-baseline",
                    "The project has no Baseline yet.");
            return await PoolFor(parserPath).Prewarm(projectPath, target.Value.Token, target.Value.GrammarPath).ConfigureAwait(false);
        });
    }

    /// <summary>The state of a project's session: Idle, Loading, Ready or Failed.</summary>
    public static WarmSessionState GetState(string projectPath, string? parserPath = null) =>
        PoolFor(parserPath).GetState(projectPath);

    internal static WarmPanGlossPool PoolFor(string? parserPath)
    {
        var executable = parserPath ?? PanGlossExecutable.TryLocate();
        return Pools.GetOrAdd(executable ?? string.Empty, path =>
        {
            var pool = new WarmPanGlossPool(path.Length == 0 ? null : path);
            pool.StateChanged += (sender, args) => StateChanged?.Invoke(sender, args);
            return pool;
        });
    }

    internal static WarmFirstTracer TracerFor(string? parserPath, PanGlossInvoker invoker) =>
        new(PoolFor(parserPath), new PanGlossTracer(invoker), invoker);

    /// <summary>The session key part that changes whenever the Baseline does.</summary>
    internal static string TokenText(SIL.Motif.Contract.Baselines.BaselineToken token) =>
        token.SemanticSnapshotDigest + "|" + token.BundleDigest;

    /// <summary>Stops every resident parser; the host calls it on exit.</summary>
    public static void Shutdown()
    {
        foreach (var pool in Pools.Values) pool.Dispose();
        Pools.Clear();
    }

    private static (string Token, string GrammarPath)? Resolve(string projectPath)
    {
        var outcome = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
            return baseline is null
                ? CommandOutcome<string>.Success(string.Empty)
                : CommandOutcome<string>.Success(TokenText(baseline.Token) + "\n" + baseline.FwDataPath);
        });
        if (!outcome.Succeeded || string.IsNullOrEmpty(outcome.Value)) return null;
        var parts = outcome.Value.Split('\n', 2);
        return (parts[0], parts[1]);
    }
}
