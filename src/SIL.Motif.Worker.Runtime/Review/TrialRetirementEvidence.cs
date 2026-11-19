using System.Text;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>
/// Retains, for the words the author waived, every Reading copy a case stopped emitting. The word-level waiver names
/// spellings; these rows name the canonical Readings and copy counts behind them, so a consumer can tell the planned
/// retirement from losing a different Reading or a different multiplicity.
/// </summary>
internal sealed class TrialRetirementEvidence(IEnumerable<string> waivedWords)
{
    private readonly HashSet<string> _waived = waivedWords.Select(Key).ToHashSet(StringComparer.Ordinal);
    private readonly HashSet<string> _measured = new(StringComparer.Ordinal);
    private readonly HashSet<string> _limitations = new(StringComparer.Ordinal);
    private readonly List<TrialLostReading> _rows = [];
    private long _cases;

    private static string Key(string word) => word.Normalize(NormalizationForm.FormC);

    /// <summary>Records the retirement of one planned case; cases of unwaived words are ignored.</summary>
    internal void Add(ReviewCaseDelta item, ParseWordEvidence? before, ParseWordEvidence? after)
    {
        var word = Key(item.Word);
        if (!_waived.Contains(word)) return;
        _measured.Add(word);
        _cases++;
        if (!Measured(item.Before) || !Measured(item.After)) { _limitations.Add("case-incomplete"); return; }
        var old = Readings(item.Before, before, item.Word);
        var current = Readings(item.After, after, item.Word);
        if (old.Any(reading => reading.Signature is null) || current.Any(reading => reading.Signature is null))
        {
            _limitations.Add("reading-signature-unavailable");
            return;
        }
        var remaining = current.Count(reading => TrialExportCapture.Acceptance(TrialExportCapture.MorphemeAcceptance(reading)) ==
            ReadingAcceptance.LexiconBacked);
        var left = current.GroupBy(reading => reading.Signature!.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var group in old.GroupBy(reading => reading.Signature!.Id, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var lost = group.Count() - left.GetValueOrDefault(group.Key);
            if (lost <= 0) continue;
            var sample = group.First();
            var confirmed = item.Assertions.Where(assertion => assertion.Eligible && !assertion.WholeWordNegative &&
                assertion.Morphology is not null && item.ReadingLosses.Any(loss => loss.AssertionIds.Contains(assertion.Id)) &&
                AnalysisMorphologyMatcher.Matches(sample, assertion.Morphology)).ToArray();
            _rows.Add(new TrialLostReading(item.Word, item.CaseKey, group.Key, confirmed.Any(a => a.Approved),
                confirmed.Any(a => !a.Approved), confirmed.Select(a => a.Id).Order(StringComparer.Ordinal).ToArray())
            { Signature = sample.Signature, Count = lost, RemainingReadingCount = remaining });
        }
    }

    internal IReadOnlyList<TrialLostReading> Rows => _rows.OrderBy(row => Key(row.Word), StringComparer.Ordinal)
        .ThenBy(row => row.CaseKey, StringComparer.Ordinal).ThenBy(row => row.ReadingIdentity, StringComparer.Ordinal).ToArray();

    /// <summary>Null when nothing was waived; otherwise complete only when every waived word was measured and every Reading identified.</summary>
    internal TrialRetirementAttestation? Attestation(string reportDigest)
    {
        if (_waived.Count == 0) return null;
        var limitations = new HashSet<string>(_limitations, StringComparer.Ordinal);
        if (_waived.Any(word => !_measured.Contains(word))) limitations.Add("waived-word-not-measured");
        return new TrialRetirementAttestation(reportDigest, limitations.Count == 0,
            _waived.Order(StringComparer.Ordinal).ToArray(), _cases, _rows.Sum(row => row.Count!.Value), _rows.Count,
            limitations.Order(StringComparer.Ordinal).ToArray());
    }

    private static bool Measured(ReviewCaseSideFacts side) => side.Completion == Completion.Complete || side.Parsed == false;

    private static IReadOnlyList<ParseAnalysis> Readings(ReviewCaseSideFacts side, ParseWordEvidence? evidence, string word) =>
        side.Completion == Completion.Complete ? ReviewCaseClassifier.Observe(evidence, word).Readings : [];
}
