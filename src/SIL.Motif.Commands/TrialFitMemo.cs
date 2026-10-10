using System.Collections.Concurrent;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// Remembers a Proposal's fit check against its Trial copy, because opening that copy is slow. The key holds the
/// Draft revision and whether the copy still exists, so an edit or a cleaned-up copy is checked afresh.
/// </summary>
internal static class TrialFitMemo
{
    private const int Capacity = 64;
    private static readonly ConcurrentDictionary<(string ProposalId, string Revision, string? JobId, bool ScratchExists),
        IReadOnlyList<ChangeFit>> Entries = new();

    internal static IReadOnlyList<ChangeFit> GetOrCompute(string proposalId, string revision, string? jobId,
        bool scratchExists, Func<IReadOnlyList<ChangeFit>> compute)
    {
        var key = (proposalId, revision, jobId, scratchExists);
        if (Entries.TryGetValue(key, out var cached)) return cached;
        var computed = compute();
        if (Entries.Count >= Capacity) Entries.Clear();
        Entries[key] = computed;
        return computed;
    }
}
