namespace SIL.Motif.Worker.Corpus.Ledger;

/// <summary>A count of word types with their summed token weight.</summary>
internal readonly record struct Tally(long Types, long Tokens)
{
    public Tally Add(long weight) => new(Types + 1, Tokens + weight);
}

/// <summary>A tally split by whether a current check proves it or the prior ledger state is kept stale.</summary>
internal readonly record struct CurrentStale(Tally Current, Tally Stale)
{
    public Tally Total => new(Current.Types + Stale.Types, Current.Tokens + Stale.Tokens);

    public CurrentStale Add(long weight, StateFreshness freshness) => freshness == StateFreshness.Current
        ? this with { Current = Current.Add(weight) } : this with { Stale = Stale.Add(weight) };
}

internal sealed record CoverageRow(JoinedType Type, CoverageDecision Decision, TypeCheck? Check);

internal sealed record RankedOpen(string Key, long Weight, bool Stale, OpenKind Kind, string Mechanism);

internal sealed record LedgerStamp(
    string LedgerSha256, string SidecarSha256, string FrequencySha256, string GrammarRevision, string TokenizerId,
    string TokenizerSha256, string Fold, string Normalization, IReadOnlyList<string> SourceIds, string ScopeFilter);

internal sealed record LedgerCoverageReport(
    LedgerStamp Stamp, LedgerJoin Join, IReadOnlyList<CoverageRow> Rows,
    IReadOnlyDictionary<ReportState, CurrentStale> ByState, Tally NoState,
    IReadOnlyDictionary<string, CurrentStale> DispositionByKind, IReadOnlyDictionary<string, CurrentStale> OpenByMechanism,
    CurrentStale NoAnalysis, CurrentStale WrongOnly, CurrentStale Lost, Tally Reach,
    Tally RecurringOpenCurrent, Tally RecurringOpenStale, Tally OpenSingletons, double OpenSingletonTokenShare,
    IReadOnlyList<RankedOpen> RankedRecurringOpen, Tally UnresolvedRecurring, Tally RecurringLostStale,
    Tally RecurringUnsupportedDisposition, bool ExactTotals)
{
    public DoneResult Done { get; init; } = DoneResult.Unmeasured;
}

internal static class LedgerCoverage
{
    public static LedgerCoverageReport Build(LedgerCoverageInputs inputs)
    {
        var join = LedgerJoiner.Join(inputs.Ledger, inputs.Sidecar, inputs.Frequency, inputs.DeclaredFrequencySha256);
        foreach (var supplied in inputs.Checks?.Values ?? [])
            if (supplied is { Completeness: CheckCompleteness.Complete, Comparable: true, ProducedReadings: null })
                throw new LedgerRefusalException(LedgerRefusalCode.CheckMalformed,
                    "A complete comparable check must carry an explicit produced Reading set.");
        var rows = new List<CoverageRow>(join.Types.Count);
        var byState = Enum.GetValues<ReportState>().ToDictionary(state => state, _ => default(CurrentStale));
        var dispositions = new SortedDictionary<string, CurrentStale>(StringComparer.Ordinal);
        var mechanisms = new SortedDictionary<string, CurrentStale>(StringComparer.Ordinal);
        CurrentStale noAnalysis = default, wrongOnly = default, lost = default;
        Tally staleLost = default, unsupported = default;
        Tally noState = default, reach = default, curOpen = default, staleOpen = default, singletons = default, unresolved = default;
        var ranked = new List<RankedOpen>();
        foreach (var type in join.Types)
        {
            var effective = join.Blocked ? type with { Row = null } : type;
            TypeCheck? check = null;
            inputs.Checks?.TryGetValue(type.Key, out check);
            var decision = CoverageStateRule.Decide(effective, check);
            rows.Add(new(type, decision, check));
            if (decision.PresenceProved) reach = reach.Add(type.Weight);
            var recurring = type.Weight >= 2;
            if (decision.State is not { } state)
            {
                noState = noState.Add(type.Weight);
                if (recurring) unresolved = unresolved.Add(type.Weight);
                continue;
            }
            byState[state] = byState[state].Add(type.Weight, decision.Freshness);
            if (recurring && decision.Freshness != StateFreshness.Current) unresolved = unresolved.Add(type.Weight);
            if (decision.DispositionKind is { } kind)
                dispositions[kind] = dispositions.GetValueOrDefault(kind).Add(type.Weight, decision.Freshness);
            if (!decision.CountsAsOpen) continue;
            var mechanism = decision.Mechanism ?? CoverageStateRule.Unclassified;
            mechanisms[mechanism] = mechanisms.GetValueOrDefault(mechanism).Add(type.Weight, decision.Freshness);
            if (decision.Open == OpenKind.NoAnalysis) noAnalysis = noAnalysis.Add(type.Weight, decision.Freshness);
            if (decision.Open == OpenKind.WrongOnly) wrongOnly = wrongOnly.Add(type.Weight, decision.Freshness);
            if (state == ReportState.Lost) lost = lost.Add(type.Weight, decision.Freshness);
            if (!recurring) { singletons = singletons.Add(type.Weight); continue; }
            var stale = decision.Freshness != StateFreshness.Current;
            if (stale) staleOpen = staleOpen.Add(type.Weight); else curOpen = curOpen.Add(type.Weight);
            ranked.Add(new(type.Key, type.Weight, stale, decision.Open, mechanism));
            if (stale && state == ReportState.Lost) staleLost = staleLost.Add(type.Weight);
            if (decision.Open == OpenKind.UnsupportedDisposition) unsupported = unsupported.Add(type.Weight);
        }
        ranked.Sort((left, right) => right.Weight != left.Weight ? right.Weight.CompareTo(left.Weight)
            : string.CompareOrdinal(left.Key, right.Key));
        var exact = !join.Blocked && join.ResidualTypes == 0 && noState.Types == 0 &&
            byState.Values.All(item => item.Stale.Types == 0);
        var share = join.EligibleTokens == 0 ? 0d : (double)singletons.Tokens / join.EligibleTokens;
        var sidecar = inputs.Sidecar;
        var stamp = new LedgerStamp(inputs.Ledger.Digest, sidecar.Digest, inputs.Frequency.Digest, sidecar.GrammarRevision,
            sidecar.TokenizerId, sidecar.TokenizerSha256, sidecar.Fold, sidecar.Normalization, sidecar.SourceIds, sidecar.ScopeFilter);
        var report = new LedgerCoverageReport(stamp, join, rows, byState, noState, dispositions, mechanisms, noAnalysis, wrongOnly,
            lost, reach, curOpen, staleOpen, singletons, share, ranked, unresolved, staleLost, unsupported, exact);
        return report with { Done = DoneVerdict.Compute(report, inputs) };
    }
}

internal sealed record LedgerCoverageInputs(
    LedgerFile Ledger, LedgerSidecar Sidecar, PinnedFrequencyList Frequency,
    IReadOnlyDictionary<string, TypeCheck>? Checks = null, string? DeclaredFrequencySha256 = null,
    string? AuditSampleDirectory = null, string? ProtectedSetPath = null);
