using System.Text.Json;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands.Parsimony;

public sealed class RetirementExpectationTranslationTests
{
    private static readonly string Digest = "sha256:" + new string('a', 64);
    private static readonly Guid ProjectGuid = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid WordformGuid = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnalysisGuid = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid BundleGuid = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid OtherAnalysisGuid = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid OtherBundleGuid = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid RetiredGuid = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid ReplacementGuid = Guid.Parse("00000000-0000-0000-0000-000000000007");
    private static readonly Guid OtherFormGuid = Guid.Parse("00000000-0000-0000-0000-000000000008");
    private static readonly Guid MsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000009");
    private static readonly Guid OtherMsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid NegativeCaseGuid = Guid.Parse("00000000-0000-0000-0000-000000000013");
    private static readonly Guid NegativeRevisionGuid = Guid.Parse("00000000-0000-0000-0000-000000000014");
    private static readonly Guid NegativeRecordGuid = Guid.Parse("00000000-0000-0000-0000-000000000016");
    private static readonly BaselineToken Baseline = new(ProjectGuid.ToString("D"), Digest, "projection/v1",
        "2026-10-07T12:00:00Z", Digest);
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TranslatesOnlyTheMappedBundleRoleBackedByTheBoundDryRunEffects()
    {
        var original = Frozen();
        var intent = Intent();
        var proposal = Proposal();
        var dryRun = DryRun(proposal, Effects());

        var translated = RetirementExpectationTranslator.Translate(original, Baseline, intent, proposal, dryRun);

        Assert.Equal(Id(RetiredGuid), original.Cases[0].Readings[0].Morphs[0].Form);
        Assert.Equal(Id(ReplacementGuid), translated.Translated.Cases[0].Readings[0].Morphs[0].Form);
        Assert.Equal(Id(OtherFormGuid), translated.Translated.Cases[0].Readings[1].Morphs[0].Form);
        Assert.False(translated.Translated.Cases[0].InSelection);
        Assert.Equal("nj", Assert.Single(translated.BundleTextEffects).Before["mgz"]);
        Assert.Equal("n", Assert.Single(translated.BundleTextEffects).After["mgz"]);
        Assert.Equal(dryRun.EffectDigest, translated.DryRunEffectDigest);
    }

    [Fact]
    public void RefusesSourceProposalAndFormEffectBindingMismatches()
    {
        var original = Frozen();
        var proposal = Proposal();
        var dryRun = DryRun(proposal, Effects());

        AssertRefusal(RetirementExpectationTranslationRefusalKind.BindingMismatch, () =>
            RetirementExpectationTranslator.Translate(original, OtherBaseline(), Intent(), proposal, dryRun));
        AssertRefusal(RetirementExpectationTranslationRefusalKind.BindingMismatch, () =>
            RetirementExpectationTranslator.Translate(original, Baseline, Intent(), proposal,
                DryRun(Proposal(OtherFormGuid), Effects(OtherFormGuid))));
        AssertRefusal(RetirementExpectationTranslationRefusalKind.BindingMismatch, () =>
            RetirementExpectationTranslator.Translate(original, Baseline, Intent(OtherFormGuid), proposal, dryRun));
        AssertRefusal(RetirementExpectationTranslationRefusalKind.FormCopyMismatch, () =>
            RetirementExpectationTranslator.Translate(original, Baseline, Intent(), proposal,
                DryRun(proposal, [Effects()[0]])));
        AssertRefusal(RetirementExpectationTranslationRefusalKind.BindingMismatch, () =>
            RetirementExpectationTranslator.Translate(original with
                { Cases = [original.Cases[0] with { Surface = "other" }] }, Baseline, Intent(), proposal, dryRun));
    }

    [Fact]
    public void RefusesManyToOneAndApprovedDisapprovedTranslationCollisions()
    {
        var second = Reading(OtherAnalysisGuid, OtherBundleGuid, ReplacementGuid, MsaGuid, "n");
        var collision = FrozenWith(Reading(AnalysisGuid, BundleGuid, RetiredGuid, MsaGuid, "nj"), second);
        var collisionProposal = Proposal();
        AssertRefusal(RetirementExpectationTranslationRefusalKind.ManyToOneCollision, () =>
            RetirementExpectationTranslator.Translate(collision, Baseline, Intent(), collisionProposal,
                DryRun(collisionProposal, Effects())));

        var contradiction = FrozenWith(Reading(AnalysisGuid, BundleGuid, RetiredGuid, MsaGuid, "nj"),
            second with { Opinion = "disapproved" });
        var contradictionProposal = Proposal();
        AssertRefusal(RetirementExpectationTranslationRefusalKind.OpinionContradiction, () =>
            RetirementExpectationTranslator.Translate(contradiction, Baseline, Intent(), contradictionProposal,
                DryRun(contradictionProposal, Effects())));
    }

    [Fact]
    public void RefusesMissingBundleIdentityAndGuessedSourceMorphology()
    {
        var missingBundle = Reading(AnalysisGuid, BundleGuid, RetiredGuid, MsaGuid, "nj") with
        {
            Morphs = [Morph(BundleGuid, RetiredGuid, MsaGuid, "nj") with { Bundle = null }],
        };
        var missingProposal = Proposal();
        AssertRefusal(RetirementExpectationTranslationRefusalKind.IncompleteIdentity, () =>
            RetirementExpectationTranslator.Translate(FrozenWith(missingBundle), Baseline, Intent(), missingProposal,
                DryRun(missingProposal, Effects())));

        var guessed = Reading(AnalysisGuid, BundleGuid, RetiredGuid, MsaGuid, "nj") with
        {
            Morphs = [Morph(BundleGuid, RetiredGuid, MsaGuid, "nj") with
            {
                Form = null,
                GuessedString = "nj",
                GuessedWritingSystem = "mgz",
            }],
        };
        var guessedProposal = Proposal();
        AssertRefusal(RetirementExpectationTranslationRefusalKind.IncompleteIdentity, () =>
            RetirementExpectationTranslator.Translate(FrozenWith(guessed), Baseline, Intent(), guessedProposal,
                DryRun(guessedProposal, Effects())));
    }

    [Fact]
    public void KeepsOriginalNegativeAndOnlyStagesAnExplicitHumanRevisionOnTheRetiredForm()
    {
        var originalJudgment = Judgment(NegativeRevisionGuid, NegativeCaseGuid,
            new ReviewedNegativeJudgment(Id(NegativeCaseGuid), "mgz", "nj", "old reading",
                new ReadingNegativeTarget([NegativeMorph(RetiredGuid)])));
        var replacementJudgment = Judgment(NegativeRecordGuid, NegativeCaseGuid,
            new ReviewedNegativeJudgment(Id(NegativeCaseGuid), "mgz", "nj", "rechecked reading",
                new ReadingNegativeTarget([NegativeMorph(RetiredGuid)])),
            [new JudgmentPredecessor(originalJudgment.RevisionId,
                HumanJudgmentCodec.LogicalDigest(originalJudgment))]);
        var originalNegative = FrozenNegative(originalJudgment);
        var original = WithNegative(Frozen(), originalNegative);
        var proposal = ProposalWithJudgment(replacementJudgment);
        var effects = Effects().Append(JudgmentEffect(replacementJudgment)).ToArray();

        var translated = RetirementExpectationTranslator.Translate(original, Baseline, Intent(), proposal,
            DryRun(proposal, effects));

        Assert.Equal(originalJudgment.RevisionId, Assert.Single(translated.Original.ReviewedNegatives).Revision);
        var afterNegative = Assert.Single(translated.Translated.ReviewedNegatives);
        Assert.Equal(replacementJudgment.RevisionId, afterNegative.Revision);
        Assert.Equal(Id(RetiredGuid), Assert.Single(afterNegative.Morphs).Form);
        Assert.Equal("rechecked reading", afterNegative.Context);
    }

    [Fact]
    public void AHumanRetractionRemovesOnlyTheAfterExpectationAndKeepsTheOriginalHistory()
    {
        var originalJudgment = Judgment(NegativeRevisionGuid, NegativeCaseGuid,
            new ReviewedNegativeJudgment(Id(NegativeCaseGuid), "mgz", "nj", "old reading",
                new ReadingNegativeTarget([NegativeMorph(RetiredGuid)])));
        var retraction = Judgment(NegativeRecordGuid, NegativeCaseGuid, new RetractionJudgment(),
            [new JudgmentPredecessor(originalJudgment.RevisionId,
                HumanJudgmentCodec.LogicalDigest(originalJudgment))]);
        var original = WithNegative(Frozen(), FrozenNegative(originalJudgment));
        var proposal = ProposalWithJudgment(retraction);

        var translated = RetirementExpectationTranslator.Translate(original, Baseline, Intent(), proposal,
            DryRun(proposal, Effects().Append(JudgmentEffect(retraction)).ToArray()));

        Assert.Single(translated.Original.ReviewedNegatives);
        Assert.Empty(translated.Translated.ReviewedNegatives);
    }

    private static FrozenExpectationSet Frozen() => FrozenWith(
        Reading(AnalysisGuid, BundleGuid, RetiredGuid, MsaGuid, "nj"),
        Reading(OtherAnalysisGuid, OtherBundleGuid, OtherFormGuid, OtherMsaGuid, "ka"));

    private static FrozenExpectationSet FrozenWith(params FrozenExpectedReading[] readings) => BuildFrozen(
        [new FrozenExpectationCase(Id(WordformGuid), Id(WordformGuid), "mgz", "ganga", false, true, readings)], []);

    private static FrozenExpectationSet WithNegative(FrozenExpectationSet frozen, FrozenReviewedNegative negative) =>
        BuildFrozen(frozen.Cases, [negative]);

    private static FrozenExpectationSet BuildFrozen(IReadOnlyList<FrozenExpectationCase> cases,
        IReadOnlyList<FrozenReviewedNegative> negatives)
    {
        var orderedCases = cases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
        var orderedNegatives = negatives.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
        var json = JsonSerializer.Serialize(new { cases = orderedCases, reviewedNegatives = orderedNegatives }, ManifestOptions);
        var manifestDigest = IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(json));
        return new FrozenExpectationSet(Baseline, Digest, manifestDigest,
            "frozen-expectations/v1/" + manifestDigest, Array.AsReadOnly(orderedCases),
            Array.AsReadOnly(orderedNegatives), []);
    }

    private static FrozenExpectedReading Reading(Guid analysis, Guid bundle, Guid form, Guid msa, string text,
        string opinion = "approved") => new(Id(analysis), Id(analysis), opinion, Digest, [],
        [Morph(bundle, form, msa, text)]);

    private static FrozenExpectationMorph Morph(Guid bundle, Guid form, Guid msa, string text) =>
        new(Id(bundle), Id(form), Id(msa), null, "whole", null, null,
            [new FrozenBundleText("mgz", text, Digest)]);

    private static RetireAllomorphIntent Intent(Guid? replacementForm = null)
    {
        var entry = Id(Guid.Parse("00000000-0000-0000-0000-000000000012"));
        var retired = Identity(RetiredGuid, entry, "alternate");
        var replacement = Identity(replacementForm ?? ReplacementGuid, entry, "alternate");
        return new RetireAllomorphIntent(entry, AllomorphRetirementScope.Affix, [retired],
            [new AllomorphRoleReplacement(Id(RetiredGuid), Id(MsaGuid), null, "whole", replacement)],
            [new RetirementBundle(Id(BundleGuid), Id(AnalysisGuid), Id(WordformGuid), Id(RetiredGuid),
                Id(MsaGuid), null, "whole")], []);
    }

    private static AllomorphIdentity Identity(Guid id, string entry, string location) =>
        new(Id(id), entry, "MoAffixAllomorph", location, "suffix", Digest, null);

    private static Proposal Proposal(Guid? replacementForm = null)
    {
        var operation = new OperationEnvelope(CanonicalId.Mint(), AllomorphRetargetOperationKinds.SetBundleMorph,
            target: CanonicalId.Parse(Id(BundleGuid)), after: Json(new
            {
                retiredForms = new[] { Id(RetiredGuid) },
                censusDigest = Digest,
                retiredForm = Id(RetiredGuid),
                replacement = Id(replacementForm ?? ReplacementGuid),
            }));
        return new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            [operation]);
    }

    private static IReadOnlyList<ExpectedEffect> Effects(Guid? replacementForm = null) =>
    [
        new ExpectedEffect(CanonicalId.Parse(Id(BundleGuid)), SnapshotFields.WfiMorphBundleMorph,
            new Dictionary<string, string> { ["ref"] = Id(RetiredGuid) },
            new Dictionary<string, string> { ["ref"] = Id(replacementForm ?? ReplacementGuid) }),
        new ExpectedEffect(CanonicalId.Parse(Id(BundleGuid)), SnapshotFields.WfiMorphBundleForm,
            new Dictionary<string, string> { ["mgz"] = "nj" },
            new Dictionary<string, string> { ["mgz"] = "n" }),
    ];

    private static SIL.Motif.Model.DryRun.DryRun DryRun(Proposal proposal, IReadOnlyList<ExpectedEffect> effects)
    {
        var proposalDigest = IntentDigest.Compute(proposal);
        var effectDigest = ExpectedEffectSetDigest.Compute(effects);
        var anchor = new BoundDryRunAnchor(proposalDigest, Digest, effectDigest, "1.0.0", "11.0.0", "2",
            "2026-10-07T12:00:00Z");
        return new SIL.Motif.Model.DryRun.DryRun(proposalDigest, "test baseline", effects, effectDigest, anchor);
    }

    private static BaselineToken OtherBaseline() => new(ProjectGuid.ToString("D"), Digest, "projection/v1",
        "2026-10-07T12:00:00Z", "sha256:" + new string('b', 64));

    private static void AssertRefusal(RetirementExpectationTranslationRefusalKind kind, Action action)
    {
        var error = Assert.Throws<RetirementExpectationTranslationRefusalException>(action);
        Assert.True(error.Kind == kind, $"Expected {kind}, got {error.Kind}: {error.Message}");
    }

    private static HumanJudgment Judgment(Guid revision, Guid caseId, HumanJudgmentBody body,
        IReadOnlyList<JudgmentPredecessor>? replaces = null) => new(
        Id(ProjectGuid), Id(caseId), Id(revision), replaces ?? [], body,
        Actor: new JudgmentActor(JudgmentActorKind.Human, "reviewer", "Reviewer"));

    private static FrozenReviewedNegative FrozenNegative(HumanJudgment judgment)
    {
        var negative = (ReviewedNegativeJudgment)judgment.Body;
        var morphs = negative.Target is ReadingNegativeTarget reading
            ? reading.Morphs.Select(item => new FrozenExpectationMorph(null, item.Identity.Form!,
                item.Identity.Msa!, item.Identity.InflType, "whole", item.Identity.GuessedString,
                item.GuessedWritingSystem, [])).ToArray()
            : [];
        return new FrozenReviewedNegative(negative.CaseId, judgment.RevisionId,
            HumanJudgmentCodec.LogicalDigest(judgment), negative.WritingSystem, negative.Form,
            negative.Context, negative.Target is SurfaceNegativeTarget ? "surface" : "reading", morphs);
    }

    private static Proposal ProposalWithJudgment(HumanJudgment judgment)
    {
        var bundleOperation = Proposal().Operations.Single();
        var physical = HumanJudgmentCodec.Format(judgment);
        var judgmentOperation = new OperationEnvelope(CanonicalId.Mint(),
            HumanJudgmentCustomFieldOperationKinds.Set, target: CanonicalId.FromGuid(NegativeRecordGuid),
            after: Json(new { text = physical }));
        return new Proposal(new Dictionary<string, string>
        {
            ["analysis"] = "1.0",
            ["system"] = "1.0",
        }, CanonicalId.Mint(), null, [bundleOperation, judgmentOperation]);
    }

    private static ExpectedEffect JudgmentEffect(HumanJudgment judgment)
    {
        var physical = HumanJudgmentCodec.Format(judgment).Normalize(System.Text.NormalizationForm.FormD);
        return new ExpectedEffect(CanonicalId.FromGuid(NegativeRecordGuid),
            SnapshotFields.RnGenericRecMotifHumanJudgment, new Dictionary<string, string>(),
            new Dictionary<string, string> { ["text"] = physical });
    }

    private static NegativeJudgmentMorph NegativeMorph(Guid form) => new(
        new ParseMorph(Id(form), Id(MsaGuid), null, null), null,
        form == RetiredGuid ? "nj" : "n", "affix");

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
    private static string Id(Guid value) => CanonicalId.FromGuid(value).Value;
}
