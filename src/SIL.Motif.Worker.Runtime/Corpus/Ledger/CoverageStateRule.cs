namespace SIL.Motif.Worker.Corpus.Ledger;

internal enum CheckCompleteness { Unmeasured, Partial, Complete }

/// <summary>Why a check cannot be compared with the report: it was made at another grammar pin.</summary>
internal enum CheckIncomparable { None, RevisionIncompatible }

/// <summary>
/// A saved check of one type against the report pin. <see cref="ProducedReadings"/> holds canonical
/// Reading projections when the producer established Reading identity; an explicit empty set proves zero
/// Readings. Null means identity was not established, which a complete comparable check may not claim.
/// A comparable check carries the grammar pin it was made at in <see cref="GrammarPin"/>. A check that is not
/// comparable says why in <see cref="Incomparable"/>.
/// </summary>
internal sealed record TypeCheck(
    CheckCompleteness Completeness, bool Comparable, bool ProvedPresent, bool AllProducedWrong = false,
    IReadOnlyCollection<string>? ProducedReadings = null, string? GrammarPin = null,
    CheckIncomparable Incomparable = CheckIncomparable.None)
{
    public static TypeCheck Unmeasured { get; } = new(CheckCompleteness.Unmeasured, false, false);
}

internal enum ReportState { Expected, Unverified, Disposition, Open, Lost }

internal enum StateFreshness { Current, Stale, Unavailable }

internal enum OpenKind { None, NoAnalysis, WrongOnly, Carried, Lost, UnsupportedDisposition }

internal sealed record CoverageDecision(
    ReportState? State, StateFreshness Freshness, int Rule, OpenKind Open, string? Mechanism, string? DispositionKind,
    IReadOnlyList<string> MissingReadings, bool ComparisonUnmeasured, bool PresenceProved, IReadOnlyList<string> Reasons)
{
    public bool CountsAsOpen => State is ReportState.Open or ReportState.Lost;
}

/// <summary>The first-applicable-row state rule: only a complete comparable check proves absence.</summary>
internal static class CoverageStateRule
{
    public const string Unclassified = "unclassified";
    private static readonly string[] SameNamed = ["name", "loan", "other-language", "abbreviation", "numeral", "non-word"];
    private const string IntendedPrefix = "intended=";

    public static string? DispositionKind(LedgerRow row)
    {
        if (row.State != LedgerState.Disposition || row.Evidence is null) return null;
        var kind = row.Class;
        if (kind is null) return null;
        if (SameNamed.Contains(kind, StringComparer.Ordinal)) return kind;
        if (kind == "artifact") return "non-word";
        if (kind != "misspelling" || !row.Evidence.StartsWith(IntendedPrefix, StringComparison.Ordinal)) return null;
        var form = row.Evidence[IntendedPrefix.Length..];
        var bar = form.IndexOf(" | ", StringComparison.Ordinal);
        if (bar >= 0) form = form[..bar];
        return form.Length > 0 && form.Trim() == form ? "misspelling" : null;
    }

    public static CoverageDecision Decide(JoinedType type, TypeCheck? check)
    {
        var row = type.Row;
        check ??= TypeCheck.Unmeasured;
        var usable = check.Comparable && check.Completeness != CheckCompleteness.Unmeasured;
        var complete = usable && check.Completeness == CheckCompleteness.Complete;
        var present = usable && check.ProvedPresent;
        var reasons = new List<string>();
        if (type.Missing) reasons.Add("ledger-row-missing");
        if (type.DuplicateRows.Count > 0) reasons.Add("duplicate-normalized-key");
        if (type.RevisionIncompatible) reasons.Add("incompatible-revision");
        if (type.ConflictingTokens is not null) reasons.Add("conflicting-weight");
        if (check.Completeness != CheckCompleteness.Unmeasured && !check.Comparable) reasons.Add(check.Incomparable == CheckIncomparable.RevisionIncompatible
            ? "check-revision-incompatible" : "incompatible-check");
        if (check.Completeness == CheckCompleteness.Unmeasured) reasons.Add("check-unmeasured");

        var protectedReadings = row?.ExpectedReadings.Select(item => item.Canonical).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var claimsReadings = row is { State: LedgerState.Expected or LedgerState.Lost };
        if (claimsReadings && protectedReadings.Length == 0) reasons.Add("expected-readings-empty");
        // Only an explicit produced set proves absence; a null set is refused upstream for a complete check.
        var produced = check.ProducedReadings;
        var identity = complete && produced is not null;
        var missing = identity ? protectedReadings.Where(reading => !produced!.Contains(reading)).ToArray() : [];
        var comparisonUnmeasured = protectedReadings.Length > 0 && !identity;
        var dispositionKind = row is null ? null : DispositionKind(row);
        var mechanism = row?.State is LedgerState.Open or LedgerState.Lost ? row.Class ?? Unclassified : Unclassified;

        CoverageDecision Make(int rule, ReportState? state, StateFreshness freshness, OpenKind open = OpenKind.None,
            string? kind = null, string[]? lost = null) => new(state, freshness, rule, open,
                state is ReportState.Open or ReportState.Lost ? mechanism : null, kind, lost ?? [],
                comparisonUnmeasured, present, reasons);

        if (missing.Length > 0) return Make(1, ReportState.Lost, StateFreshness.Current, OpenKind.Lost, lost: missing);
        var recovered = identity && protectedReadings.Length > 0;
        if (row is { State: LedgerState.Lost } && protectedReadings.Length > 0 && !recovered)
            return Make(2, ReportState.Lost, StateFreshness.Stale, OpenKind.Lost);
        if ((complete || present) && dispositionKind is not null)
            return Make(3, ReportState.Disposition, StateFreshness.Current, kind: dispositionKind);
        if (present && protectedReadings.Length > 0) return Make(4, ReportState.Expected, StateFreshness.Current);
        if (complete && present && check.AllProducedWrong) return Make(5, ReportState.Open, StateFreshness.Current, OpenKind.WrongOnly);
        if (present) return Make(6, ReportState.Unverified, StateFreshness.Current);
        if (complete) return Make(7, ReportState.Open, StateFreshness.Current, OpenKind.NoAnalysis);

        if (row is null) return Make(8, null, StateFreshness.Unavailable);
        if (row.State == LedgerState.Disposition && dispositionKind is null)
        {
            reasons.Add("unsupported-disposition");
            return Make(8, ReportState.Open, StateFreshness.Stale, OpenKind.UnsupportedDisposition);
        }
        var prior = row.State switch
        {
            LedgerState.Expected => ReportState.Expected, LedgerState.Unverified => ReportState.Unverified,
            LedgerState.Disposition => ReportState.Disposition, LedgerState.Open => ReportState.Open, _ => ReportState.Lost,
        };
        return Make(8, prior, StateFreshness.Stale, prior == ReportState.Open ? OpenKind.Carried : prior == ReportState.Lost ? OpenKind.Lost : OpenKind.None,
            prior == ReportState.Disposition ? dispositionKind : null);
    }
}
