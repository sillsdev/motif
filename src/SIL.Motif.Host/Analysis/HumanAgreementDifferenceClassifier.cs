using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Analysis;

/// <summary>Compares parser checkpoints against one immutable capture of human decisions.</summary>
public static class HumanAgreementDifferenceClassifier
{
    /// <summary>Computes paired observations and advisory buckets using the same decisions at both checkpoints.</summary>
    public static ProposalDifferenceCaseFacts Compare(ParseWordEvidence? beforeEvidence, ParseWordEvidence? afterEvidence,
        CompiledHumanDecisions decisions, ProposalDifferencePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        policy ??= new();
        var before = decisions.Classify(beforeEvidence);
        var after = decisions.Classify(afterEvidence);
        var beforeSearch = HumanAgreementClassifier.Validate(beforeEvidence, false);
        var afterSearch = HumanAgreementClassifier.Validate(afterEvidence, false);
        var beforeIdentities = Identities(beforeSearch);
        var afterIdentities = Identities(afterSearch);
        var complete = beforeSearch.CanProveMissing && afterSearch.CanProveMissing;
        var gained = complete ? Except(afterIdentities, beforeIdentities) : [];
        var lost = complete ? Except(beforeIdentities, afterIdentities) : [];
        var changes = new List<ProposalDifferenceChange>();
        var buckets = new List<ProposalDifferenceBucketMembership>();

        if (complete)
        {
            if (beforeIdentities.Count == 0 && afterIdentities.Count > 0) changes.Add(ProposalDifferenceChange.Gained);
            else if (beforeIdentities.Count > 0 && afterIdentities.Count == 0) changes.Add(ProposalDifferenceChange.Lost);
            else if (gained.Count > 0 || lost.Count > 0) changes.Add(ProposalDifferenceChange.Changed);
            if (afterIdentities.Count > 1 && afterIdentities.Count > beforeIdentities.Count)
                changes.Add(ProposalDifferenceChange.AmbiguityUp);
            if (gained.Count > 0 || lost.Count > 0) Add(ProposalDifferenceBucket.AnalysisChanged);
            if ((long)afterIdentities.Count - beforeIdentities.Count >= policy.AbsoluteIncrease &&
                (long)afterIdentities.Count >= (long)policy.Ratio * Math.Max(1, beforeIdentities.Count))
                Add(ProposalDifferenceBucket.AmbiguityBlowup);
        }

        var quality = before.Conflicts.Count == 0 && after.Conflicts.Count == 0;
        var beforeComplete = Complete(before);
        var afterComplete = Complete(after);
        var afterObserved = afterSearch.CanClassifyMorphology;
        if (quality)
        {
            var lostApproved = before.MatchedApprovedAnalysisIds.Intersect(after.ProvenMissingApprovedAnalysisIds,
                StringComparer.Ordinal).ToArray();
            if (beforeComplete && afterComplete && lostApproved.Length > 0)
                Add(ProposalDifferenceBucket.Regression, lostApproved);

            if (beforeComplete && afterComplete && before.ApprovedState == HumanAgreementApprovalState.ApprovedUnique &&
                after.ApprovedState == HumanAgreementApprovalState.ApprovedWithAlternatives && gained.Count > 0)
                Add(ProposalDifferenceBucket.NewCompetingParse, before.MatchedApprovedAnalysisIds);

            if (SpellingComplete(before) && before.ProducedIdentityCount == 0 &&
                before.SpellingAcceptance == HumanAgreementSpellingAcceptance.RejectedMisspelling &&
                after.SpellingAcceptance == HumanAgreementSpellingAcceptance.AcceptedMisspelling)
                Add(ProposalDifferenceBucket.Overgeneration, observed: !SpellingComplete(after));

            var newDisapproved = after.MatchedDisapprovedAnalysisIds.Except(before.MatchedDisapprovedAnalysisIds,
                StringComparer.Ordinal).ToArray();
            var newUnapprovedParse = beforeComplete && afterComplete &&
                before.ApprovedState == HumanAgreementApprovalState.NoParse &&
                after.ApprovedState == HumanAgreementApprovalState.NoApprovedMatch;
            if (beforeComplete && afterObserved && (newDisapproved.Length > 0 || newUnapprovedParse))
                Add(ProposalDifferenceBucket.NewWrongReading, newDisapproved, !afterComplete);

            var gainedApproved = after.MatchedApprovedAnalysisIds.Intersect(before.ProvenMissingApprovedAnalysisIds,
                StringComparer.Ordinal).ToArray();
            if (beforeComplete && afterObserved && gainedApproved.Length > 0)
                Add(ProposalDifferenceBucket.ImprovedApprovedMatch, gainedApproved, !afterComplete);

            if (beforeComplete && afterComplete &&
                before.ApprovedState == HumanAgreementApprovalState.ApprovedWithAlternatives &&
                after.ApprovedState == HumanAgreementApprovalState.ApprovedUnique && lostApproved.Length == 0)
                Add(ProposalDifferenceBucket.ImprovedUnique, after.MatchedApprovedAnalysisIds);

            var rejectedDisapproved = before.MatchedDisapprovedAnalysisIds.Except(after.MatchedDisapprovedAnalysisIds,
                StringComparer.Ordinal).ToArray();
            var rejectedSpelling = before.SpellingAcceptance == HumanAgreementSpellingAcceptance.AcceptedMisspelling &&
                after.SpellingAcceptance == HumanAgreementSpellingAcceptance.RejectedMisspelling;
            if (beforeSearch.CanClassifyMorphology &&
                ((afterComplete && rejectedDisapproved.Length > 0) || (SpellingComplete(after) && rejectedSpelling)))
                Add(ProposalDifferenceBucket.ImprovedNegativeRejection, rejectedDisapproved);

            if (beforeComplete && afterComplete && before.ApprovedExpectedMorphologyCount == 0 &&
                before.ProducedIdentityCount > 0 && after.ProducedIdentityCount == 0)
                Add(ProposalDifferenceBucket.LostParse);
        }

        return new ProposalDifferenceCaseFacts(policy, before, after, complete,
            HumanAgreementClassifier.Freeze(changes), HumanAgreementClassifier.Freeze(beforeIdentities.Values),
            HumanAgreementClassifier.Freeze(afterIdentities.Values), gained, lost,
            HumanAgreementClassifier.Freeze(buckets.OrderBy(item => item.Bucket)));

        void Add(ProposalDifferenceBucket bucket, IEnumerable<string>? ids = null, bool observed = false) =>
            buckets.Add(new(bucket, HumanAgreementClassifier.Freeze((ids ?? []).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)), observed));
    }

    private static bool Complete(HumanAgreementFacts facts) =>
        facts.Availability == HumanAgreementEvidenceAvailability.Available &&
        facts.Completion == HumanAgreementSearchCompletion.Complete;

    // An undecodable expected morphology leaves the produced parses and the spelling status intact.
    private static bool SpellingComplete(HumanAgreementFacts facts) =>
        facts.Completion == HumanAgreementSearchCompletion.Complete && (facts.Availability ==
            HumanAgreementEvidenceAvailability.Available || (facts.Availability ==
            HumanAgreementEvidenceAvailability.PartiallyAvailable &&
            !facts.Limitations.Contains(HumanAgreementLimitation.ProjectionUnavailable)));

    private static SortedDictionary<string, HumanAgreementProducedIdentity> Identities(
        HumanAgreementClassifier.ValidatedEvidence evidence)
    {
        var identities = new SortedDictionary<string, HumanAgreementProducedIdentity>(StringComparer.Ordinal);
        if (!evidence.CanClassifyMorphology) return identities;
        foreach (var analysis in evidence.Evidence!.Analyses)
        {
            var key = HumanAgreementClassifier.ParserIdentityKey(analysis);
            if (identities.ContainsKey(key)) continue;
            var identity = new HumanAgreementProducedIdentity(HumanAgreementClassifier.Freeze(analysis.Morphs.Select(morph =>
                new HumanAgreementProducedMorph(morph.Form!, morph.Msa!, morph.InflType,
                    morph.GuessedString is null ? null : HumanAgreementClassifier.Nfd(morph.GuessedString)))));
            identities.Add(key, identity);
        }
        return identities;
    }

    private static IReadOnlyList<HumanAgreementProducedIdentity> Except(
        SortedDictionary<string, HumanAgreementProducedIdentity> left,
        SortedDictionary<string, HumanAgreementProducedIdentity> right) =>
        HumanAgreementClassifier.Freeze(left.Where(pair => !right.ContainsKey(pair.Key)).Select(pair => pair.Value));
}
