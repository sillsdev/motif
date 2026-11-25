using System.Reflection;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

/// <summary>
/// The closed registry of refusal codes: one <see cref="RefusalSpec"/> per constant of
/// <see cref="RefusalCodes"/>, <see cref="HostRefusalCodes"/> and <see cref="ParsimonyRefusalCodes"/>, keyed by
/// code. A front end renders a refusal's next step from its spec, so no front end keeps a table of advice.
/// </summary>
/// <remarks>
/// Pinned by `EveryRegisteredCodeHasASpec`: a constant without a spec, or a spec without a constant, fails the
/// suite. A spec's reason is the one most of the code's call sites use; the refusal itself carries the reason
/// that applied.
/// </remarks>
public static partial class RefusalCatalog
{
    /// <summary>Every registered refusal code mapped to its spec.</summary>
    public static IReadOnlyDictionary<string, RefusalSpec> Specs { get; } = Build();

    private static IReadOnlyDictionary<string, RefusalSpec> Build()
    {
        var specs = new Dictionary<string, RefusalSpec>(StringComparer.Ordinal);
        var windowCodes = new HashSet<string>(Areas<string>("WindowCodes"), StringComparer.Ordinal);
        foreach (var spec in Areas<RefusalSpec>("Specs"))
            specs.Add(spec.Code, spec with { ShownInWindow = windowCodes.Contains(spec.Code) });
        return specs;
    }

    // Area files supply Specs<Area>() and WindowCodes<Area>(); a new area then edits no shared list.
    private static IEnumerable<T> Areas<T>(string prefix) =>
        typeof(RefusalCatalog).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                method.GetParameters().Length == 0 && method.ReturnType == typeof(IEnumerable<T>))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .SelectMany(method => (IEnumerable<T>)method.Invoke(null, null)!);

    /// <summary>Selects outcome-specific Help for a wait that stopped or could not confirm cleanup.</summary>
    public static string? WaitOutcomeHelp(Refusal refusal)
    {
        if (refusal.Code is not (RefusalCodes.JobWaitTimeout or RefusalCodes.JobWaitCancelled or
            RefusalCodes.JobWaitCleanupUnconfirmed)) return null;
        var status = refusal.Facts.GetValueOrDefault("status") ?? refusal.Facts.GetValueOrDefault("jobStatus");
        if (refusal.Facts.GetValueOrDefault("cleanup") == "incomplete")
            return refusal.Facts.GetValueOrDefault("terminal") == "true" ? "job-wait-cleanup" : "job-wait-unconfirmed";
        if (status == "cancelled") return "job-wait-ended-cancelled";
        return refusal.Facts.GetValueOrDefault("terminal") == "true" ? "job-wait-ended" : null;
    }

    private static RefusalSpec Spec(string code, FailureReason reason, NextStep? next = null) =>
        new(code, reason, next);

    private static NextStep Command(string command, params (string Name, NextArg Arg)[] args) =>
        new(command, args.ToDictionary(arg => arg.Name, arg => arg.Arg, StringComparer.Ordinal));

    private static (string Name, NextArg Arg) Caller(string name) => (name, NextArg.Caller(name));
}
