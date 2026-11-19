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
            AddSecondReadingEvidence(item, readings);
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

    private void AddSecondReadingEvidence(ReviewCaseDelta item, IReadOnlyList<ReadingRow> readings)
    {
        var before = readings.Where(reading => reading.Side == Side.Before).ToArray();
        var after = readings.Where(reading => reading.Side == Side.After).ToArray();
        if (before.Length == 0 || after.Length < 2) return;

        var exactBaseline = before.FirstOrDefault(HasExactIdentity);
        var baseline = exactBaseline ?? before[0];
        if (before.Any(reading => HasExactIdentity(reading)
                ? reading.IdentityDigest != baseline.IdentityDigest
                : !SameMorphology(baseline, reading)))
            return;

        var exactAfter = after.Where(HasExactIdentity).DistinctBy(reading => reading.IdentityDigest).ToArray();
        var unsignedAfter = after.Where(reading => !HasExactIdentity(reading)).ToArray();
        // Same ordered morphology as a signed observation: a duplicate.
        var novelUnsigned = unsignedAfter.Where(reading => !exactAfter.Any(exact => SameMorphology(exact, reading))).ToArray();
        var possibleUnsignedSecond = novelUnsigned.Length > 0 &&
            (exactAfter.Length > 0 || novelUnsigned.Skip(1).Any(reading => !SameMorphology(novelUnsigned[0], reading)));
        if (exactAfter.Length < 2 && !possibleUnsignedSecond) return;

        var approved = item.Assertions.Where(assertion => assertion.Eligible && assertion.Approved && !assertion.WholeWordNegative)
            .Select(assertion => assertion.Id).ToHashSet(StringComparer.Ordinal);
        bool Unapproved(ReadingRow reading) => !reading.ExpectationIds.Any(approved.Contains);
        var newUnapprovedExact = exactAfter.Any(reading => reading.IdentityDigest != exactBaseline?.IdentityDigest && Unapproved(reading));
        if (newUnapprovedExact && exactAfter.Length > 1 && before.All(HasExactIdentity))
            _held.Add(item.Word);
        else if (newUnapprovedExact || novelUnsigned.Any(Unapproved))
            HoldIdentityUnavailable = true;
    }

    private static bool HasExactIdentity(ReadingRow reading) => reading.IdentityState == IdentityState.Exact;

    // Authoritative morph IDs can rule out equality when a Reading lacks a signature.
    private static bool SameMorphology(ReadingRow left, ReadingRow right) =>
        left.Morphs.Count == right.Morphs.Count && left.Morphs.Zip(right.Morphs).All(pair =>
            Guid.Parse(pair.First.FormId!) == Guid.Parse(pair.Second.FormId!) &&
            Guid.Parse(pair.First.MsaId!) == Guid.Parse(pair.Second.MsaId!) &&
            pair.First.InflTypeIds.Select(Guid.Parse).SequenceEqual(pair.Second.InflTypeIds.Select(Guid.Parse)));

    internal IReadOnlyList<string> HeldWords => _held.Order(StringComparer.Ordinal).ToArray();
    internal bool HoldIdentityUnavailable { get; private set; }

    internal TrialLosses Losses => new(_noParse.Order(StringComparer.Ordinal).ToArray(),
        _approved.Order(StringComparer.Ordinal).ToArray(),
        _readings.OrderBy(item => item.Word, StringComparer.Ordinal).ThenBy(item => item.ReadingIdentity, StringComparer.Ordinal).ToArray());
    internal IReadOnlyList<TrialStoppedWord> StoppedWords => _stopped;
    internal IReadOnlyList<string> BlockingStoppedWords => _blockingStopped.Order(StringComparer.Ordinal).ToArray();
}
