using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Config;
using SIL.Motif.Projection.Review;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewInputCaptureTests
{
    [Fact]
    public async Task ConfiguredSetsJoinTouchedWordsByDefault()
    {
        var captured = await Capture([Set("controls", "reference", Member("one", "cat"))], ["dog"]);
        Assert.Equal(new[] { "cat", "dog" }, captured.Cases.Select(item => item.Word).Order().ToArray());
        Assert.Single(captured.Sets);
        Assert.Empty(captured.OmittedSetIds);
    }

    [Fact]
    public async Task ExplicitSubsetNamesOmittedSets()
    {
        var captured = await Capture([Set("a", "reference", Member("one", "cat")),
            Set("b", "regression", Member("two", "dog"))], [], ["a"]);
        Assert.Equal("b", Assert.Single(captured.OmittedSetIds));
        Assert.Equal("cat", Assert.Single(captured.Cases).Word);
    }

    [Fact]
    public async Task NoSetsIsExplicitAndDoesNotChangeOtherScopes()
    {
        var captured = await Capture([], ["dog"]);
        Assert.Empty(captured.Sets);
        Assert.Equal("dog", Assert.Single(captured.Cases).Word);
    }

    [Fact]
    public async Task WeightsAndCellsChangeMembershipDigest()
    {
        var first = Member("one", "cat") with { Token = new("occ", "text", null, 0, 1) };
        var second = first with { Token = first.Token! with { Weight = 2 } };
        var a = await Capture([Set("sample", "sampled-tokens", first)], []);
        var b = await Capture([Set("sample", "sampled-tokens", second)], []);
        Assert.NotEqual(a.Sets[0].MembershipDigest, b.Sets[0].MembershipDigest);
        var cell = Member("cell-case", "cat") with
        {
            Cell = new("cell", "lexeme", [new ReviewCellFeature("tense", "past")], "variant"),
        };
        var changedCell = cell with { Cell = cell.Cell! with { VariantId = "other" } };
        var c = await Capture([Set("cells", "paradigm-cells", cell)], []);
        var d = await Capture([Set("cells", "paradigm-cells", changedCell)], []);
        Assert.NotEqual(c.Sets[0].MembershipDigest, d.Sets[0].MembershipDigest);
    }

    [Fact]
    public async Task DisplayRenameDoesNotChangeNumericScope()
    {
        var first = Set("controls", "reference", Member("one", "cat"));
        var a = await Capture([first], []);
        var b = await Capture([first with { Name = "Renamed" }], []);
        Assert.Equal(a.ScopeDigest, b.ScopeDigest);
        Assert.Equal(a.Sets[0].MembershipDigest, b.Sets[0].MembershipDigest);
    }

    [Fact]
    public async Task ConflictingIdentityPartitionsCannotReuseOneParse()
    {
        var second = Member("two", "cat") with { WritingSystemId = "other" };
        var captured = await Capture([Set("a", "reference", Member("one", "cat"), second)], []);
        var partitions = ReviewInputCapture.Partitions(captured);
        Assert.Equal(2, partitions.Count);
        Assert.All(partitions, partition => Assert.Equal("cat", Assert.Single(partition.Words)));
        Assert.Equal(2, partitions.Select(partition => partition.WritingSystemId).Distinct().Count());
    }

    [Fact]
    public async Task TouchedWordKeepsApprovedAssertionsWhenNamedMembershipHasNoReferences()
    {
        var wordform = Guid.NewGuid();
        var wordformId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(wordform).Value;
        var morphology = new ApprovedMorphology([]) { SourceWordformGuid = wordform.ToString("D") };
        var assertion = new ReviewAssertion("approved", "reading", morphology, ReferenceAuthority.Human,
            "person", "revision", true, false, true, []);
        var identity = new ReviewProjectWordIdentity("cat", "qaa", "qaa", wordformId, "word-key");
        var member = Member("one", "cat") with { WordformId = wordformId };
        var result = await ReviewInputCapture.CaptureAsync("project", "proposal", null,
            [Set("a", "reference", member)], null, ["cat"], [identity], [assertion], []);
        Assert.Equal("approved", Assert.Single(Assert.Single(result.Cases).Assertions).Id);
    }

    [Fact]
    public async Task NamedMembershipDoesNotHideAmbiguousTouchedWord()
    {
        var identities = new[]
        {
            new ReviewProjectWordIdentity("cat", "qaa", "qaa", "first", "first-key"),
            new ReviewProjectWordIdentity("cat", "qab", "qab", "second", "second-key"),
        };
        var result = await ReviewInputCapture.CaptureAsync("project", "proposal", null,
            [Set("a", "reference", Member("one", "cat") with { WritingSystemId = "qac" })], null,
            ["cat"], identities, [], []);
        Assert.Equal(new[] { "qaa", "qab", "qac" }, result.Cases.Select(item => item.WritingSystemId).Order().ToArray());
    }

    [Fact]
    public async Task ContextualNegativesRemainDistinctAndUnavailableToAnUnqualifiedParse()
    {
        var member = Member("one", "cat") with { ExpectationRefs = ["n1", "n2"] };
        var first = new ReviewAssertion("n1", "", null, ReferenceAuthority.Human,
            "person", "revision", false, true, true, []);
        var second = first with { Id = "n2" };
        var captured = await ReviewInputCapture.CaptureAsync("project", "proposal", null,
            [Set("a", "negative", member)], null, [], [], [],
            [new("one", "cat", "qaa", "before", null, first),
             new("one", "cat", "qaa", "after", null, second)]);
        var assertions = Assert.Single(captured.Cases).Assertions;
        Assert.Equal(new[] { "before", "after" }, assertions.Select(item => item.Context).ToArray());
        Assert.All(assertions, item => Assert.False(item.Eligible));
        var result = SIL.Motif.Worker.Runtime.Review.ReviewCaseClassifier.Compare(new(
            "case", "word", "cat", "qaa", null, null, assertions, Assert.Single(captured.Cases).Memberships)
            { IdentityAvailable = true });
        Assert.Contains("negative-context-unavailable", result.Limitations);
        Assert.Equal(new[] { "n1", "n2" }, result.Before.UnknownExpectationIds);
        Assert.False(result.NewlyAcceptedNegative);
    }

    [Fact]
    public async Task TouchedSpellingInMultipleWritingSystemsKeepsEveryIdentity()
    {
        var identities = new[] { new ReviewProjectWordIdentity("cat", "qaa", "qaa", "wordform/a", "key/a"),
            new ReviewProjectWordIdentity("cat", "qab", "qab", "wordform/b", "key/b") };
        var captured = await ReviewInputCapture.CaptureAsync("project", "proposal", null, [], null,
            ["cat"], identities, [], []);
        Assert.Equal(new[] { "key/a", "key/b" }, captured.Cases.Select(item => item.WordKey).Order().ToArray());
        Assert.Equal(2, ReviewInputCapture.Partitions(captured).Count);
    }

    [Fact]
    public async Task FrozenExpectationsSurviveSourceAndOpinionEdits()
    {
        var wordform = Guid.NewGuid();
        var wordformId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(wordform).Value;
        var forms = new List<string> { "cat" };
        var refs = new List<string> { "analysis" };
        var morphology = new ApprovedMorphology([new(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), null, forms)])
        { SourceWordformGuid = wordform.ToString("D") };
        var assertion = new ReviewAssertion("analysis", "reading", morphology, ReferenceAuthority.Human,
            "person", "revision", true, false, true, refs);
        var identity = new ReviewProjectWordIdentity("cat", "qaa", "qaa", wordformId, "word-key");
        var member = Member("one", "cat") with { WordformId = wordformId, ExpectationRefs = refs };
        var captured = await ReviewInputCapture.CaptureAsync("project", "proposal", null,
            [Set("a", "reference", member)], null, [], [identity], [assertion], []);

        forms[0] = "changed";
        refs[0] = "changed";
        var frozen = Assert.Single(Assert.Single(captured.Cases).Assertions);
        Assert.Equal("cat", Assert.Single(Assert.Single(frozen.Morphology!.Morphs).Forms));
        Assert.Equal("analysis", Assert.Single(frozen.SourceRefs));
        Assert.Equal("analysis", Assert.Single(Assert.Single(captured.Cases).Memberships).ExpectationIds[0]);
    }

    [Fact]
    public async Task ManagedArtifactRetainsSourceBytesAndRejectsTampering()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(ReviewInputCaptureTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var database = MotifDatabase.OpenOwned(Path.Combine(root, "project.db"),
                new ProjectLocator(Path.Combine(root, "project.fwdata"), "project"),
                MotifSchema.CurrentSchema, new Version(1, 0));
            var inputs = await Capture([Set("a", "reference", Member("one", "cat"))], []);
            var source = new byte[] { 1, 2, 3 };
            var reference = ReviewInputArtifacts.Store(database, new(inputs, new Dictionary<string, byte[]> { ["a"] = source }));
            source[0] = 9;
            Assert.Equal(new byte[] { 1, 2, 3 }, ReviewInputArtifacts.Read(database, reference).Sources["a"]);
            var path = Path.Combine(database.FullPath + ".review-inputs", reference.Sha256 + ".json");
            File.AppendAllText(path, " ");
            Assert.Throws<InvalidOperationException>(() => ReviewInputArtifacts.Read(database, reference));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CandidateCannotConfirmItsOwnReference()
    {
        var id = SIL.Motif.Contract.Ids.CanonicalId.Mint("proposal/");
        var other = SIL.Motif.Contract.Ids.CanonicalId.Mint("proposal/");
        ReviewSourceDecision[] decisions =
        [
            new(Guid.NewGuid(), Guid.NewGuid(), true, id.Value, "revision", "operation"),
            new(Guid.NewGuid(), Guid.NewGuid(), true, other.Value, "revision", "other-operation"),
        ];
        var retained = Assert.Single(ReviewTrialCapture.ExcludingCandidate(decisions, id.Value));
        Assert.Equal(other.Value, retained.ProposalId);
    }

    private static Task<SIL.Motif.Contract.Review.FrozenReviewInputs> Capture(
        IReadOnlyList<ReviewCaptureSet> sets, IReadOnlyList<string> touched, IReadOnlyList<string>? chosen = null) =>
        ReviewInputCapture.CaptureAsync("project", "proposal", null, sets, chosen, touched, [], [], []);

    private static ReviewCaptureSet Set(string id, string role, params ReviewWordSetMember[] members) =>
        new(id, id, role, "source", members);

    private static ReviewWordSetMember Member(string id, string word) =>
        new(id, word, "qaa", null, new("citation", null), [], null, null);
}
