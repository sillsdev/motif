namespace SIL.Motif.Contract.Responses;

/// <summary>A count of word types and the token weight they carry.</summary>
/// <param name="Types">How many word types.</param>
/// <param name="Tokens">The summed token weight of those types.</param>
public sealed record CoverageTally(long Types, long Tokens);

/// <summary>A tally split by whether a current check proves it or the ledger's prior state is carried unproved.</summary>
/// <param name="Current">Types proved by a check made at the ledger's grammar revision.</param>
/// <param name="Stale">Types whose ledger state is carried without such a check.</param>
public sealed record CoverageCurrentStale(CoverageTally Current, CoverageTally Stale);

/// <summary>What pins the measurement: change any of these and the numbers are not comparable.</summary>
/// <param name="GrammarRevision">The grammar revision the ledger and sidecar are pinned to.</param>
/// <param name="LedgerSha256">The digest of the ledger file as read.</param>
/// <param name="FrequencySha256">The digest of the frequency list as read.</param>
/// <param name="TokenizerId">The tokenizer the frequency list was made with.</param>
public sealed record CoverageStamp(string GrammarRevision, string LedgerSha256, string FrequencySha256, string TokenizerId);

/// <summary>One of the three Done tests.</summary>
/// <param name="Verdict"><c>pass</c>, <c>fail</c> or <c>unmeasured</c>.</param>
/// <param name="Reasons">Why it did not pass, as stable kebab-case names such as <c>recurring-open-current</c>; empty on a pass.</param>
/// <param name="Counts">The counts the verdict rests on, by camelCase name.</param>
public sealed record CoverageDoneTest(string Verdict, IReadOnlyList<string> Reasons, IReadOnlyDictionary<string, long> Counts);

/// <summary>The Done verdict and the three tests it is the conjunction of.</summary>
/// <param name="Verdict"><c>fail</c> when any test fails, <c>pass</c> when all pass, otherwise <c>unmeasured</c>.</param>
/// <param name="Accounted">Every recurring word type is accounted for: no recurring type is Open.</param>
/// <param name="Correct">A frozen audit sample shows the analyses are right.</param>
/// <param name="Stable">A protected set of readings is still produced.</param>
public sealed record CoverageDone(string Verdict, CoverageDoneTest Accounted, CoverageDoneTest Correct, CoverageDoneTest Stable);

/// <summary>The word types in each state, each split into current and stale.</summary>
public sealed record CoverageByState(
    CoverageCurrentStale Expected, CoverageCurrentStale Unverified, CoverageCurrentStale Disposition,
    CoverageCurrentStale Open, CoverageCurrentStale Lost);

/// <summary>The Open types of one mechanism, the ledger row's class.</summary>
/// <param name="Types">Open types of this mechanism.</param>
/// <param name="Current">Of those, proved Open by a current check.</param>
/// <param name="Stale">Of those, carried from the ledger without one.</param>
public sealed record CoverageMechanismCount(long Types, long Current, long Stale);

/// <summary>One recurring Open word type.</summary>
/// <param name="Word">The word type key.</param>
/// <param name="Weight">Its token weight.</param>
/// <param name="Mechanism">The mechanism, or <c>unclassified</c>.</param>
/// <param name="Stale">Whether the Open state is carried from the ledger rather than proved by a current check.</param>
public sealed record CoverageRankedOpen(string Word, long Weight, string Mechanism, bool Stale);

/// <summary>What is still Open.</summary>
/// <param name="Recurring">Open types seen at least twice.</param>
/// <param name="Singletons">Open types seen once.</param>
/// <param name="ByMechanism">Open types of the heaviest mechanisms by type count, keyed by name, at most 20; the rest are summed in <paramref name="OtherMechanisms"/>.</param>
/// <param name="Ranked">The recurring Open types by weight, heaviest first, cut to the request's limit.</param>
/// <param name="RankedTotal">How many recurring Open types there are before the cut.</param>
/// <param name="MechanismTotal">How many distinct mechanisms the Open types have before the cap on <paramref name="ByMechanism"/>.</param>
/// <param name="OtherMechanisms">The summed counts of the mechanisms beyond the cap; null when none were cut.</param>
public sealed record CoverageOpen(
    CoverageTally Recurring, CoverageTally Singletons, IReadOnlyDictionary<string, CoverageMechanismCount> ByMechanism,
    IReadOnlyList<CoverageRankedOpen> Ranked, int RankedTotal, int MechanismTotal, CoverageMechanismCount? OtherMechanisms);

/// <summary>
/// Word-type coverage over a lane ledger: where every type of the frequency list stands and whether the lane is Done.
/// This is not the <c>coverage</c> report kind, which is the share of a scope's words the parser analysed.
/// </summary>
/// <param name="SchemaVersion">Always 1.</param>
/// <param name="Stamp">What pins the measurement.</param>
/// <param name="Done">The Done verdict and its three tests.</param>
/// <param name="ByState">Types and tokens in each state.</param>
/// <param name="Open">What is still Open.</param>
/// <param name="TokenShare">The share of eligible tokens carried by Open singletons, from 0 to 1; null unless <c>--tokens</c> was given.</param>
public sealed record CoverageResponse(
    int SchemaVersion, CoverageStamp Stamp, CoverageDone Done, CoverageByState ByState, CoverageOpen Open, double? TokenShare);
