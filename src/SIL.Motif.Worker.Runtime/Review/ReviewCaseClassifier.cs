using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Analysis;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Compares one case against its frozen reading and whole-word assertions.</summary>
public static class ReviewCaseClassifier
{
    /// <summary>Returns only observed losses; an unfinished search cannot prove an absence.</summary>
    public static ReviewCaseDelta Compare(ReviewCaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.IdentityAvailable)
            return new ReviewCaseDelta(facts.CaseKey, facts.WordKey, facts.Word, facts.WritingSystemId,
                false, false, false, [], [], facts.Memberships, ["case-identity-unavailable"]);
        var before = Observe(facts.Before, facts.Word);
        var after = Observe(facts.After, facts.Word);
        var limitations = new List<string>();
        if (!before.Complete) limitations.Add("before-incomplete");
        if (!after.Complete) limitations.Add("after-incomplete");

        var losses = new Dictionary<string, (bool Approved, bool Reference, HashSet<string> Ids)>(StringComparer.Ordinal);
        foreach (var assertion in facts.Assertions.Where(a => a.Eligible && !a.WholeWordNegative))
        {
            if (!assertion.Approved && !facts.Memberships.Any(membership =>
                membership.Role is "reference" or "regression" && membership.ExpectationIds.Contains(assertion.Id)))
            {
                limitations.Add("reference-membership-unavailable");
                continue;
            }
            if (assertion.Morphology is null || !Exact(assertion.Morphology))
            {
                limitations.Add("expectation-identity-unavailable");
                continue;
            }
            if (!after.Complete || !before.Readings.Any(reading => AnalysisMorphologyMatcher.Matches(reading, assertion.Morphology)) ||
                after.Readings.Any(reading => AnalysisMorphologyMatcher.Matches(reading, assertion.Morphology)))
                continue;
            if (!losses.TryGetValue(assertion.ReadingIdentity, out var loss))
                loss = (false, false, new HashSet<string>(StringComparer.Ordinal));
            loss.Approved |= assertion.Approved;
            loss.Reference |= !assertion.Approved;
            loss.Ids.Add(assertion.Id);
            losses[assertion.ReadingIdentity] = loss;
        }

        var lost = losses.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ReviewReadingLoss(pair.Key, pair.Value.Approved, pair.Value.Reference,
                pair.Value.Ids.Order(StringComparer.Ordinal).ToArray())).ToArray();
        var noParse = before.Readings.Count > 0 && after.Complete && after.Readings.Count == 0;
        var approvedLoss = lost.Any(item => item.Approved);
        var negativeIds = facts.Assertions.Where(item => item.Eligible && item.WholeWordNegative)
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var negativeMembership = facts.Memberships.Any(item => item.Role == "negative" &&
            item.ExpectationIds.Any(negativeIds.Contains));
        var negative = negativeMembership && before.Complete && before.Readings.Count == 0 && after.Readings.Count > 0;
        var flags = new List<ReviewFlagId>();
        if (noParse || approvedLoss) flags.Add(ReviewFlagId.NewWordFailures);
        if (lost.Length > 0) flags.Add(ReviewFlagId.ConfirmedReadingsLost);
        if (negative) flags.Add(ReviewFlagId.KnownNonwordsNowAccepted);
        return new ReviewCaseDelta(facts.CaseKey, facts.WordKey, facts.Word, facts.WritingSystemId,
            noParse, approvedLoss, negative, lost, flags, facts.Memberships,
            limitations.Distinct(StringComparer.Ordinal).ToArray())
        {
            Before = SideFacts(facts.Before, before, facts.Assertions),
            After = SideFacts(facts.After, after, facts.Assertions),
            Assertions = facts.Assertions,
            AssertionsCaptured = true,
            Changed = CompareReadings(before, after),
        };
    }

    private static bool? CompareReadings((IReadOnlyList<ParseAnalysis> Readings, bool Complete) before,
        (IReadOnlyList<ParseAnalysis> Readings, bool Complete) after)
    {
        if (!before.Complete || !after.Complete) return null;
        static string Identity(ParseAnalysis reading) => JsonSerializer.Serialize(reading.Morphs.Select(morph => new
        {
            Form = Guid.Parse(morph.Form!).ToString("D"), Msa = Guid.Parse(morph.Msa!).ToString("D"),
            InflType = morph.InflType is null ? null : Guid.Parse(morph.InflType).ToString("D"),
            Guess = morph.GuessedString?.Normalize(NormalizationForm.FormD),
        }));
        var previous = before.Readings.Select(Identity).ToHashSet(StringComparer.Ordinal);
        return !previous.SetEquals(after.Readings.Select(Identity));
    }

    private static ReviewCaseSideFacts SideFacts(ParseWordEvidence? evidence,
        (IReadOnlyList<ParseAnalysis> Readings, bool Complete) observed, IReadOnlyList<ReviewAssertion> assertions)
    {
        var matched = new List<string>();
        var missing = new List<string>();
        var unknown = new List<string>();
        foreach (var assertion in assertions.Where(a => a.Eligible && !a.WholeWordNegative))
        {
            if (assertion.Morphology is null || !Exact(assertion.Morphology)) { unknown.Add(assertion.Id); continue; }
            if (observed.Readings.Any(reading => AnalysisMorphologyMatcher.Matches(reading, assertion.Morphology)))
                matched.Add(assertion.Id);
            else if (observed.Complete) missing.Add(assertion.Id);
            else unknown.Add(assertion.Id);
        }
        var reasons = new List<string>();
        if (evidence?.Capped == true) reasons.Add("work-limit");
        if (evidence?.TimedOut == true) reasons.Add("time-limit");
        if (evidence is not null && !observed.Complete && reasons.Count == 0) reasons.Add("invalid-evidence");
        var completion = evidence is null ? Completion.NotMeasured : observed.Complete ? Completion.Complete :
            evidence.Capped || evidence.TimedOut ? Completion.Stopped : Completion.Unavailable;
        bool? parsed = observed.Readings.Count > 0 ? true : observed.Complete ? false : null;
        return new ReviewCaseSideFacts(completion, parsed, reasons, matched, missing, unknown);
    }

    private static (IReadOnlyList<ParseAnalysis> Readings, bool Complete) Observe(ParseWordEvidence? evidence, string word)
    {
        if (evidence is null || evidence.Schema != "fieldworks-parse-analysis/v1" || evidence.ElapsedMs < 0 ||
            evidence.Attempts is < 0 || evidence.Word is null || word is null || evidence.Analyses is null || evidence.Unavailable is null ||
            evidence.InvalidShape || evidence.Refusal is not null || evidence.Unavailable.Any(string.IsNullOrWhiteSpace))
            return ([], false);
        try
        {
            if (!string.Equals(word.Normalize(NormalizationForm.FormD), evidence.Word.Normalize(NormalizationForm.FormD), StringComparison.Ordinal))
                return ([], false);
            if (evidence.Analyses.Any(analysis => !Exact(analysis))) return ([], false);
            foreach (var morph in evidence.Analyses.SelectMany(analysis => analysis.Morphs))
                if (morph.GuessedString is not null) _ = morph.GuessedString.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException) { return ([], false); }
        var complete = !evidence.Capped && !evidence.TimedOut && evidence.Unavailable.Count == 0;
        return (evidence.Analyses, complete);
    }

    private static bool Exact(ParseAnalysis? analysis) => analysis?.Morphs is { Count: > 0 } &&
        analysis.Morphs.All(m => m is not null && Guid.TryParse(m.Form, out var form) && form != Guid.Empty &&
            Guid.TryParse(m.Msa, out var msa) && msa != Guid.Empty &&
            (m.InflType is null || Guid.TryParse(m.InflType, out var infl) && infl != Guid.Empty));

    internal static bool Exact(ApprovedMorphology morphology) => morphology.Morphs is { Count: > 0 } &&
        morphology.Morphs.All(m => m is not null && Guid.TryParse(m.Form, out var form) && form != Guid.Empty &&
            Guid.TryParse(m.Msa, out var msa) && msa != Guid.Empty && ValidForms(m.Forms) &&
            (m.InflType is null || Guid.TryParse(m.InflType, out var infl) && infl != Guid.Empty));
    private static bool ValidForms(IReadOnlyList<string>? forms)
    {
        if (forms is null || forms.Any(form => form is null)) return false;
        try
        {
            foreach (var form in forms) _ = form.Normalize(NormalizationForm.FormD);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
