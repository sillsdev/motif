using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;
using WordRow = SIL.Motif.Contract.Review.WordRow;

namespace SIL.Motif.Tests.Worker.Review;

public sealed class ReviewReportPagingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-review-store-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly ReviewReportRepository _repository;
    private readonly CanonicalId _proposal = CanonicalId.Mint("proposal/");

    public ReviewReportPagingTests()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) VALUES ($id, 'sha256:intent', 'proposed');";
        command.Parameters.AddWithValue("$id", _proposal.Value);
        command.ExecuteNonQuery();
        _repository = new ReviewReportRepository(_database);
    }

    [Fact]
    public async Task UnchangedWordsAndAllMembershipsAreAddressable()
    {
        var cases = Enumerable.Range(0, 4).Select(i => Sample(i, 2)).ToArray();
        var id = await Publish("all", cases);
        var words = _repository.ReadPage(Request(id));
        Assert.Equal(4, words.TotalMatching);
        Assert.Equal(4, words.Items.Count);
        var tokens = _repository.ReadPage(Request(id) with { Section = "sampled-tokens" });
        Assert.Equal(8, tokens.TotalMatching);
        Assert.All(tokens.Items, row => Assert.IsType<TokenRow>(row));
    }

    [Fact]
    public async Task FailureSamplesPrioritizeWeightThenMembershipCount()
    {
        var low = Sample(0, 1);
        var many = Sample(1, 2);
        var heavy = Sample(2, 1) with
        {
            Memberships = [new ReviewMembership("set", "heavy", "sampled-tokens", 9, null, null, [])]
        };
        var id = await Publish("failure-order", low, many, heavy);
        var samples = _repository.ReadFailureSamples(Request(id), 3);
        Assert.Equal(new[] { "case-0002", "case-0001", "case-0000" }, samples.Select(row => row.CaseKey));
    }

    [Fact]
    public async Task RetainedSectionRowsAndEmptySectionsAreAddressable()
    {
        var effect = new EffectRow("operation", "change", new ReadTarget(TargetKind.Resource, "detail", "motif://detail", null, null));
        var publication = Publication("sections") with
        {
            AvailableSections = ["effects", "warnings"],
            SectionItems = SectionRows(new ReviewSectionItem("effects", effect))
        };
        var id = await _repository.PublishAsync(publication, Cases(Sample(0, 0)), CancellationToken.None);
        var effects = _repository.ReadPage(Request(id) with { Section = "effects" });
        Assert.Equal("operation", Assert.IsType<EffectRow>(Assert.Single(effects.Items)).OperationId);
        var warnings = _repository.ReadPage(Request(id) with { Section = "warnings" });
        Assert.Equal(ListAvailability.Complete, warnings.ListAvailability);
        Assert.Empty(warnings.Items);
        Assert.Throws<ReviewDetailUnavailableException>(() => _repository.ReadPage(Request(id) with { Section = "parsimony" }));
    }

    [Fact]
    public async Task ParserWorkRuleChangesRoundTripThroughFullReportPages()
    {
        var row = new RuleWorkRow("morph_rule", "rule-id", "authored", null, "Rule", "paired", 25, null);
        var publication = Publication("rule-work") with
        {
            AvailableSections = ["parser-work"], SectionItems = SectionRows(new ReviewSectionItem("parser-work", row))
        };
        var id = await _repository.PublishAsync(publication, Cases(Sample(0, 0)), CancellationToken.None);
        var page = _repository.ReadPage(Request(id) with { Section = "parser-work" });
        Assert.Equal(25, Assert.IsType<RuleWorkRow>(Assert.Single(page.Items)).PercentChange);
        Assert.DoesNotContain("parserWork", JsonSerializer.Serialize(page, ReviewJson.Options), StringComparison.OrdinalIgnoreCase);
    }

    private static async IAsyncEnumerable<ReviewSectionItem> SectionRows(params ReviewSectionItem[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    [Fact]
    public async Task OversizedSectionRowReturnsAnIdentifiableStub()
    {
        var effect = new EffectRow("operation", new string('x', 220_000),
            new ReadTarget(TargetKind.Resource, "detail", "motif://detail", null, null));
        var publication = Publication("large-effect") with
        {
            AvailableSections = ["effects"], SectionItems = SectionRows(new ReviewSectionItem("effects", effect))
        };
        var id = await _repository.PublishAsync(publication, Cases(Sample(0, 0)), CancellationToken.None);
        var page = _repository.ReadPage(Request(id) with { Section = "effects" });
        var row = Assert.IsType<EffectRow>(Assert.Single(page.Items));
        Assert.Equal("operation", row.OperationId);
        Assert.Equal("motif://detail", row.Detail.Uri);
        Assert.Equal(ListAvailability.Partial, page.ListAvailability);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, ReviewJson.Options).Length <= 192 * 1024);
    }

    [Fact]
    public async Task ParadigmCellCombinesEveryVariant()
    {
        var first = Sample(0, 0) with
        {
            Memberships = [new ReviewMembership("paradigm", "one", "paradigm-cells", 1, "cell", "one", ["e1"]) { LexemeId = "lexeme" }],
            Before = new ReviewCaseSideFacts(Completion.Complete, false, [], [], ["e1"], []),
            After = new ReviewCaseSideFacts(Completion.Complete, false, [], [], ["e1"], [])
        };
        var second = Sample(1, 0) with
        {
            Memberships = [new ReviewMembership("paradigm", "two", "paradigm-cells", 1, "cell", "two", ["e2"]) { LexemeId = "lexeme" }],
            Before = new ReviewCaseSideFacts(Completion.Complete, true, [], ["e2"], [], []),
            After = new ReviewCaseSideFacts(Completion.Complete, false, [], [], ["e2"], [])
        };
        var id = await Publish("cell", first, second);
        var page = _repository.ReadPage(Request(id) with { Section = "paradigm-cells" });
        var row = Assert.IsType<CellRow>(Assert.Single(page.Items));
        Assert.Equal(2, row.VariantCount);
        Assert.Equal(CellOutcome.Covered, row.Before);
        Assert.Equal(CellOutcome.Uncovered, row.After);
    }

    [Fact]
    public async Task WordOrderUsesNormalizedOrdinalKeysAcrossPages()
    {
        var cases = new[] { Sample(0, 0) with { Word = "\uE000" }, Sample(1, 0) with { Word = "\U00010000" },
            Sample(2, 0) with { Word = "\u00E9" }, Sample(3, 0) with { Word = "e\u0301" } };
        var id = await Publish("ordinal", cases);
        var actual = new List<string>();
        string? cursor = null;
        do
        {
            var page = _repository.ReadPage(Request(id) with { Limit = 1, Cursor = cursor });
            actual.Add(Assert.IsType<WordRow>(Assert.Single(page.Items)).Word);
            cursor = page.NextCursor;
        } while (cursor is not null);
        var expected = cases.OrderBy(item => item.Word.Normalize(NormalizationForm.FormD), StringComparer.Ordinal)
            .ThenBy(item => item.CaseKey, StringComparer.Ordinal).Select(item => item.Word);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task EveryCellVariantHasAChildPage()
    {
        var cases = Enumerable.Range(0, 23).Select(i => Sample(i, 0) with
        {
            Memberships = [new ReviewMembership("paradigm", "case-" + i, "paradigm-cells", 1,
                "cell", "variant-" + i, []) { LexemeId = "lexeme" }]
        }).ToArray();
        var id = await Publish("variants", cases);
        var first = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "variants", "cell", null, 10) { SetId = "paradigm" });
        var second = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "variants", "cell", first.NextCursor, 20) { SetId = "paradigm" });
        Assert.Equal(10, first.Returned);
        Assert.Equal(13, second.Returned);
        Assert.Equal(23, first.Items.Concat(second.Items).Cast<WordRow>().Select(row => row.CaseKey).Distinct().Count());
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ControlMembershipsRemainDistinctForOneWord()
    {
        var item = Sample(0, 0) with { Memberships = [
            new ReviewMembership("a", "case-a", "reference", 1, null, null, []),
            new ReviewMembership("b", "case-b", "regression", 1, null, null, [])] };
        var id = await Publish("controls", item);
        var page = _repository.ReadPage(Request(id) with { Section = "controls" });
        Assert.Equal(2, page.TotalMatching);
        Assert.Equal(new[] { "a", "b" }, page.Items.Cast<WordRow>().Select(row => Assert.Single(row.SetIds)));
    }

    [Fact]
    public async Task MoreThanOneHundredFailuresAreAllReachable()
    {
        var id = await Publish("many", Enumerable.Range(0, 150).Select(i => Sample(i, 0) with { NewNoParse = true }).ToArray());
        var seen = new HashSet<string>();
        string? cursor = null;
        do
        {
            var page = _repository.ReadPage(Request(id) with { Cursor = cursor, Limit = 37 });
            foreach (var item in page.Items) Assert.True(seen.Add(Assert.IsType<WordRow>(item).CaseKey));
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(150, seen.Count);
    }

    [Fact]
    public async Task OneWordWithMoreThanTenPlacesHasCompleteChildPages()
    {
        var id = await Publish("places", Sample(0, 23));
        var first = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "memberships", "case-0000", null, 10));
        var second = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "memberships", "case-0000", first.NextCursor, 20));
        Assert.Equal(10, first.Returned);
        Assert.Equal(13, second.Returned);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task CursorBindsProjectReportSectionAndFilters()
    {
        var first = await Publish("cursor-a", Sample(0, 2), Sample(1, 2));
        var second = await Publish("cursor-b", Sample(0, 2), Sample(1, 2));
        var cursor = _repository.ReadPage(Request(first) with { Limit = 1 }).NextCursor;
        Assert.NotNull(cursor);
        Assert.Throws<ReviewCursorInvalidException>(() => _repository.ReadPage(Request(first) with { Cursor = cursor, ProjectKey = "other" }));
        Assert.Throws<ReviewCursorInvalidException>(() => _repository.ReadPage(Request(second) with { Cursor = cursor }));
        Assert.Throws<ReviewCursorInvalidException>(() => _repository.ReadPage(Request(first) with { Cursor = cursor, Section = "sampled-tokens" }));
        Assert.Throws<ReviewCursorInvalidException>(() => _repository.ReadPage(Request(first) with { Cursor = cursor, Filter = "changed" }));
    }

    [Fact]
    public async Task SetAndOutcomeFiltersSelectExactMemberships()
    {
        var item = Sample(0, 0) with
        {
            NewNoParse = true, Changed = true, Flags = [ReviewFlagId.NewWordFailures],
            Before = new ReviewCaseSideFacts(Completion.Complete, true, [], [], [], []),
            After = new ReviewCaseSideFacts(Completion.Complete, false, [], [], [], []),
            Memberships = [
                new ReviewMembership("a", "a-case", "sampled-tokens", 1, null, null, []) { OccurrenceId = "a-place" },
                new ReviewMembership("b", "b-case", "sampled-tokens", 1, null, null, []) { OccurrenceId = "b-place" }]
        };
        var id = await Publish("filters", item);
        var page = _repository.ReadPage(Request(id) with { Section = "sampled-tokens", Set = "a", Outcome = "no-parse",
            Flag = ReviewFlagId.NewWordFailures, Filter = "changed" });
        Assert.Equal(2, page.TotalInSection);
        Assert.Equal(1, page.TotalMatching);
        Assert.Equal("a-place", Assert.IsType<TokenRow>(Assert.Single(page.Items)).OccurrenceId);
    }

    [Fact]
    public async Task UnknownChangeIsNeitherChangedNorUnchanged()
    {
        var id = await Publish("change-filter", Sample(0, 0) with { Changed = true },
            Sample(1, 0) with { Changed = false }, Sample(2, 0));
        var changed = _repository.ReadPage(Request(id) with { Filter = "changed" });
        var unchanged = _repository.ReadPage(Request(id) with { Filter = "unchanged" });
        Assert.Equal("case-0000", Assert.IsType<WordRow>(Assert.Single(changed.Items)).CaseKey);
        Assert.Equal("case-0001", Assert.IsType<WordRow>(Assert.Single(unchanged.Items)).CaseKey);
        Assert.Equal(3, changed.TotalInSection);
        Assert.Equal(3, unchanged.TotalInSection);
    }

    [Fact]
    public async Task MalformedChildCursorIsRejected()
    {
        var id = await Publish("bad-child", Sample(0, 0) with { Readings = [] });
        var cursor = ReviewReportCursor.Encode(new(1, "project", id.Value, "readings", "children-v1", 1,
            "not-an-ordinal", "readings", "case-0000"));
        Assert.Throws<ReviewCursorInvalidException>(() => _repository.ReadChildren(
            new ReviewChildPageRequest("project", id, "readings", "case-0000", cursor)));
    }

    [Fact]
    public async Task MissingReadingEvidenceIsUnavailable()
    {
        var id = await Publish("missing-readings", Sample(0, 0));
        var page = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "readings", "case-0000"));
        Assert.Equal(ListAvailability.Unavailable, page.ListAvailability);
        Assert.Null(page.TotalMatching);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task NewReportsDoNotChangeOldPages()
    {
        var id = await Publish("old", Sample(0, 0), Sample(1, 0));
        var before = JsonSerializer.Serialize(_repository.ReadPage(Request(id)), ReviewJson.Options);
        await Publish("new", Sample(2, 0));
        Assert.Equal(before, JsonSerializer.Serialize(_repository.ReadPage(Request(id)), ReviewJson.Options));
    }

    [Fact]
    public async Task OversizedReadingListReturnsStubThenCompleteChildren()
    {
        var many = Enumerable.Range(0, 300).Select(i => new ReadingRow("reading-" + i, Side.Before,
            IdentityState.Exact, [], true, [], [])).ToArray();
        var id = await Publish("children", Sample(0, 0) with { Readings = many });
        var page = _repository.ReadPage(Request(id));
        var row = Assert.IsType<WordRow>(Assert.Single(page.Items));
        Assert.Equal("case-0000", row.CaseKey);
        Assert.Equal(300, row.Before.ObservedReadingCount);
        Assert.Equal(0, row.After.ObservedReadingCount);
        var child = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "readings", row.CaseKey, null, 200));
        Assert.Equal(200, child.Returned);
        Assert.Equal(ListAvailability.Complete, child.ListAvailability);
        Assert.NotNull(child.NextCursor);
        var remainder = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "readings", row.CaseKey, child.NextCursor, 200));
        Assert.Equal(100, remainder.Returned);
    }

    [Fact]
    public async Task FrozenExpectationsAndSourcesHaveTypedChildPages()
    {
        var morphology = new ApprovedMorphology([new ApprovedMorph("11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222", null, [])]);
        var assertion = new ReviewAssertion("expected", "identity", morphology, ReferenceAuthority.Human,
            "actor", "revision", true, false, true, ["source"]);
        var source = new SourceRow("source", "citation", "line", null, false, "actor");
        var item = Sample(0, 0) with
        {
            Assertions = [assertion], Sources = [source],
            Before = new ReviewCaseSideFacts(Completion.Complete, true, [], ["expected"], [], []),
            After = new ReviewCaseSideFacts(Completion.Complete, false, [], [], ["expected"], [])
        };
        var id = await Publish("witnesses", item);
        var word = Assert.IsType<WordRow>(Assert.Single(_repository.ReadPage(Request(id)).Items));
        Assert.Equal(1, word.ExpectationsTotal);
        Assert.Equal(1, word.Before.ApprovedMatched);
        Assert.Equal(1, word.After.ApprovedMissing);
        var expectation = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "expectations", item.CaseKey));
        var row = Assert.IsType<ExpectationRow>(Assert.Single(expectation.Items));
        Assert.Equal(ExpectationOutcome.Matched, row.Before);
        Assert.Equal(ExpectationOutcome.Missing, row.After);
        var sources = _repository.ReadChildren(new ReviewChildPageRequest("project", id, "sources", item.CaseKey));
        Assert.Equal("citation", Assert.IsType<SourceRow>(Assert.Single(sources.Items)).Citation);
        Assert.Equal(ListAvailability.Complete, sources.ListAvailability);
    }

    [Fact]
    public async Task LargeStreamReadsOnlyOneBoundedPage()
    {
        var id = await _repository.PublishAsync(Publication("large") with { EstimatedDetailBytes = 50_000_000 },
            LargeCases(), CancellationToken.None);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = _repository.ReadPage(Request(id) with { Limit = 10 });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10, page.Returned);
        Assert.Equal(5_000, page.TotalMatching);
        Assert.NotNull(page.NextCursor);
        Assert.True(allocated < 4_000_000, $"A ten-row read allocated {allocated} bytes.");
    }

    private static async IAsyncEnumerable<ReviewCaseDelta> LargeCases()
    {
        for (var i = 0; i < 5_000; i++)
        {
            yield return Sample(i, 0);
            if (i % 128 == 0) await Task.Yield();
        }
    }

    [Fact]
    public async Task DamagedReadingIsNeverSubstituted()
    {
        var reading = new ReadingRow("identity", Side.Before, IdentityState.Exact, [], true, [], []);
        var id = await Publish("damaged", Sample(0, 0) with { Readings = [reading] });
        var artifact = Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "project.motif.db.review-readings"), "*.json", SearchOption.AllDirectories));
        File.WriteAllText(artifact, "damaged");
        Assert.Throws<InvalidDataException>(() => _repository.ReadChildren(new ReviewChildPageRequest("project", id, "readings", "case-0000")));
    }

    [Fact]
    public async Task DeferReleasesReadingArtifacts()
    {
        var reading = new ReadingRow("identity", Side.Before, IdentityState.Exact, [], true, [], []);
        var id = await Publish("release", Sample(0, 0) with { Readings = [reading] });
        var root = Path.Combine(_root, "project.motif.db.review-readings");
        Assert.Single(Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories));
        new ProposalRepository(_database).SetStatus(_proposal, "deferred", null);
        Assert.Empty(Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories));
        Assert.Throws<ReviewDetailUnavailableException>(() => _repository.ReadPage(Request(id)));
    }

    [Fact]
    public async Task LimitMayChangeWithoutDuplicateOrMissingRows()
    {
        var id = await Publish("limits", Enumerable.Range(0, 17).Select(i => Sample(i, 0)).ToArray());
        var first = _repository.ReadPage(Request(id) with { Limit = 5 });
        var second = _repository.ReadPage(Request(id) with { Limit = 12, Cursor = first.NextCursor });
        Assert.Equal(17, first.Items.Concat(second.Items).Cast<WordRow>().Select(row => row.CaseKey).Distinct().Count());
    }

    [Fact]
    public async Task PurgedDetailDoesNotRestartAtFirstPage()
    {
        var id = await Publish("purge", Sample(0, 0), Sample(1, 0));
        var cursor = _repository.ReadPage(Request(id) with { Limit = 1 }).NextCursor;
        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            ReviewReportRetention.DropDetail(connection, transaction, _proposal.Value, "2026-01-01T00:00:00Z");
            transaction.Commit();
        }
        Assert.Throws<ReviewDetailUnavailableException>(() => _repository.ReadPage(Request(id) with { Cursor = cursor }));
    }

    private Task<ReviewReportId> Publish(string digest, params ReviewCaseDelta[] cases) =>
        _repository.PublishAsync(Publication(digest), Cases(cases), CancellationToken.None);
    private ReviewPageRequest Request(ReviewReportId id) => new("project", id, "wordforms");
    private static ReviewCaseDelta Sample(int i, int count) => Case("case-" + i.ToString("D4")) with
    {
        Memberships = Enumerable.Range(0, count).Select(n => new ReviewMembership("set", "member-" + n, "sampled-tokens", 1, null, null, [])
            { OccurrenceId = "occurrence-" + n, SourceLocator = "place-" + n }).ToArray()
    };

    private ReviewReportPublication Publication(string digest) =>
        new("project", _proposal.Value, "trial", "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digest))),
            Report(), [], 1_000_000);

    private NewAssessmentRecord Assessment(string id) => new(
        AssessmentId: id, ProposalId: _proposal, ProposalIntentDigest: "sha256:intent", Assessor: "pangloss",
        Kind: "Correctness", ScopeJson: "{}", ScopeDigest: "sha256:scope",
        Subject: new AssessmentSubject.Words(Selection.Create("selection", ["sample"]), "whitespace", "1"),
        BaselineToken: "{}", OutcomeDigest: "sha256:outcome", SemanticDigest: "sha256:semantic",
        GrammarSourceSha256: "sha256:grammar", ModelFingerprint: "fingerprint", Pipeline: "pipeline",
        DiagnosticCount: 0, Material: new AssessmentRecordMaterial.Words([], null, null, []), SavedUtc: null);

    private static ReviewCaseDelta Case(string key) => new(key, key, key, "ws", false, false, false, [], [], [], []);
    private static async IAsyncEnumerable<ReviewCaseDelta> Cases(params ReviewCaseDelta[] cases)
    {
        foreach (var item in cases) { yield return item; await Task.Yield(); }
    }
    private static async IAsyncEnumerable<ReviewCaseDelta> Interrupted()
    {
        yield return Case("a");
        await Task.Yield();
        throw new InvalidOperationException("interrupted");
    }

    private ReportView Report()
    {
        var report = (ReportView)Create(typeof(ReportView));
        return report with
        {
            Schema = "motif.proposal-review-report", SchemaVersion = 1,
            Pins = report.Pins with { ProjectKey = "project", ProposalId = _proposal.Value, TrialJobId = "trial" },
            Summary = report.Summary with
            {
                Notice = "No red flags means only 'not bad', never 'good'.",
                Incomplete = report.Summary.Incomplete with { Label = "Incomplete" },
            },
            FlagEvidence = Enum.GetValues<ReviewFlagId>().Select(id => new FlagEvidence(id, EvidenceState.Unavailable, [], 0)).ToArray(),
        };
    }
    private static object Create(Type type)
    {
        if (type == typeof(BaselineToken)) return new BaselineToken("project", "sha256:" + new string('a', 64), "v1",
            "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64));
        if (type == typeof(string)) return "test";
        if (type == typeof(long)) return 1L;
        if (type == typeof(int)) return 1;
        if (type == typeof(double)) return 1d;
        if (type == typeof(bool)) return false;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (type == typeof(ReadTarget)) return new ReadTarget(TargetKind.Resource, "test", "motif://test", null, null);
        if (type == typeof(EvidencePin)) return Create(typeof(SetEvidencePin));
        if (type == typeof(FlagFacts) || type == typeof(WorkChange)) return new WorkChange(null, 0, "test", WorkComparison.NotComparable, []);
        var constructor = type.GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(parameter =>
            Nullable.GetUnderlyingType(parameter.ParameterType) is not null ||
            new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable
                ? null : Create(parameter.ParameterType)).ToArray());
    }
    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
