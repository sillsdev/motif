using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class EvidenceSetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.EvidenceSetsTests",
        Guid.NewGuid().ToString("N"));

    public EvidenceSetsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CurrentNamesTheLatestParseTimeRunAndItsSameInvocationMeasurements()
    {
        using var fixture = new ProjectFixture(_root);
        var invocation = Invocation("invocation/current");
        var repository = new AssessmentRepository(fixture.Database);
        repository.Record(fixture.Assessment("root", AssessmentKinds.ParseTime, ["cat", "dog"],
            "2026-09-24T11:00:00Z") with { Material = fixture.Assessment("root", AssessmentKinds.ParseTime, ["cat", "dog"],
            "2026-09-24T11:00:00Z").RequireWordMaterial() with { Invocation = invocation } });
        repository.Record(fixture.Assessment("correctness", AssessmentKinds.Correctness, ["cat", "dog"],
            "2026-09-24T11:00:00Z") with { Material = fixture.Assessment("correctness", AssessmentKinds.Correctness, ["cat", "dog"],
            "2026-09-24T11:00:00Z").RequireWordMaterial() with { Invocation = invocation } });
        repository.Record(fixture.Assessment("object-timing", AssessmentKinds.ObjectTiming, ["cat", "dog"],
            "2026-09-24T11:00:00Z") with { Material = fixture.Assessment("object-timing", AssessmentKinds.ObjectTiming, ["cat", "dog"],
            "2026-09-24T11:00:00Z").RequireWordMaterial() with { Invocation = invocation } });
        repository.Record(fixture.Assessment("rerun", AssessmentKinds.ParseTime, ["cat"],
            "2026-09-24T11:05:00Z") with { ReplacesAssessmentId = "root" });

        var result = EvidenceSets.Current(fixture.Database, fixture.Project);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var set = result.Value!;
        Assert.Equal("root", set.RootAssessmentId);
        Assert.Equal(["rerun"], set.ReplacementAssessmentIds);
        Assert.Equal("correctness", set.MeasurementIds[AssessmentKinds.Correctness]);
        Assert.Equal("object-timing", set.MeasurementIds[AssessmentKinds.ObjectTiming]);
        Assert.Equal(EvidenceOriginKind.Project, set.Origin.Kind);
    }

    [Fact]
    public void CurrentIsNullBeforeAnyAssessment()
    {
        using var fixture = new ProjectFixture(_root);

        var result = EvidenceSets.Current(fixture.Database, fixture.Project);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Null(result.Value);
    }

    [Fact]
    public void CurrentNeverSelectsATrialAssessment()
    {
        using var fixture = new ProjectFixture(_root);
        var repository = new AssessmentRepository(fixture.Database);
        var proposalId = CanonicalId.FromGuid(Guid.NewGuid());
        new ProposalRepository(fixture.Database).SaveRevision(new ProposalRevisionRecord(proposalId,
            "sha256:" + new string('2', 64), "{}", "proposed", null, null, null));
        repository.Record(fixture.Assessment("project-root", AssessmentKinds.ParseTime, ["cat", "dog"],
            "2026-09-24T11:00:00Z"));
        repository.Record(fixture.Assessment("trial-root", AssessmentKinds.ParseTime, ["cat", "dog"],
            "2026-09-24T12:00:00Z") with
        {
            ProposalId = proposalId,
            ProposalIntentDigest = "sha256:" + new string('b', 64),
        });

        var result = EvidenceSets.Current(fixture.Database, fixture.Project);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("project-root", result.Value!.RootAssessmentId);
        Assert.Equal(EvidenceOriginKind.Project, result.Value.Origin.Kind);
    }

    [Fact]
    public void SnapshotCarriesTheSameSetCurrentReturns()
    {
        using var fixture = new ProjectFixture(_root);
        new AssessmentRepository(fixture.Database).Record(fixture.Assessment("root", AssessmentKinds.ParseTime,
            ["cat", "dog"], "2026-09-24T11:00:00Z"));

        var current = EvidenceSets.Current(fixture.Database, fixture.Project);
        var snapshot = CurrentEvidenceQuery.ReadCurrentEvidence(fixture.Database, fixture.Project,
            includeResolvedReadings: false, includeWordContext: false);

        Assert.True(current.Succeeded, current.Refusal?.Message);
        Assert.True(snapshot.Succeeded, snapshot.Refusal?.Message);
        Assert.Equal(current.Value!.Baseline, snapshot.Value!.Set!.Baseline);
        Assert.Equal(current.Value.RootAssessmentId, snapshot.Value.Set.RootAssessmentId);
        Assert.Equal(current.Value.MeasuredWords, snapshot.Value.Set.MeasuredWords);
        Assert.Equal(current.Value.Origin, snapshot.Value.Set.Origin);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static BatchInvocationEvidence Invocation(string id) => new(id, "source", "digest", "digest",
        "words", "digest", "tsv", "digest", "stderr", "digest", 1000, StepCap.Default, 1, false);

    private sealed class ProjectFixture : IDisposable
    {
        private readonly string _root;
        private readonly Guid _textId = Guid.NewGuid();

        public ProjectFixture(string testRoot)
        {
            _root = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, "project.fwdata");
            File.WriteAllText(path, "synthetic project marker");
            Project = new ProjectLocator(path, "project");
            Database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), Project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
            TokenJson = JsonSerializer.Serialize(token, MotifJson.CreateOptions());
            var baselineRoot = Path.Combine(_root, "baseline");
            new BaselineRepository(Database).Record(ProjectWorkspaceKey.Compute(Project),
                new BaselinePublication(baselineRoot, Path.Combine(baselineRoot, "project.fwdata"), token),
                DateTimeOffset.Parse("2026-09-24T10:30:00Z"), DateTimeOffset.Parse("2026-09-24T10:00:00Z"),
                TestTextWords.Empty, new ProjectSummarySnapshot(2, 2, 2, 0, 0, ["cat", "dog"],
                    [new ProjectTextSummary(_textId, "Text", 2, 2,
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["cat"] = 1, ["dog"] = 1 })]));
            new NamedSelectionRepository(Database).SetDefault("Default", [_textId], [],
                SelectionLimitPolicy.Estimated(StepCap.Default), null);
        }

        public ProjectLocator Project { get; }
        public MotifDatabase Database { get; }
        private string TokenJson { get; }

        public NewAssessmentRecord Assessment(string id, string kind, IReadOnlyList<string> words, string savedUtc) =>
            new NewAssessmentRecord(
            AssessmentId: id,
            ProposalId: null,
            ProposalIntentDigest: null,
            Assessor: "pangloss",
            Kind: kind,
            ScopeJson: "{}",
            ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(Selection.Create("Default", words), "whitespace", "1"),
            BaselineToken: TokenJson,
            OutcomeDigest: "sha256:outcome",
            SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar",
            ModelFingerprint: "fingerprint",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words(words.Select(word => new AssessedWord(word, "analysed", [])).ToArray(), null, null, []),
            SavedUtc: savedUtc
        );

        public void Dispose() => Database.Dispose();
    }
}
