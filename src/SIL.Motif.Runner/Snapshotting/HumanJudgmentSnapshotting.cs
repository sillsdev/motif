using System;
using System.Collections.Generic;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Reads a Notebook record's reserved judgment field into the semantic snapshot. The field is a project-local
/// custom field, so its id is resolved by name at run time rather than generated from the LibLCM model.
/// </summary>
internal static class HumanJudgmentSnapshotting
{
    /// <summary>
    /// Returns no alternatives when the project has no reserved field, so a project that never initialized
    /// judgments keeps its digest, pinned by `ProjectWithoutTheJudgmentFieldEmitsNoJudgmentAlternatives`.
    /// An ambiguous or incompatible field throws, pinned by `AnAmbiguousJudgmentFieldRefusesTheSnapshot`.
    /// </summary>
    public static Dictionary<string, string> ReadRecord(LcmCache cache, IRnGenericRec record)
    {
        var field = SnapshotPass.JudgmentField(cache);
        if (field is null) return new();

        return StringFieldSnapshotting.ReadAlternatives(cache,
            cache.DomainDataByFlid.get_StringProp(record.Hvo, field.Value));
    }
}

/// <summary>
/// One pass of semantic snapshots over many objects of one cache. Inside a pass the project-local judgment field
/// is resolved once, on the first Notebook record, rather than once per record; outside a pass every record
/// resolves it again. Dispose the pass on the thread that began it, when the pass ends, so a schema change made
/// afterwards is seen by the next snapshot.
/// </summary>
public sealed class SnapshotPass : IDisposable
{
    [ThreadStatic] private static SnapshotPass? t_current;

    private readonly SnapshotPass? _outer;
    private readonly LcmCache _cache;
    private bool _resolved;
    private int? _judgmentField;

    private SnapshotPass(LcmCache cache)
    {
        _cache = cache;
        _outer = t_current;
        t_current = this;
    }

    /// <summary>Begins a pass over objects of <paramref name="cache"/> on the current thread.</summary>
    public static SnapshotPass Begin(LcmCache cache) =>
        new(cache ?? throw new ArgumentNullException(nameof(cache)));

    /// <summary>Ends the pass and restores any pass that enclosed it.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(t_current, this)) t_current = _outer;
    }

    internal static int? JudgmentField(LcmCache cache)
    {
        var pass = t_current;
        if (pass is null || !ReferenceEquals(pass._cache, cache))
            return HumanJudgmentCustomFieldHandler.TryResolveField(cache);
        // A refusal is not remembered, so every record inside the pass still throws as it would outside one.
        if (!pass._resolved)
        {
            pass._judgmentField = HumanJudgmentCustomFieldHandler.TryResolveField(cache);
            pass._resolved = true;
        }
        return pass._judgmentField;
    }
}
