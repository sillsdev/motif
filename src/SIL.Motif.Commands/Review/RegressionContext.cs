using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>The objects one Proposal operation names, with the entries and forms they stand for.</summary>
/// <param name="Position">The one-based position of the operation in the Proposal.</param>
/// <param name="OperationId">The operation's canonical id.</param>
/// <param name="Kind">The operation kind.</param>
/// <param name="Ids">The objects the operation names and, for an entry, its forms and analyses.</param>
internal sealed record OperationObjects(int Position, string OperationId, string Kind, IReadOnlySet<Guid> Ids);

/// <summary>What a regression row needs beyond the report: the Proposal's operations and the Baseline's entry of a form.</summary>
/// <param name="Operations">Each operation with the objects it names.</param>
/// <param name="Reader">The open Baseline copy, or null when none could be read; disposing the context closes it.</param>
/// <param name="BaselineLimit">Why no Baseline copy was read, or null when one was; the overlap is then narrower.</param>
internal sealed record RegressionContext(IReadOnlyList<OperationObjects> Operations, BaselineReadCache? Reader,
    string? BaselineLimit = null) : IDisposable
{
    public void Dispose() => Reader?.Dispose();

    /// <summary>The id of the entry that owns a form or analysis in the Baseline, or null when it holds none.</summary>
    internal string? EntryOf(string id) =>
        Reader is not null && ToGuid(id) is { } guid &&
        Reader.Cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var found) && OwningEntry(found) is { } entry
            ? CanonicalId.FromGuid(entry.Guid).ToString() : null;

    /// <summary>Parses a canonical id or a GUID; null for any other text.</summary>
    internal static Guid? ToGuid(string text) =>
        Guid.TryParse(text, out var guid) ? guid : CanonicalId.TryParse(text, out var canonical) ? canonical.ToGuid() : null;

    /// <summary>
    /// The operations whose objects overlap an id a Baseline analysis holds. This states a shared object and nothing more:
    /// it is not a cause, and an operation with no overlap may still have caused the loss.
    /// </summary>
    internal static IReadOnlyList<TouchedByOperation> Overlap(RegressionContext context, IReadOnlyList<RegressedReading> before)
    {
        var held = new HashSet<Guid>();
        foreach (var morph in before.SelectMany(reading => reading.Morphs))
            foreach (var id in new[] { morph.EntryId, morph.MsaId, morph.FormId })
                if (id is not null && ToGuid(id) is { } guid) held.Add(guid);
        var result = new List<TouchedByOperation>();
        foreach (var operation in context.Operations)
        {
            var shared = operation.Ids.Where(held.Contains).Select(id => CanonicalId.FromGuid(id).ToString()).Order(StringComparer.Ordinal).ToArray();
            if (shared.Length == 0) continue;
            result.Add(new TouchedByOperation(operation.Position, operation.OperationId, operation.Kind, shared,
                $"Operation {operation.Position} ({operation.Kind}) names {string.Join(", ", shared)}, which this word's " +
                "Baseline reading uses. This is an overlap of ids, not a finding that the operation caused the loss."));
        }
        return result;
    }

    /// <summary>
    /// Reads the Proposal and the current Baseline. Without a readable Baseline the operations name only the ids they
    /// carry and no entry is read, so the overlap is narrower, never wrong.
    /// </summary>
    internal static RegressionContext Load(MotifDatabase database, string projectKey, string proposalId)
    {
        var proposal = TrialTouchedWords.LoadProposal(database, proposalId);
        BaselineReadCache? reader = null;
        string? baselineLimit = null;
        try
        {
            if (new BaselineRepository(database).GetCurrentPublished(projectKey) is { } baseline)
                reader = BaselineReadCache.Open(baseline.FwDataPath);
            else baselineLimit = "the project has no current Baseline";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or InvalidDataException)
        {
            baselineLimit = "the Baseline copy could not be read (" + exception.Message + ")";
        }
        return Build(proposal, reader, baselineLimit);
    }

    /// <summary>Names the objects each operation of <paramref name="proposal"/> touches, expanded through <paramref name="reader"/> when it is open.</summary>
    internal static RegressionContext Build(Proposal proposal, BaselineReadCache? reader, string? baselineLimit)
    {
        var operations = new List<OperationObjects>();
        var position = 0;
        foreach (var operation in proposal.Operations)
        {
            position++;
            var ids = new HashSet<Guid>(TrialTouchedWords.OperationIds(operation));
            if (reader is not null)
                foreach (var id in ids.ToArray())
                {
                    var morphs = new HashSet<Guid>();
                    var infos = new HashSet<Guid>();
                    TrialTouchedWords.Expand(reader.Cache, id, morphs, infos);
                    ids.UnionWith(morphs);
                    ids.UnionWith(infos);
                }
            operations.Add(new OperationObjects(position, operation.OperationId.ToString(), operation.Kind, ids));
        }
        return new RegressionContext(operations, reader, baselineLimit);
    }

    private static ILexEntry? OwningEntry(ICmObject found) => found switch
    {
        ILexEntry entry => entry,
        IMoForm or IMoMorphSynAnalysis => found.OwnerOfClass<ILexEntry>(),
        _ => null,
    };
}
