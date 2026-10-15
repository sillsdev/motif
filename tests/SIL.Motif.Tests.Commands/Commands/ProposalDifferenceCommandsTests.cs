using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProposalDifferenceCommandsTests : IDisposable
{
    private const string FormA = "11111111-1111-1111-1111-111111111111";
    private const string FormB = "22222222-2222-2222-2222-222222222222";
    private const string Msa = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProposalDifferenceTests",
        Guid.NewGuid().ToString("N"));

    public ProposalDifferenceCommandsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ImprovedApprovedWordAndNewlyAcceptedMisspellingAreFiledSeparately()
    {
        using var fixture = new Fixture(_root);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        var summary = produced.Value!.Summary;
        Assert.Equal(2, summary.Words);
        Assert.Equal(3, summary.Occurrences);
        Assert.Equal(0, summary.UnboundOccurrences);
        Assert.Equal(2, summary.CouldBeBadWords);
        Assert.Equal(1, summary.ImprovedWords);
        Assert.Contains(summary.Matrix, cell => cell is { BeforeState: "no-approved-match", AfterState: "approved-unique", Words: 1 });
        var sita = Assert.Single(produced.Value.Words, word => word.Word == "sita");
        Assert.Equal(2, sita.Occurrences);
        Assert.Contains(ProposalDifferenceBucket.ImprovedApprovedMatch, sita.Buckets);
        Assert.Contains(ProposalDifferenceBucket.AnalysisChanged, sita.Buckets);
        Assert.DoesNotContain(sita.Buckets, bucket => bucket is ProposalDifferenceBucket.Regression or ProposalDifferenceBucket.Overgeneration);
        var mutu = Assert.Single(produced.Value.Words, word => word.Word == "mutu");
        Assert.Contains(ProposalDifferenceBucket.Overgeneration, mutu.Buckets);
    }

    [Fact]
    public void ShowFiltersToCouldBeBadWordsAndPagesTheMatches()
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        var bad = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: produced.Summary.DifferenceId, CouldBeBad: true, Bucket: "delta.overgeneration")).Value!;
        var onlyBucket = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Bucket: "delta.improved.approved-match")).Value!;
        var paged = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: produced.Summary.DifferenceId, Limit: 1)).Value!;

        Assert.Equal(["mutu"], bad.Words.Select(word => word.Word));
        Assert.Equal(["sita"], onlyBucket.Words.Select(word => word.Word));
        Assert.Equal(2, paged.MatchingWords);
        Assert.Equal(1, paged.NextOffset);
        Assert.Equal(JsonSerializer.Serialize(produced.Summary, MotifJson.CreateOptions()),
            JsonSerializer.Serialize(paged.Summary, MotifJson.CreateOptions()));
    }

    [Fact]
    public void ABadBucketCodeAndAMissingTrialAreRefusedWithNamedCodes()
    {
        using var fixture = new Fixture(_root);

        var bucket = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Bucket: "delta.nonsense"));
        var none = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId));
        var noTrial = ProposalDifferenceCommands.Produce(new ProposalDifferenceProduceRequest(fixture.ProjectPath,
            CanonicalId.Mint("proposal/").Value, fixture.TextId.ToString("D")));

        Assert.Equal("difference.invalid-bucket", bucket.Refusal!.Code);
        Assert.Equal("difference.none", none.Refusal!.Code);
        Assert.Equal("difference.no-trial", noTrial.Refusal!.Code);
    }

    [Fact]
    public void ARecordedBaselineAfterTheTrialRefusesBecauseTheDecisionsMayDiffer()
    {
        using var fixture = new Fixture(_root);
        fixture.RecordBaseline("sha256:" + new string('9', 64), "2026-09-24T12:00:00Z", semantic: '7');

        var outcome = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.Equal("difference.baseline-changed", outcome.Refusal!.Code);
    }

    [Fact]
    public void AWordOnlyMeasuredAfterTheProposalIsKeptWithNotMeasuredBefore()
    {
        using var fixture = new Fixture(_root, beforeOmitsSita: true);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        var sita = Assert.Single(produced.Value!.Words, word => word.Word == "sita");
        Assert.Equal(HumanAgreementApprovalState.NotMeasured, sita.Facts.Before.ApprovedState);
        Assert.False(sita.Facts.CompleteComparison);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dir;
        private readonly string _tokenJson;
        private readonly bool _beforeOmitsSita;

        public Fixture(string testRoot, bool beforeOmitsSita = false)
        {
            _beforeOmitsSita = beforeOmitsSita;
            _dir = System.IO.Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ProjectPath = System.IO.Path.Combine(_dir, "project.fwdata");
            File.WriteAllText(ProjectPath, "synthetic project marker");
            Project = new ProjectLocator(ProjectPath, "project");
            Database = MotifDatabase.OpenOwned(System.IO.Path.Combine(_dir, "project.motif.db"), Project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            Token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
            _tokenJson = JsonSerializer.Serialize(Token, MotifJson.CreateOptions());
            RecordBaseline(Token.BundleDigest, Token.CapturedUtc);
            Seed();
        }

        public string ProjectPath { get; }
        public ProjectLocator Project { get; }
        public MotifDatabase Database { get; }
        public BaselineToken Token { get; }
        public Guid TextId { get; } = Guid.NewGuid();
        public string ProposalId { get; private set; } = "";

        public ProposalDifferenceProduceRequest ProduceRequest() =>
            new(ProjectPath, ProposalId, TextId.ToString("D"));

        public void RecordBaseline(string bundleDigest, string capturedUtc, char semantic = '1')
        {
            var token = new BaselineToken("project-id", "sha256:" + new string(semantic, 64), "projection-v1", capturedUtc,
                bundleDigest);
            var baselineRoot = System.IO.Path.Combine(_dir, "baseline-" + capturedUtc.Replace(':', '-'));
            var segment = Guid.NewGuid();
            var paragraph = Guid.NewGuid();
            var sitaId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var mutuId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var analysisId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var identity = new ApprovedMorphology([new ApprovedMorph(FormA, Msa, null, ["sita"])])
            { SourceAnalysisId = CanonicalId.FromGuid(analysisId).Value };
            TextWordsProjectedToken Token(string form, Guid wordform, int index) => new(form,
                [new WritingSystemText(form, "swh")], wordform, "analysed", null, null, null, null, index, null);
            var text = new TextWordsProjectedText(TextId, "Hadithi",
                [new TextWordsProjectedLine(1, "sita mutu sita .", [Token("sita", sitaId, 0), Token("mutu", mutuId, 1),
                    Token("sita", sitaId, 2), new TextWordsProjectedToken(".", [new WritingSystemText(".", "swh")], null,
                        null, null, null, null, null, 3, null)], paragraph, segment, true)], []);
            var approved = new TextWordsProjectedAnalysis("k1", [])
            { AnalysisId = analysisId, Opinion = ReadingGrade.Approved, Identity = identity };
            new BaselineRepository(Database).Record(ProjectWorkspaceKey.Compute(Project),
                new BaselinePublication(baselineRoot, System.IO.Path.Combine(baselineRoot, "project.fwdata"), token),
                DateTimeOffset.Parse("2026-09-24T10:30:00Z"), DateTimeOffset.Parse(capturedUtc),
                new TextWordsProjection([text],
                [
                    new TextWordsProjectedWordform(sitaId, [approved], [], 0, false, [approved]),
                    new TextWordsProjectedWordform(mutuId, [], [], 0, true, []),
                ]),
                new ProjectSummarySnapshot(2, 3, 2, 0, 0, ["sita", "mutu"],
                    [new ProjectTextSummary(TextId, "Hadithi", 2, 3,
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["sita"] = 2, ["mutu"] = 1 })]));
        }

        private void Seed()
        {
            var proposals = new ProposalRepository(Database);
            var id = CanonicalId.Mint("proposal/");
            var json = ProposalCommands.BuildProposalJson(new DraftDocument
            {
                ProposalId = id.Value,
                ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
                Operations =
                [
                    new DraftOperation
                    {
                        OperationId = CanonicalId.Mint("operation/").Value,
                        Kind = "lexical/lexSense/setGloss",
                        Target = CanonicalId.Mint("lexSense/").Value,
                        After = JsonDocument.Parse("{\"ws\":\"en\",\"text\":\"x\"}").RootElement.EnumerateObject()
                            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal),
                    },
                ],
            });
            var digest = IntentDigest.Compute(ProposalJsonParser.Parse(json));
            proposals.SaveRevision(new ProposalRevisionRecord(id, digest, json, "proposed", null, null, null));
            ProposalId = id.Value;

            var assessments = new AssessmentRepository(Database);
            Record(assessments, "before", null, null, Word("sita", 0, Reading(FormB)), Word("mutu", 1));
            Record(assessments, "after", id, digest, Word("sita", 0, Reading(FormA)), Word("mutu", 1, Reading(FormB)));
            var jobs = new JobRepository(Database);
            var input = JsonSerializer.Serialize(new
            {
                proposalJson = json, scope = (string?)null, words = new[] { "sita", "mutu" }, allWords = false,
                limits = (TrialLimits?)null, baselineAssessmentIds = new[] { "before" },
            }, MotifJson.CreateOptions());
            var job = jobs.Create("trial-1", ProjectWorkspaceKey.Compute(Project), TrialJobHandler.TrialKind, input,
                "2026-09-24T11:00:00Z");
            job = jobs.Transition(job.JobId, JobStatus.Running);
            job = jobs.PublishDryRun(job.JobId, DryRun(digest), job.Version);
            jobs.Transition(job.JobId, JobStatus.Completed, JsonSerializer.Serialize(new
            {
                baselineToken = Token, assessmentIds = new[] { "after" }, allWords = false,
            }, MotifJson.CreateOptions()));
        }

        private void Record(AssessmentRepository assessments, string id, CanonicalId? proposal, string? digest,
            params AssessedWord[] words)
        {
            if (_beforeOmitsSita && id == "before") words = words.Where(word => word.Word != "sita").ToArray();
            assessments.Record(new NewAssessmentRecord(id, proposal, digest, "pangloss", AssessmentKinds.ParseTime, "{}",
                "sha256:scope", "whitespace", "1", _tokenJson, Selection.Create("Default", ["sita", "mutu"]),
                "sha256:outcome", "sha256:semantic", "sha256:grammar", "fingerprint", "pipeline", 0, words,
                SavedUtc: "2026-09-24T11:00:00Z"));
        }

        private static AssessedWord Word(string word, int index, params ParseAnalysis[] readings) =>
            new(word, "analysed", []) { Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", index, word, 1,
                false, false, false, readings, []) };

        private static ParseAnalysis Reading(string form) => new([new ParseMorph(form, Msa, null, null)]);

        private static string DryRun(string digest) => JsonSerializer.Serialize(new
        {
            intentDigest = digest, baselineNote = "baseline", expectedEffects = Array.Empty<object>(),
            effectDigest = "sha256:" + new string('2', 64),
            anchor = new
            {
                intentDigest = digest, footprintDigest = "sha256:" + new string('3', 64),
                effectDigest = "sha256:" + new string('2', 64), runnerVersion = "1.0.0", libLcmVersion = "11.0.0",
                projectionVersion = "projection-v1", dryRunAtUtc = "2026-09-24T11:00:00Z",
            },
        }, MotifJson.CreateOptions());

        public void Dispose() => Database.Dispose();
    }
}
