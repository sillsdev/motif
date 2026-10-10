using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Commands.Parsimony;

public sealed class ParsimonyParserEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyParserEvidenceTests),
        Guid.NewGuid().ToString("N"));
    private readonly Guid _wordform = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly Guid _approvedForm = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private readonly Guid _rejectedForm = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private readonly Guid _nearMatchForm = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private readonly Guid _msa = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private readonly Guid _otherMsa = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");
    private readonly Guid _inflType = Guid.Parse("abababab-abab-4bab-8bab-abababababab");
    private readonly Guid _approvedAnalysis = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly Guid _rejectedAnalysis = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private readonly Guid _duplicateRejectedAnalysis = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void DisapprovedReadingDoesNotRejectOtherReadings()
    {
        var parserCase = Case("kita", Analysis(Morph(_rejectedForm)), Analysis(Morph(_approvedForm)),
            Analysis(Morph(_nearMatchForm)));
        var source = Source([parserCase]);
        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Single(projection.DisapprovedMorphologies);
        Assert.Equal(2, JsonSerializer.Deserialize<string[]>(
            projection.DisapprovedMorphologies[0].AnalysisGuidsJson)!.Length);
        Assert.Equal(2, projection.DisapprovedMatches.Count);
        Assert.Contains(projection.DisapprovedMatches,
            item => item.DisapprovedAnalysisGuid == _rejectedAnalysis.ToString("D").ToLowerInvariant());
        Assert.Contains(projection.DisapprovedMatches,
            item => item.DisapprovedAnalysisGuid == _duplicateRejectedAnalysis.ToString("D").ToLowerInvariant());
        Assert.Equal("complete", Assert.Single(projection.Cases).Completion);
        Assert.Equal(3, projection.Analyses.Count);
    }

    [Fact]
    public void GuessedStringUsesTheExistingAdr0027Matcher()
    {
        var parserCase = Case("kita", new ParseAnalysis([new ParseMorph(
            GuidText(_rejectedForm), GuidText(_msa), null, "rejected-allomorph")]));
        var source = Source([parserCase]);
        var wordform = Wordform(rejectedForms: ["rejected-allomorph"]);

        var projection = AssessmentEvidenceProjector.Build([source], [wordform], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal(2, projection.DisapprovedMatches.Count);
        Assert.Equal(new[] { _rejectedAnalysis, _duplicateRejectedAnalysis }
                .Select(item => item.ToString("D").ToLowerInvariant()).Order(StringComparer.Ordinal),
            projection.DisapprovedMatches.Select(item => item.DisapprovedAnalysisGuid).Order(StringComparer.Ordinal));
        Assert.Equal("rejected-allomorph", Assert.Single(projection.Morphs).GuessedStringNfd);
    }

    [Fact]
    public void MsaAndInflectionTypeMustMatchTheDisapprovedTriple()
    {
        var original = Wordform();
        var rejectedMorph = original.Analyses[1].Morphs[0] with { InflTypeGuid = _inflType };
        var wordform = original with
        {
            Analyses = original.Analyses.Select(item => item.Opinion == "disapproved"
                ? item with { Morphs = [rejectedMorph] }
                : item).ToArray(),
        };
        var parserCase = Case("kita",
            Analysis(new ParseMorph(GuidText(_rejectedForm), GuidText(_msa), GuidText(_inflType), null)),
            Analysis(new ParseMorph(GuidText(_rejectedForm), GuidText(_otherMsa), GuidText(_inflType), null)),
            Analysis(new ParseMorph(GuidText(_rejectedForm), GuidText(_msa), null, null)));
        var source = Source([parserCase]);

        var projection = AssessmentEvidenceProjector.Build([source], [wordform], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal(2, projection.DisapprovedMatches.Count);
        Assert.All(projection.DisapprovedMatches, item => Assert.Equal(0, item.ParserAnalysisOrdinal));
    }

    [Fact]
    public void OrderedMorphBundlesMustRemainInTheSameOrder()
    {
        var original = Wordform();
        var first = original.Analyses[1].Morphs[0];
        var second = first with
        {
            MorphGuid = _approvedForm,
            Forms = [new EvidenceForm("en", "second", "second")],
        };
        var wordform = original with
        {
            Analyses = original.Analyses.Select(item => item.Opinion == "disapproved"
                ? item with { Morphs = [first, second] }
                : item).ToArray(),
        };
        var exact = new ParseAnalysis([Morph(_rejectedForm), Morph(_approvedForm)]);
        var reversed = new ParseAnalysis([Morph(_approvedForm), Morph(_rejectedForm)]);
        var source = Source([Case("kita", exact, reversed)]);

        var projection = AssessmentEvidenceProjector.Build([source], [wordform], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal(2, projection.DisapprovedMatches.Count);
        Assert.All(projection.DisapprovedMatches, item => Assert.Equal(0, item.ParserAnalysisOrdinal));
    }

    [Fact]
    public void TimeoutAndInvalidShapeDoNotBecomeCompletedEvidence()
    {
        var timedOut = Case("kita", Analysis(Morph(_rejectedForm))) with { TimedOut = true };
        var invalid = Case("kita") with { InvalidShape = true };
        var source = Source([timedOut, invalid]);

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.All(projection.Cases, item => Assert.Equal("incomplete", item.Completion));
        Assert.Equal(2, projection.DisapprovedMatches.Count);
        Assert.Equal(0, Assert.Single(projection.Runs).CompletedCaseCount);
    }

    [Fact]
    public void CompletedZeroAnalysisCaseRemainsACompletedCase()
    {
        var source = Source([Case("kita")]);

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        var parserCase = Assert.Single(projection.Cases);
        Assert.Equal("complete", parserCase.Completion);
        Assert.Equal(0, parserCase.AnalysisCount);
        Assert.Empty(projection.Analyses);
    }

    [Fact]
    public void UnsupportedParserCountersRemainUnknown()
    {
        var source = Source([Case("kita")]);
        source = source with { StoredCases = [Assert.Single(source.StoredCases)! with { Attempts = 37 }] };

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        var parserCase = Assert.Single(projection.Cases);
        Assert.Null(parserCase.Attempts);
        Assert.Null(parserCase.Passes);
    }

    [Fact]
    public void MissingSidecarProducesUnavailableRunInsteadOfAnEmptyParserResult()
    {
        var source = Source([Case("kita")], includeSidecar: false);

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal("unavailable", Assert.Single(projection.Runs).Status);
        Assert.Empty(projection.Cases);
    }

    [Fact]
    public void StoredGrammarWarningsMayExtendAnEmptySidecarCase()
    {
        var source = Source([Case("kita")]);
        var storedCase = Assert.Single(source.StoredCases)! with
        {
            Unavailable = ["warning: this affix cannot be loaded as an affix rule."],
        };
        source = source with { StoredCases = [storedCase] };

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal("complete", Assert.Single(projection.Runs).Status);
        Assert.Equal("complete", Assert.Single(projection.Cases).Completion);
    }

    [Fact]
    public void UnrelatedStoredWarningCannotExtendASidecarCase()
    {
        var source = Source([Case("kita")]);
        var storedCase = Assert.Single(source.StoredCases)! with
        {
            Unavailable = ["warning: unrelated parser warning."],
        };
        source = source with { StoredCases = [storedCase] };

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal("unavailable", Assert.Single(projection.Runs).Status);
        Assert.Empty(projection.Cases);
    }

    [Fact]
    public void ObservedBadReadingSurvivesIncompleteSearchWithoutARejectionClaim()
    {
        var source = Source([Case("kita", Analysis(Morph(_rejectedForm))) with { Capped = true }]);

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal("incomplete", Assert.Single(projection.Cases).Completion);
        Assert.Equal(2, projection.DisapprovedMatches.Count);
        Assert.All(projection.DisapprovedMatches, item => Assert.Equal(0, item.ParserAnalysisOrdinal));
    }

    [Fact]
    public void ReviewedNegativeAcceptedHasACompletedCaseDenominator()
    {
        var lumaWordform = Wordform() with
        {
            Guid = Guid.Parse("55555555-5555-4555-8555-555555555555"),
            Forms = [new EvidenceForm("en", "luma", "luma")],
        };
        var source = Source([
            Case("kita", Analysis(Morph(_approvedForm))),
            Case("luma", Analysis(Morph(_rejectedForm))) with { Capped = true },
        ]);

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform(), lumaWordform],
            source.SourceSha256, ParserHash(), "proposal/exact", "sha256:intent",
            [Negative("negative/kita", "revision/kita", "kita"),
                Negative("negative/luma", "revision/luma", "luma")]);

        var completed = Assert.Single(projection.ReviewedNegativeCases,
            item => item.CaseId == "negative/kita");
        Assert.Equal("complete", completed.Completion);
        Assert.True(completed.Accepted);
        var incomplete = Assert.Single(projection.ReviewedNegativeCases,
            item => item.CaseId == "negative/luma");
        Assert.Equal("incomplete", incomplete.Completion);
        Assert.True(incomplete.Accepted);
        Assert.Equal(1, projection.ReviewedNegativeCases.Count(item => item.Completion == "complete"));
    }

    [Fact]
    public void BaselineAssessmentCannotBeAttachedToCandidateEvidence()
    {
        var source = Source([Case("kita")], proposalId: null, proposalIntentDigest: null);

        var exception = Assert.Throws<InvalidDataException>(() => AssessmentEvidenceProjector.Build(
            [source], [Wordform()], source.SourceSha256, ParserHash(), "proposal/exact", "sha256:intent"));

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactProposalAssessmentIsAcceptedForItsCandidateSource()
    {
        var source = Source([Case("kita")], proposalId: "proposal/exact", proposalIntentDigest: "sha256:intent");

        var projection = AssessmentEvidenceProjector.Build([source], [Wordform()], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal("complete", Assert.Single(projection.Runs).Status);
        Assert.Equal("proposal/exact", source.ProposalId);
    }

    [Fact]
    public void SurfaceWithMoreThanOneWritingSystemIdentityIsNotJoined()
    {
        var first = Wordform();
        var second = first with { Guid = Guid.Parse("44444444-4444-4444-8444-444444444444") };
        var source = Source([Case("kita", Analysis(Morph(_rejectedForm)))]);

        var projection = AssessmentEvidenceProjector.Build([source], [first, second], source.SourceSha256,
            ParserHash(), "proposal/exact", "sha256:intent");

        Assert.Equal("unavailable", Assert.Single(projection.Cases).IdentityStatus);
        Assert.Empty(projection.DisapprovedMorphologies);
        Assert.Empty(projection.DisapprovedMatches);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ParserAssessmentSource Source(IReadOnlyList<ParseWordEvidence> cases,
        bool includeSidecar = true, string? proposalId = "proposal/exact",
        string? proposalIntentDigest = "sha256:intent")
    {
        Directory.CreateDirectory(_root);
        var orderedCases = cases.Select((item, index) => item with { Index = index }).ToArray();
        var sourcePath = Path.Combine(_root, "source.fwdata");
        File.WriteAllText(sourcePath, "exact project source", Encoding.UTF8);
        var parserHash = ParserHash();
        string? sidecarPath = null;
        string? sidecarSha = null;
        if (includeSidecar)
        {
            sidecarPath = Path.Combine(_root, "analyses.jsonl");
            File.WriteAllLines(sidecarPath, orderedCases.Select(item =>
                JsonSerializer.Serialize(item, ParseMorphEvidence.JsonOptions)));
            sidecarSha = BatchInvocationEvidence.DigestFile(sidecarPath);
        }
        return new ParserAssessmentSource("assessment/parser", "pangloss", AssessmentKinds.ParseTime,
            proposalId, proposalIntentDigest, BatchInvocationEvidence.DigestFile(sourcePath), "sha256:" + parserHash,
            null, "invocation/test", sourcePath, sidecarPath, sidecarSha, 1000, "1000", 1, orderedCases);
    }

    private string ParserHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("parser executable")))
        .ToLowerInvariant();

    private EvidenceWordform Wordform(IReadOnlyList<string>? rejectedForms = null)
    {
        var morphs = new[]
        {
            new EvidenceMorph(_approvedForm, _msa, null, null, null,
                [new EvidenceForm("en", "approved-allomorph", "approved-allomorph")], [], []),
            new EvidenceMorph(_rejectedForm, _msa, null, null, null,
                (rejectedForms ?? ["rejected-allomorph"]).Select(form => new EvidenceForm("en", form, form)).ToArray(), [], []),
        };
        return new EvidenceWordform(_wordform, 0,
            [new EvidenceForm("en", "kita", "kita")],
            [
                new EvidenceAnalysis(_approvedAnalysis, "approved", "fieldworks", "approved", [morphs[0]]),
                new EvidenceAnalysis(_rejectedAnalysis, "disapproved", "fieldworks", "rejected", [morphs[1]]),
                new EvidenceAnalysis(_duplicateRejectedAnalysis, "disapproved", "fieldworks", "duplicate", [morphs[1]]),
            ]);
    }

    private static ParseWordEvidence Case(string word, params ParseAnalysis[] analyses) =>
        new(ParseMorphEvidence.Schema, 0, word, 4, false, false, false, analyses, []);

    private ParseAnalysis Analysis(ParseMorph morph) => new([morph]);

    private ParseMorph Morph(Guid form) => new(GuidText(form), GuidText(_msa), null, null);

    private static ReviewedNegativeExpectation Negative(string caseId, string revisionId, string form) =>
        new(caseId, "judgment/" + caseId, revisionId, "notebook/" + caseId,
            "sha256:" + new string('a', 64), "en", form, "context", new SurfaceNegativeTarget(), null,
            null, "A person rejected this surface.", new JudgmentActor(JudgmentActorKind.Human), null,
            null, "eligible", null);

    private static string GuidText(Guid guid) => guid.ToString("D").ToLowerInvariant();
}
