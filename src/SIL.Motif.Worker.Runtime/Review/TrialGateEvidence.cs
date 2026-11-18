using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Retains named gate facts without keeping every case's full parse and review detail in memory.</summary>
internal sealed class TrialGateEvidence
{
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly HashSet<string> _noParse = new(StringComparer.Ordinal);
    private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
    private readonly List<TrialLostReading> _readings = [];
    private readonly List<TrialStoppedWord> _stopped = [];
    private readonly HashSet<string> _blockingStopped = new(StringComparer.Ordinal);

    internal void Add(ReviewCaseDelta item)
    {
        if (item.Before.Completion == Completion.Complete && item.After.Completion == Completion.Complete &&
            (item.Memberships.Any(member => member.Role == "sampled-tokens") ||
             item.Assertions.Any(assertion => assertion.Eligible && assertion.Approved && !assertion.WholeWordNegative)) &&
            item.Readings is { } readings)
        {
            var before = readings.Where(reading => reading.Side == Side.Before).Select(reading => reading.IdentityDigest)
                .ToHashSet(StringComparer.Ordinal);
            var after = readings.Where(reading => reading.Side == Side.After).DistinctBy(reading => reading.IdentityDigest).ToArray();
            var approved = item.Assertions.Where(assertion => assertion.Eligible && assertion.Approved && !assertion.WholeWordNegative)
                .Select(assertion => assertion.Id).ToHashSet(StringComparer.Ordinal);
            if (before.Count == 1 && after.Length > 1 && after.Any(reading => !before.Contains(reading.IdentityDigest) &&
                    !reading.ExpectationIds.Any(approved.Contains)))
                _held.Add(item.Word);
        }
        if (item.NewNoParse) _noParse.Add(item.Word);
        if (item.NewApprovedLoss) _approved.Add(item.Word);
        foreach (var reading in item.ReadingLosses)
            _readings.Add(new TrialLostReading(item.Word, item.CaseKey, reading.ReadingIdentity,
                reading.Approved, reading.ReferenceConfirmed, reading.AssertionIds));
        foreach (var (side, facts) in new[] { (Side.Before, item.Before), (Side.After, item.After) })
            if (facts.Completion is Completion.Stopped or Completion.Unavailable)
                _stopped.Add(new TrialStoppedWord(item.CaseKey, item.Word, side, facts.StopReasons, facts.StopDetails ?? []) { Outcome = facts.Outcome });
        if (item.Before.Parsed == true && !item.NewNoParse &&
            item.After.Completion is Completion.Stopped or Completion.Unavailable)
            _blockingStopped.Add(item.Word);
    }

    internal IReadOnlyList<string> HeldWords => _held.Order(StringComparer.Ordinal).ToArray();

    internal TrialLosses Losses => new(_noParse.Order(StringComparer.Ordinal).ToArray(),
        _approved.Order(StringComparer.Ordinal).ToArray(),
        _readings.OrderBy(item => item.Word, StringComparer.Ordinal).ThenBy(item => item.ReadingIdentity, StringComparer.Ordinal).ToArray());
    internal IReadOnlyList<TrialStoppedWord> StoppedWords => _stopped;
    internal IReadOnlyList<string> BlockingStoppedWords => _blockingStopped.Order(StringComparer.Ordinal).ToArray();
}
