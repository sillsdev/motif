using System;
using System.Collections.Generic;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Maps an operation <c>kind</c> string to the <see cref="IOperationHandler"/> that knows how to
/// dispatch it. Runner-local and LibLCM-aware — distinct from
/// <see cref="SIL.Motif.Contract.Parsing.OperationKindRegistry"/>, which only answers "is this kind
/// string well-formed enough for strict JSON parsing to accept," LibLCM-free, in the Contract kernel.
/// A generated kind file registers with both: the Contract registry so
/// <see cref="SIL.Motif.Contract.Parsing.ProposalJsonParser"/> accepts the kind at all, and this one
/// so <see cref="SIL.Motif.Runner.DryRun.ProposalDryRunner"/>/<see cref="SIL.Motif.Runner.Apply.ProposalApplier"/>/
/// <see cref="SIL.Motif.Runner.Apply.FootprintProbe"/> know how to run it.
/// </summary>
public static class OperationHandlerRegistry
{
    private sealed record Registration(IOperationHandler Handler, IReadOnlyList<FieldWriteCapability> FieldWrites);

    private static readonly Dictionary<string, Registration> Handlers = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="handler"/> for <paramref name="kind"/>. Throws if
    /// <paramref name="kind"/> is already registered — every generated kind file registers exactly
    /// once, via its own <c>[ModuleInitializer]</c>, so a duplicate means a real bug (a name
    /// collision between two generated fields), not a legitimate re-registration.</summary>
    /// <param name="kind">The same kind the parser registers.</param>
    /// <param name="handler">The executable handler.</param>
    /// <param name="fieldWrites">Direct field edits a record importer can request through this handler.
    /// Omit them for kinds that require a different semantic intent.</param>
    public static void Register(string kind, IOperationHandler handler, params FieldWriteCapability[] fieldWrites)
    {
        if (string.IsNullOrEmpty(kind))
            throw new ArgumentException("Operation kind must not be empty.", nameof(kind));
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(fieldWrites);

        if (Handlers.ContainsKey(kind))
        {
            throw new InvalidOperationException(
                $"An operation handler is already registered for kind '{kind}'. Each kind must " +
                "register exactly one handler.");
        }

        Handlers[kind] = new(handler, Array.AsReadOnly(fieldWrites.Select(write => write with
        {
            ChildClasses = Array.AsReadOnly(write.ChildClasses.ToArray()),
        }).ToArray()));
    }

    /// <summary>
    /// Looks up the handler for <paramref name="kind"/>, or throws <see cref="NotSupportedException"/>
    /// naming <paramref name="context"/> — the exact wording each of the three former switch
    /// statements used (e.g. <c>"Stage C dryRun does not support operation kind '...'."</c>), so
    /// callers keep their error messages verbatim.
    /// </summary>
    public static IOperationHandler Resolve(string kind, string context)
    {
        if (Handlers.TryGetValue(kind, out var registration))
            return registration.Handler;

        throw new NotSupportedException($"{context} does not support operation kind '{kind}'.");
    }

    /// <summary>The registered handler's declared direct field edits, empty for a semantic operation without one.</summary>
    public static IReadOnlyList<FieldWriteCapability> FieldWrites(string kind) => Handlers[kind].FieldWrites;

    /// <summary>Every currently registered kind, for catalog enumeration and parity checks.</summary>
    public static IReadOnlyCollection<string> RegisteredKinds => Handlers.Keys;
}
