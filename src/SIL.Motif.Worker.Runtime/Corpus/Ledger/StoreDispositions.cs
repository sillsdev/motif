using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Corpus.Ledger;

/// <summary>One standing word type disposition read from the Motif store.</summary>
internal sealed record StoreDisposition(string Key, string Category, string Evidence, string? Intended)
{
    /// <summary>The state text Coverage reads: a misspelling names its intended form first, as <c>intended=X | evidence</c>.</summary>
    public string StateText => Category == "misspelling" ? $"intended={Intended} | {Evidence}" : Evidence;
}

/// <summary>A type whose store disposition and lane-ledger claim disagree; the ledger's claim is kept.</summary>
internal sealed record DispositionConflict(string Key, long Weight, string StoreClaim, string LedgerClaim);

/// <summary>Why a store row filled a type: it resolved a ledger Open or Unverified type, replaced an Expected or Lost claim pinned at an incompatible revision, or the ledger said nothing usable.</summary>
internal enum StoreFillReason { ResolvedOpenOrUnverified, ReplacedIncompatibleClaim, LedgerSilent }

internal sealed record StoreFillExample(string Key, long Weight, string StoreCategory, StoreFillReason Reason);

/// <summary>
/// How the store's standing dispositions met the lane ledger over the eligible types. Store counts every type with a
/// standing store row, Ledger every type the ledger claims as a disposition; Both agree; Conflict disagree (or the
/// ledger claims Expected or Lost readings); StoreOnly are the types the store alone accounts for, split by
/// <see cref="StoreFillReason"/> into ResolvedOpen (a ledger Open or Unverified type, so a lane can flip from fail to pass),
/// ReplacedIncompatible (an Expected or Lost claim at an incompatible revision, already untrusted) and LedgerSilent.
/// ClearedDuplicateRows and ClearedConflictingWeights count the ledger findings a store fill made moot.
/// </summary>
internal sealed record StoreDispositionSummary(
    Tally Store, Tally Ledger, Tally Both, Tally Conflict, Tally StoreOnly, long StoreRowsOutsideList,
    IReadOnlyDictionary<string, Tally> StoreByCategory, IReadOnlyList<DispositionConflict> ConflictExamples,
    Tally ResolvedOpen = default, Tally ReplacedIncompatible = default, Tally LedgerSilent = default,
    Tally ClearedDuplicateRows = default, Tally ClearedConflictingWeights = default,
    IReadOnlyList<StoreFillExample>? FillExamples = null)
{
    public const int MaxExamples = 10;
    public const int MaxFillExamples = 3;

    public IReadOnlyList<StoreFillExample> Fills => FillExamples ?? [];

    public static StoreDispositionSummary Empty { get; } = new(default, default, default, default, default, 0,
        new Dictionary<string, Tally>(), []);
}

/// <summary>Reads the store's standing rows through the existing repository, never by a second query.</summary>
internal static class StoreDispositionReader
{
    private const int PageSize = 5000;

    public static IReadOnlyDictionary<string, StoreDisposition> Read(WordTypeDispositionRepository repository)
    {
        var result = new Dictionary<string, StoreDisposition>(StringComparer.Ordinal);
        var offset = 0;
        while (true)
        {
            var (page, total) = repository.List(null, offset, PageSize);
            foreach (var row in page)
                result[LedgerJoiner.Key(row.Key)] = new(LedgerJoiner.Key(row.Key), row.Category, row.Evidence, row.Intended);
            offset += page.Count;
            if (page.Count == 0 || offset >= total) return result;
        }
    }
}

/// <summary>
/// Merges standing store dispositions into a join. The rule: A or B, I am choosing A. A: the lane ledger wins where it
/// holds a claim (a supported disposition, or Expected or Lost readings); the store fills every type the ledger makes
/// no claim about (no row, duplicate rows, an incompatible revision, Open, Unverified, or an unsupported disposition).
/// One path replaces a ledger Expected or Lost row: one pinned at an incompatible grammar revision is already untrusted
/// and makes no claim, so a store row fills it; that is counted as ReplacedIncompatible.
/// B, the store always winning, was rejected because a ledger claim is pinned and reviewed at a grammar revision and a
/// silent override would change a lane's verdict without a trace. Every disagreement is counted and shown.
/// </summary>
internal static class StoreDispositionOverlay
{
    private sealed record Claim(string Text, string? Kind, string? Intended);

    public static (LedgerJoin Join, StoreDispositionSummary Summary) Apply(
        LedgerJoin join, IReadOnlyDictionary<string, StoreDisposition>? store, string grammarRevision)
    {
        store ??= new Dictionary<string, StoreDisposition>();
        var apply = !join.Blocked && store.Count > 0;
        var types = new List<JoinedType>(join.Types.Count);
        Tally storeTally = default, ledgerTally = default, both = default, conflict = default, storeOnly = default;
        var byCategory = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        var examples = new List<DispositionConflict>();
        Tally resolvedOpen = default, replaced = default, silent = default, clearedDup = default, clearedWeight = default;
        var fills = new List<StoreFillExample>();
        long missing = join.MissingTypes, missingW = join.MissingWeight, dup = join.DuplicateTypes, dupW = join.DuplicateWeight,
            rev = join.RevisionTypes, revW = join.RevisionWeight, clash = join.ConflictTypes, clashW = join.ConflictWeight,
            residual = join.ResidualTypes, residualW = join.ResidualWeight;
        var matched = 0;
        foreach (var type in join.Types)
        {
            var claim = ClaimOf(type);
            if (claim?.Kind is not null) ledgerTally = ledgerTally.Add(type.Weight);
            if (!apply || !store.TryGetValue(type.Key, out var disposition)) { types.Add(type); continue; }
            matched++;
            storeTally = storeTally.Add(type.Weight);
            byCategory[disposition.Category] = byCategory.GetValueOrDefault(disposition.Category).Add(type.Weight);
            if (claim is null)
            {
                storeOnly = storeOnly.Add(type.Weight);
                var reason = type.Row is { State: LedgerState.Expected or LedgerState.Lost } && type.RevisionIncompatible
                    ? StoreFillReason.ReplacedIncompatibleClaim
                    : type.Row is { State: LedgerState.Open or LedgerState.Unverified }
                        ? StoreFillReason.ResolvedOpenOrUnverified : StoreFillReason.LedgerSilent;
                if (reason == StoreFillReason.ResolvedOpenOrUnverified) resolvedOpen = resolvedOpen.Add(type.Weight);
                else if (reason == StoreFillReason.ReplacedIncompatibleClaim) replaced = replaced.Add(type.Weight);
                else silent = silent.Add(type.Weight);
                if (fills.Count(item => item.Reason == reason) < StoreDispositionSummary.MaxFillExamples)
                    fills.Add(new(type.Key, type.Weight, disposition.Category, reason));
                if (type.DuplicateRows.Count > 0) clearedDup = clearedDup.Add(type.Weight);
                if (type.ConflictingTokens is not null) clearedWeight = clearedWeight.Add(type.Weight);
                if (type.Missing) { missing--; missingW -= type.Weight; }
                if (type.DuplicateRows.Count > 0) { dup--; dupW -= type.Weight; }
                if (type.RevisionIncompatible) { rev--; revW -= type.Weight; }
                if (type.ConflictingTokens is not null) { clash--; clashW -= type.Weight; }
                if (!type.Resolved) { residual--; residualW -= type.Weight; }
                var row = new LedgerRow(0, disposition.Key, type.Weight, LedgerState.Disposition, disposition.Category, "", [],
                    disposition.Category, disposition.StateText, "motif-store", grammarRevision);
                types.Add(type with { Row = row, DuplicateRows = [], Missing = false, RevisionIncompatible = false,
                    ConflictingTokens = null, FromStore = true });
                continue;
            }
            types.Add(type);
            if (claim.Kind == disposition.Category && (claim.Kind != "misspelling" ||
                    (claim.Intended is not null && disposition.Intended is not null &&
                     LedgerJoiner.Key(claim.Intended) == LedgerJoiner.Key(disposition.Intended))))
                both = both.Add(type.Weight);
            else
            {
                conflict = conflict.Add(type.Weight);
                examples.Add(new(type.Key, type.Weight, disposition.Category, claim.Text));
            }
        }
        examples.Sort((left, right) => right.Weight != left.Weight ? right.Weight.CompareTo(left.Weight)
            : string.CompareOrdinal(left.Key, right.Key));
        var summary = new StoreDispositionSummary(storeTally, ledgerTally, both, conflict, storeOnly,
            apply ? store.Count - matched : 0, byCategory, examples.Take(StoreDispositionSummary.MaxExamples).ToArray(),
            resolvedOpen, replaced, silent, clearedDup, clearedWeight, fills);
        if (storeOnly.Types == 0) return (join, summary);
        return (join with
        {
            Types = types, MissingTypes = missing, MissingWeight = missingW, DuplicateTypes = dup, DuplicateWeight = dupW,
            RevisionTypes = rev, RevisionWeight = revW, ConflictTypes = clash, ConflictWeight = clashW,
            ResidualTypes = residual, ResidualWeight = residualW,
        }, summary);
    }

    private static Claim? ClaimOf(JoinedType type)
    {
        var row = type.Row;
        // An incompatible-revision row is untrusted, even Expected or Lost, so a store row fills it.
        if (row is null || type.RevisionIncompatible) return null;
        if (row.State is LedgerState.Expected or LedgerState.Lost) return new("ledger-" + row.State.ToString().ToLowerInvariant(), null, null);
        if (CoverageStateRule.DispositionKind(row) is not { } kind) return null;
        return new(kind, kind, kind == "misspelling" ? CoverageStateRule.IntendedForm(row) : null);
    }
}
