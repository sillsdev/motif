using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Receipts;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class GeneratedArtifactReferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-artifact-refs-" + Guid.NewGuid().ToString("N"));

    public GeneratedArtifactReferencesTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CapturesBothDifferenceEdgesAndFrozenParsimonyCitationsFromADeclaredPrivateSnapshot()
    {
        using var database = OpenDatabase("one.fwdata");
        var token = NewBaseline('a');
        var invocation = new BatchInvocationEvidence("invocation-one", "/private/run/source.fwdata",
            Digest('1'), Digest('2'), "/private/run/words.txt", Digest('3'), "/private/run/result.tsv",
            Digest('4'), "/private/run/stderr.txt", Digest('5'), null, StepCap.Default, 2, false)
        {
            AnalysesPath = "/private/run/analyses.jsonl",
            AnalysesSha256 = Digest('6'),
        };
        var before = NewAssessment("assessment-before", token, invocation.InvocationId, null);
        var after = NewAssessment("assessment-after", token, invocation.InvocationId, before);
        var frozenRoot = "assessment-frozen-root";
        var frozenInput = "assessment-frozen-input";
        var frozenEvidenceInput = "assessment-frozen-evidence-input";
        var inputs = new ParsimonyReportInputs("bundle-one", token, "baseline", null, "model",
            new ParsimonyArtifactDigest(1, new string('a', 64)),
            new ParsimonyArtifactDigest(1, new string('b', 64)), null, null, [frozenInput]);
        var evidenceToken = NewBaseline('e');
        var evidenceInputs = new ParsimonyReportInputs("bundle-evidence-input", evidenceToken, "baseline", null,
            "model-evidence", new ParsimonyArtifactDigest(1, new string('c', 64)),
            new ParsimonyArtifactDigest(1, new string('d', 64)), null, null, [frozenEvidenceInput]);
        using var evidenceArguments = JsonDocument.Parse("{}");
        var finding = new ParsimonyFinding("finding-one", "measure-one", ParsimonyAxis.Parsimony,
            ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project-one"),
            null, new ParsimonyMeasureNumber(1, 1, "items"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "threshold/v1"),
            Digest('9'), [new ParsimonyEvidenceReference("bundle-finding", ParsimonyViews.ParserCases,
            evidenceArguments.RootElement.Clone())], "guide:measure-one", [], ParsimonyVerification.NotRun);
        var report = new ParsimonyReportResponse("report-one", inputs, [finding], "frozen report")
        {
            AssessmentIds = [frozenRoot],
            Notes = [],
        };
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var reportJson = JsonSerializer.Serialize(report, webJson);
        var evidenceJson = JsonSerializer.Serialize(new
        {
            inputs = evidenceInputs,
            grammarFactsPath = "grammar-facts.sqlite",
            evidencePath = "evidence.sqlite",
            findings = Array.Empty<object>(),
            joinQuality = (object?)null,
        }, webJson);

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO Proposals (ProposalId, Status) VALUES ('proposal-one', 'proposed');");
            Execute(connection, transaction,
                "INSERT INTO AssessmentInvocations (InvocationId, EvidenceJson) VALUES ($id, $evidence);",
                ("$id", invocation.InvocationId),
                ("$evidence", JsonSerializer.Serialize(invocation, MotifJson.CreateOptions())));
            InsertAssessment(connection, transaction, before);
            InsertAssessment(connection, transaction, after);
            InsertAssessment(connection, transaction, NewAssessment(frozenRoot, token, null, null));
            InsertAssessment(connection, transaction, NewAssessment(frozenInput, token, null, null));
            InsertAssessment(connection, transaction, NewAssessment("assessment-direct-report", token, null, null));
            InsertAssessment(connection, transaction, NewAssessment(frozenEvidenceInput, evidenceToken, null, null));
            Execute(connection, transaction,
                "INSERT INTO ProposalDifferences " +
                "(DifferenceId, ProposalId, TrialJobId, BeforeAssessmentId, AfterAssessmentId, TextId, " +
                "BaselineToken, SummaryJson, SummaryDigest, CreatedUtc) " +
                "VALUES ('difference-one', 'proposal-one', 'trial-one', $before, $after, 'text-one', " +
                "'{}', '{}', 'sha256:summary', '2026-01-01T00:00:00Z');",
                ("$before", before.Id), ("$after", after.Id));
            Execute(connection, transaction,
                "INSERT INTO Reports (ReportId, AssessmentId, ReportJson, EvidenceJson, CreatedUtc, Kind) " +
                "VALUES ('report-one', 'assessment-direct-report', $report, $evidence, '2026-01-01T00:00:00Z', 'parsimony');",
                ("$report", reportJson),
                ("$evidence", evidenceJson));
            transaction.Commit();
        }

        using var snapshotConnection = database.OpenConnection();
        using var snapshotTransaction = snapshotConnection.BeginTransaction();
        var snapshot = PrivateArtifactStoreSnapshot.FromPrivateCopy("project-one", ArtifactStoreKind.Project,
            MotifSchema.CurrentSchema, Digest('f'), snapshotConnection, snapshotTransaction);
        var domain = ArtifactReferenceDomainDeclaration.Explicit(["project-one"], true, "synthetic fixture");

        var references = GeneratedArtifactReferences.Capture([snapshot], domain);

        Assert.Equal(new[] { before.Id, after.Id, frozenRoot, frozenInput, frozenEvidenceInput,
                "assessment-direct-report" }.Order(StringComparer.Ordinal),
            references.AssessmentIds);
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == before.Id && reference.Reason == "proposal-difference-before");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == after.Id && reference.Reason == "proposal-difference-after");
        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Reports).IsComplete,
            string.Join(",", references.Diagnostics.Where(item => item.Family == ArtifactReferenceFamily.Reports)
                .Select(item => item.Code)) + " | " + string.Join(";", references.References.Where(item =>
                    item.Family == ArtifactReferenceFamily.Reports).Select(item => item.Target + ":" + item.Reason)));
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == frozenRoot && reference.Reason == "frozen-parsimony-report");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == frozenInput && reference.Reason == "frozen-parsimony-input");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == frozenEvidenceInput && reference.Reason == "frozen-parsimony-input");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == "assessment-direct-report" && reference.Reason == "report-direct-assessment");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == "bundle-finding" && reference.Reason == "frozen-parsimony-finding-evidence");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == "bundle-one" && reference.Reason == "frozen-parsimony-input-bundle");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == "bundle-evidence-input" && reference.Reason == "frozen-parsimony-input-bundle");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest &&
            reference.Target == token.BundleDigest && reference.Reason == "frozen-parsimony-input-baseline-token");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest &&
            reference.Target == evidenceToken.BundleDigest && reference.Reason == "frozen-parsimony-input-baseline-token");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Path &&
            reference.Target == "grammar-facts.sqlite" && reference.ScopeIdentity == evidenceInputs.BundleId &&
            reference.Reason == "frozen-parsimony-grammar-facts-relative");
        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.InvocationArtifacts).IsComplete,
            string.Join(",", references.Diagnostics.Where(item => item.Family == ArtifactReferenceFamily.InvocationArtifacts)
                .Select(item => item.Code)));
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Path &&
            reference.Target == invocation.SourcePath && reference.RetainingRecordId == "AssessmentInvocations/invocation-one");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Path &&
            reference.Target == invocation.AnalysesPath);
        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Assessments).IsComplete);
        Assert.False(references.ReferenceDomain.IsComplete);
    }

    [Fact]
    public void CapturesCurrentHistoryPinsRetainedMembersAndReplacementLineage()
    {
        using var database = OpenDatabase("history.fwdata");
        var token = NewBaseline('b');
        var invocation = NewInvocation("invocation-history", "/private/history/source.fwdata");
        var predecessor = NewAssessment("assessment-predecessor", token, invocation.InvocationId, null);
        var current = NewAssessment("assessment-current", token, invocation.InvocationId, predecessor);
        var correctness = NewAssessment("assessment-correctness", token, invocation.InvocationId, null);
        var timing = NewAssessment("assessment-timing", token, invocation.InvocationId, null);

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertInvocation(connection, transaction, invocation);
            InsertAssessment(connection, transaction, predecessor);
            InsertAssessment(connection, transaction, current);
            InsertAssessment(connection, transaction, correctness);
            InsertAssessment(connection, transaction, timing);
            Execute(connection, transaction, "UPDATE Assessments SET Kind='Correctness' WHERE AssessmentId=$id;",
                ("$id", correctness.Id));
            Execute(connection, transaction, "UPDATE Assessments SET Kind='ObjectTiming' WHERE AssessmentId=$id;",
                ("$id", timing.Id));
            Execute(connection, transaction, "UPDATE MotifMetadata SET CurrentAssessmentId=$id WHERE Id=1;",
                ("$id", current.Id));
            Execute(connection, transaction,
                "INSERT INTO AssessmentPins (AssessmentId, PinnedBy, PinnedUtc) VALUES ($id, 'review', '2026-01-01T00:00:00Z');",
                ("$id", correctness.Id));
            Execute(connection, transaction,
                "INSERT INTO RetainedInvocations (InvocationId, ProjectKey, BaselineToken, BaselineRootDirectory, " +
                "BaselineFwDataPath, BaselineSourceLastWriteUtc, BaselinePublishedUtc, SavedUtc, SelectionDescriptorJson, " +
                "SelectionDescriptorSha256, ExpectedKindsJson, Assessor, ScopeJson, ScopeDigest, ArtifactInvocationId) " +
                "VALUES ('retained-one', 'project-one', $token, '/private/baselines/b', '/private/baselines/b/project.fwdata', " +
                "'2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', '{}', $digest, '[]', 'pangloss', '{}', $digest, $invocation);",
                ("$token", JsonSerializer.Serialize(token, MotifJson.CreateOptions())),
                ("$digest", Digest('c')), ("$invocation", invocation.InvocationId));
            foreach (var (kind, assessment) in new[]
                     {
                         ("ParseTime", current), ("Correctness", correctness), ("ObjectTiming", timing),
                     })
                Execute(connection, transaction,
                    "INSERT INTO RetainedInvocationMembers (InvocationId, Kind, AssessmentId) VALUES " +
                    "('retained-one', $kind, $assessment);", ("$kind", kind), ("$assessment", assessment.Id));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.Contains(references.References, reference => reference.Target == current.Id && reference.Reason == "current-assessment");
        Assert.Contains(references.References, reference => reference.Target == correctness.Id && reference.Reason == "assessment-pin");
        Assert.Contains(references.References, reference => reference.Target == timing.Id && reference.Reason == "retained-invocation-member");
        Assert.Contains(references.References, reference => reference.Target == predecessor.Id && reference.Reason == "replacement-lineage");
        Assert.Equal(4, references.References.Count(reference => reference.Reason == "assessment-history"));
        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Assessments).IsComplete);
        Assert.True(references.InvocationProvenance.Single().HasAssessment);
    }

    [Fact]
    public void SharedInvocationReferencesDoNotEstablishFileOwnership()
    {
        using var first = OpenDatabase("first.fwdata");
        using var second = OpenDatabase("second.fwdata");
        var invocation = NewInvocation("shared-invocation", "/private/shared/source.fwdata");
        InsertInvocation(first, invocation);
        InsertInvocation(second, invocation);

        using var firstSnapshot = OpenSnapshot(first, "project-one", 'd');
        using var secondSnapshot = OpenSnapshot(second, "project-two", 'e');
        var references = GeneratedArtifactReferences.Capture([firstSnapshot.Snapshot, secondSnapshot.Snapshot],
            ArtifactReferenceDomainDeclaration.Explicit(["project-one", "project-two"], true, "two declared private fixture stores"));

        Assert.Equal(2, references.References.Count(reference => reference.TargetKind == ArtifactReferenceTargetKind.Path &&
            reference.Target == invocation.SourcePath));
        Assert.Equal(2, references.References.Count(reference => reference.TargetKind == ArtifactReferenceTargetKind.Invocation &&
            reference.Target == invocation.InvocationId));
        Assert.All(references.InvocationProvenance, provenance =>
        {
            Assert.True(provenance.HasDurableProvenance);
            Assert.False(provenance.HasAssessment);
            Assert.True(provenance.FilesUnknownOwned);
        });
        Assert.True(references.ReferenceDomain.IsComplete);

    }

    [Fact]
    public void MalformedRetryDoesNotHideReadableInvocationPaths()
    {
        using var database = OpenDatabase("invocation-malformed-retry.fwdata");
        var retry = new StoppedWordRetry(1200, 1, 1, 1, 0, "retry-invocation", Digest('a'), Digest('b'),
            Digest('c'), Digest('d'), "0\tword\t100\t200\trecovered");
        var invocation = NewInvocation("invocation-with-malformed-retry", "/private/retry/source.fwdata") with
        {
            StoppedWordRetry = retry,
        };
        var malformed = JsonNode.Parse(JsonSerializer.Serialize(invocation, MotifJson.CreateOptions()))!.AsObject();
        malformed["StoppedWordRetry"]!["LimitMs"] = "invalid";
        InsertInvocation(database, NewInvocation("invocation-later-valid", "/private/later/source.fwdata"));
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO AssessmentInvocations (InvocationId, EvidenceJson) VALUES ($id, $evidence);",
                ("$id", invocation.InvocationId), ("$evidence", malformed.ToJsonString()));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item =>
            item.Family == ArtifactReferenceFamily.InvocationArtifacts).IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Code == "invocation-evidence-unrecognized" &&
            item.RecordId == "AssessmentInvocations/invocation-with-malformed-retry");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Path &&
            item.Target == "/private/retry/source.fwdata" && item.Reason == "invocation-source-path");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Path &&
            item.Target == "/private/run/words.txt" && item.Reason == "invocation-words-path");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Path &&
            item.Target == "/private/run/result.tsv" && item.Reason == "invocation-tsv-path");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Path &&
            item.Target == "/private/run/stderr.txt" && item.Reason == "invocation-stderr-path");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Invocation &&
            item.Target == "invocation-with-malformed-retry");
    }

    [Fact]
    public void InvocationAnalysesPathAndDigestMustBePaired()
    {
        using var database = OpenDatabase("invocation-analyses-unpaired.fwdata");
        var invocation = NewInvocation("invocation-unpaired-analyses", "/private/analyses/source.fwdata") with
        {
            AnalysesSha256 = Digest('a'),
        };
        InsertInvocation(database, invocation);

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item =>
            item.Family == ArtifactReferenceFamily.InvocationArtifacts).IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Code == "invocation-evidence-unrecognized" &&
            item.RecordId == "AssessmentInvocations/invocation-unpaired-analyses");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Path &&
            item.Target == "/private/analyses/source.fwdata" && item.Reason == "invocation-source-path");
    }

    [Fact]
    public void InvocationAnalysesPathWithoutDigestIsRetainedAsUncertain()
    {
        using var database = OpenDatabase("invocation-analyses-path-without-digest.fwdata");
        var invocation = NewInvocation("invocation-path-without-digest", "/private/source.fwdata") with
        {
            AnalysesPath = "/private/analyses.jsonl",
        };
        InsertInvocation(database, invocation);

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item =>
            item.Family == ArtifactReferenceFamily.InvocationArtifacts).IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Code == "invocation-evidence-unrecognized" &&
            item.RecordId == "AssessmentInvocations/invocation-path-without-digest");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Path &&
            item.Target == "/private/analyses.jsonl" && item.Reason == "invocation-analyses-path");
    }

    [Fact]
    public void PreservesRawBaselinePathsWhenItsTokenCannotResolve()
    {
        using var database = OpenDatabase("baseline.fwdata");
        var publishedToken = NewBaseline('f');
        var unresolvedToken = NewBaseline('e');
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO Baselines (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, " +
                "CapturedUtc, BundleDigest, CapturedHostSessionId, CapturedEditGeneration, RootDirectory, FwDataPath, " +
                "PublishedUtc, SourceLastWriteUtc) VALUES ('project-one', $project, $semantic, $projection, $captured, " +
                "$bundle, NULL, NULL, $root, $path, $published, $source);",
                ("$project", publishedToken.ProjectIdentity), ("$semantic", publishedToken.SemanticSnapshotDigest),
                ("$projection", publishedToken.ProjectionVersion), ("$captured", publishedToken.CapturedUtc),
                ("$bundle", publishedToken.BundleDigest), ("$root", "/private/baselines/current"),
                ("$path", "/private/baselines/current/project.fwdata"), ("$published", publishedToken.CapturedUtc),
                ("$source", publishedToken.CapturedUtc));
            InsertAssessment(connection, transaction, NewAssessment("assessment-unresolved", unresolvedToken, null, null));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Path &&
            reference.Target == "/private/baselines/current" && reference.Reason == "raw-baseline-root");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Path &&
            reference.Target == "/private/baselines/current/project.fwdata" && reference.Reason == "raw-baseline-fwdata");
        Assert.False(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Baselines).IsComplete);
        Assert.Contains(references.Diagnostics, diagnostic => diagnostic.Code == "baseline-token-path-unresolved");
        Assert.True(references.ReferenceDomain.IsComplete);
    }

    [Fact]
    public void UnreadableAndUnsupportedDeclaredStoresMakeTheReferenceDomainIncomplete()
    {
        var unreadable = PrivateArtifactStoreSnapshot.Unreadable("unreadable", ArtifactStoreKind.Project,
            MotifSchema.CurrentSchema, Digest('h'), "fixture read failure");
        var unreadableReferences = GeneratedArtifactReferences.Capture([unreadable],
            ArtifactReferenceDomainDeclaration.Explicit(["unreadable"], true, "declared unreadable fixture"));
        Assert.False(unreadableReferences.ReferenceDomain.IsComplete);
        Assert.All(unreadableReferences.FamilyCompleteness.Where(item => item.Family != ArtifactReferenceFamily.ParsimonyEvidence),
            family => Assert.False(family.IsComplete));
        Assert.Contains(unreadableReferences.Diagnostics, diagnostic => diagnostic.Code == "declared-store-unreadable");

        using var database = OpenDatabase("unsupported.fwdata");
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PrivateArtifactStoreSnapshot.FromPrivateCopy(
                "invalid-kind", (ArtifactStoreKind)int.MaxValue, MotifSchema.CurrentSchema, Digest('i'),
                connection, transaction));
            transaction.Rollback();
        }
        using var unsupported = OpenSnapshot(database, "unsupported", 'i', MotifSchema.CurrentSchema - 1);
        var unsupportedReferences = GeneratedArtifactReferences.Capture([unsupported.Snapshot],
            ArtifactReferenceDomainDeclaration.Explicit(["unsupported"], true, "declared unsupported fixture"));
        Assert.False(unsupportedReferences.ReferenceDomain.IsComplete);
        Assert.All(unsupportedReferences.FamilyCompleteness.Where(item => item.Family != ArtifactReferenceFamily.ParsimonyEvidence),
            family => Assert.False(family.IsComplete));
        Assert.Contains(unsupportedReferences.Diagnostics,
            diagnostic => diagnostic.Code == "unsupported-or-invalid-store-snapshot");
    }

    [Theory]
    [InlineData("omitted", true)]
    [InlineData("text-empty", true)]
    [InlineData("null", false)]
    [InlineData("unknown", false)]
    [InlineData("missing-required", false)]
    public void FrozenParsimonyRootAssessmentIdsUseTheStoredOptionalShape(string shape, bool expectedComplete)
    {
        using var database = OpenDatabase("report-shape.fwdata");
        var token = NewBaseline('c');
        var inputs = new ParsimonyReportInputs("bundle-shape", token, "baseline", null, "model",
            new ParsimonyArtifactDigest(1, new string('a', 64)),
            new ParsimonyArtifactDigest(1, new string('b', 64)), null, null, []);
        var response = new ParsimonyReportResponse("report-shape", inputs, [], "frozen report") { Notes = [] };
        var json = JsonNode.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!
            .AsObject();
        if (shape == "omitted") json.Remove("assessmentIds");
        else if (shape == "text-empty") json["text"] = "";
        else if (shape == "null") json["assessmentIds"] = null;
        else if (shape == "missing-required") json.Remove("reportId");
        else json["futureAssessmentReferences"] = new JsonArray("future-assessment");
        var reportJson = json.ToJsonString();
        var evidenceJson = JsonSerializer.Serialize(new
        {
            inputs,
            grammarFactsPath = "grammar-facts.sqlite",
            evidencePath = "evidence.sqlite",
            findings = Array.Empty<object>(),
            joinQuality = (object?)null,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO Reports (ReportId, ReportJson, EvidenceJson, CreatedUtc, Kind) " +
                "VALUES ('report-shape', $report, $evidence, '2026-01-01T00:00:00Z', 'parsimony');",
                ("$report", reportJson), ("$evidence", evidenceJson));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.Equal(expectedComplete,
            references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Reports).IsComplete);
        if (!expectedComplete)
            Assert.Contains(references.Diagnostics, diagnostic => diagnostic.Family == ArtifactReferenceFamily.Reports &&
                diagnostic.Code == "frozen-report-reference-unrecognized");
        Assert.DoesNotContain(references.AssessmentIds, id => id == "future-assessment");
    }

    [Theory]
    [InlineData("response-input-null")]
    [InlineData("response-assessment-list-null")]
    [InlineData("response-input-assessment-list-null")]
    [InlineData("evidence-input-assessment-list-null")]
    [InlineData("evidence-path-missing")]
    [InlineData("finding-reference-null")]
    [InlineData("response-text-null")]
    [InlineData("finding-id-empty")]
    [InlineData("finding-id-whitespace")]
    [InlineData("finding-digest-empty")]
    [InlineData("finding-digest-whitespace")]
    [InlineData("row-id-mismatch")]
    public void MalformedFrozenReportMembersAreContained(string defect)
    {
        using var database = OpenDatabase("malformed-report-" + defect + ".fwdata");
        var token = NewBaseline('a');
        var responseInputs = NewParsimonyInputs("bundle-response", token, ["assessment-response-input"]);
        using var findingArguments = JsonDocument.Parse("{}");
        var finding = new ParsimonyFinding("finding-frozen", "measure-one", ParsimonyAxis.Parsimony,
            ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project-one"),
            null, new ParsimonyMeasureNumber(1, 1, "items"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "threshold/v1"),
            Digest('9'), [new ParsimonyEvidenceReference("bundle-frozen-finding", ParsimonyViews.ParserCases,
                findingArguments.RootElement.Clone())], "guide:measure-one", [], ParsimonyVerification.NotRun);
        var report = new ParsimonyReportResponse("report-row", responseInputs, [finding], "frozen report")
        {
            AssessmentIds = ["assessment-response"],
            Notes = [],
        };
        var reportNode = JsonNode.Parse(JsonSerializer.Serialize(report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        var evidenceInputs = new ParsimonyReportInputs("bundle-evidence", NewBaseline('b'), "baseline", null,
            "model-evidence", new ParsimonyArtifactDigest(1, new string('c', 64)),
            new ParsimonyArtifactDigest(1, new string('d', 64)), null, null, ["assessment-evidence"]);
        var evidenceNode = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            inputs = evidenceInputs,
            grammarFactsPath = "grammar-facts.sqlite",
            evidencePath = "evidence.sqlite",
            findings = Array.Empty<object>(),
            joinQuality = (object?)null,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        if (defect == "response-input-null") reportNode["inputs"] = null;
        if (defect == "response-assessment-list-null") reportNode["assessmentIds"] = null;
        if (defect == "response-input-assessment-list-null") reportNode["inputs"]!["assessmentIds"] = null;
        if (defect == "finding-reference-null") reportNode["findings"] = new JsonArray((JsonNode?)null);
        if (defect == "response-text-null") reportNode["text"] = null;
        if (defect == "finding-id-empty") reportNode["findings"]![0]!["findingId"] = "";
        if (defect == "finding-id-whitespace") reportNode["findings"]![0]!["findingId"] = " \t";
        if (defect == "finding-digest-empty") reportNode["findings"]![0]!["evidenceDigest"] = "";
        if (defect == "finding-digest-whitespace") reportNode["findings"]![0]!["evidenceDigest"] = " \t";
        if (defect == "row-id-mismatch") reportNode["reportId"] = "different-report-id";
        if (defect == "evidence-input-assessment-list-null") evidenceNode["inputs"]!["assessmentIds"] = null;
        if (defect == "evidence-path-missing") evidenceNode.Remove("evidencePath");

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO Reports (ReportId, ReportJson, EvidenceJson, CreatedUtc, Kind) " +
                "VALUES ('report-row', $report, $evidence, '2026-01-01T00:00:00Z', 'parsimony');",
                ("$report", reportNode.ToJsonString()), ("$evidence", evidenceNode.ToJsonString()));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Reports).IsComplete);
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Reports/report-row" &&
            item.FamilyScopedUncertainty);
        if (defect != "response-assessment-list-null")
            Assert.Contains(references.References, item => item.Target == "assessment-response" &&
                item.Reason == "frozen-parsimony-report");
        if (defect is not ("response-input-null" or "response-input-assessment-list-null"))
            Assert.Contains(references.References, item => item.Target == "assessment-response-input" &&
                item.Reason == "frozen-parsimony-input");
        if (defect != "evidence-input-assessment-list-null")
            Assert.Contains(references.References, item => item.Target == "assessment-evidence" &&
                item.Reason == "frozen-parsimony-input");
        if (defect != "response-input-null")
            Assert.Contains(references.References, item => item.Target == "bundle-response");
        Assert.Contains(references.References, item => item.Target == "bundle-evidence");
        if (defect is "response-text-null" or "finding-id-empty" or "finding-id-whitespace" or
            "finding-digest-empty" or "finding-digest-whitespace")
            Assert.Contains(references.References, item => item.Target == "bundle-frozen-finding" &&
                item.Reason == "frozen-parsimony-finding-evidence");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(999, false)]
    public void FrozenFindingEnumNumbersMustNameDefinedValues(int axis, bool expectedComplete)
    {
        using var database = OpenDatabase("finding-enum-" + axis + ".fwdata");
        var token = NewBaseline('a');
        var finding = new ParsimonyFinding("finding-enum", "measure-one", ParsimonyAxis.Parsimony,
            ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project-one"),
            null, new ParsimonyMeasureNumber(1, 1, "items"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "threshold/v1"),
            Digest('9'), [], "guide:measure-one", [], ParsimonyVerification.NotRun);
        var report = new ParsimonyReportResponse("report-enum", NewParsimonyInputs("bundle-enum", token, []),
            [finding], "report text") { AssessmentIds = ["assessment-enum"], Notes = [] };
        var reportNode = JsonNode.Parse(JsonSerializer.Serialize(report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        reportNode["findings"]![0]!["axis"] = axis;
        var strictWebOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
        };
        strictWebOptions.Converters.Add(new JsonStringEnumConverter());
        Assert.NotNull(JsonSerializer.Deserialize<ParsimonyReportResponse>(reportNode.ToJsonString(), strictWebOptions));
        var evidence = JsonSerializer.Serialize(new
        {
            inputs = NewParsimonyInputs("bundle-enum", token, []),
            grammarFactsPath = "grammar-facts.sqlite",
            evidencePath = "evidence.sqlite",
            findings = Array.Empty<object>(),
            joinQuality = (object?)null,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO Reports (ReportId, ReportJson, EvidenceJson, CreatedUtc, Kind) " +
                "VALUES ('report-enum', $report, $evidence, '2026-01-01T00:00:00Z', 'parsimony');",
                ("$report", reportNode.ToJsonString()), ("$evidence", evidence));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.True(expectedComplete == references.FamilyCompleteness.Single(item =>
                item.Family == ArtifactReferenceFamily.Reports).IsComplete,
            string.Join(",", references.Diagnostics.Where(item => item.Family == ArtifactReferenceFamily.Reports)
                .Select(item => item.RecordId + ":" + item.Code)));
        Assert.Contains(references.References, item => item.Target == "assessment-enum" &&
            item.Reason == "frozen-parsimony-report");
        if (!expectedComplete)
            Assert.Contains(references.Diagnostics, item => item.Code == "frozen-report-reference-unrecognized" &&
                item.RecordId == "Reports/report-enum");
    }

    [Fact]
    public void GrammarCheckProvenanceRetainsItsOwnBaselineBinding()
    {
        using var database = OpenDatabase("grammar-check-provenance.fwdata");
        var rowToken = NewBaseline('a');
        var provenanceToken = NewBaseline('b');
        var noProvenanceToken = NewBaseline('c');
        var mismatched = new GrammarCheckResponse([], true)
        {
            Provenance = new GrammarCheckProvenance(provenanceToken, Digest('1'), null,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null, null),
        };
        var noProvenance = new GrammarCheckResponse([], true);
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertGrammarCheck(connection, transaction, rowToken, mismatched);
            InsertGrammarCheck(connection, transaction, noProvenanceToken, noProvenance);
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest &&
            item.Target == rowToken.BundleDigest && item.Reason == "grammar-check-baseline-token");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest &&
            item.Target == provenanceToken.BundleDigest && item.Reason == "grammar-check-provenance-baseline-token");
        Assert.Contains(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.Baselines &&
            item.Code == "grammar-check-reference-unrecognized" && item.RecordId == "GrammarChecks/1");
        Assert.DoesNotContain(references.Diagnostics, item => item.RecordId == "GrammarChecks/2" &&
            item.Code == "grammar-check-reference-unrecognized");
    }

    [Fact]
    public void CapturesTypedDryRunAndTrialJobReferencesWithoutTouchingTheirPaths()
    {
        using var database = OpenDatabase("jobs.fwdata");
        var token = NewBaseline('d');
        var proposalId = CanonicalId.Mint("proposal/").Value;
        var proposalJson = JsonSerializer.Serialize(new
        {
            contractVersions = new Dictionary<string, string>(),
            proposalId,
            operations = Array.Empty<object>(),
        });
        var frozenProposal = FrozenProposalRevision.Create(proposalJson);
        var source = new DryRunSourceBinding(token, "/missing/private/baseline", "/missing/private/baseline/project.fwdata",
            Digest('3'));
        var input = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion, frozenProposal, [], source);
        var completion = new DryRunJobCompletion(token, token.CapturedUtc, "known-old", source, Digest('4'));
        var anchor = new BoundDryRunAnchor(Digest('5'), Digest('6'), Digest('7'), "runner", "liblcm", "projection",
            token.CapturedUtc);
        var trialInput = new TrialJobInput("{}", null, BaselineAssessmentIds: ["assessment-job-input"]);
        var trialResult = JsonSerializer.Serialize(new
        {
            baselineToken = token,
            assessmentIds = new[] { "assessment-job-result" },
            allWords = false,
            grammarCheckWarning = (string?)null,
        }, MotifJson.CreateOptions());
        var published = JsonSerializer.Serialize(new
        {
            intentDigest = Digest('8'),
            baselineNote = "synthetic",
            expectedEffects = JsonDocument.Parse("[]").RootElement,
            effectDigest = Digest('9'),
            anchor,
        }, MotifJson.CreateOptions());
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, "trial-job", TrialJobHandler.TrialKind, "completed",
                JsonSerializer.Serialize(trialInput, MotifJson.CreateOptions()), trialResult, null);
            InsertJob(connection, transaction, "dry-run-job", "dry-run", "completed-dry-run-only",
                JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
                JsonSerializer.Serialize(completion, MotifJson.CreateOptions()), published);
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs).IsComplete,
            string.Join(",", references.Diagnostics.Where(item => item.Family == ArtifactReferenceFamily.Jobs)
                .Select(item => item.Code)));
        Assert.Contains(references.References, reference => reference.Target == "assessment-job-input" &&
            reference.Reason == "trial-input-baseline-assessment");
        Assert.Contains(references.References, reference => reference.Target == "assessment-job-result" &&
            reference.Reason == "trial-result-assessment");
        Assert.Contains(references.References, reference => reference.Target == "/missing/private/baseline" &&
            reference.Reason == "dry-run-input-baseline-root");
        Assert.Contains(references.References, reference => reference.Target == "/missing/private/baseline/project.fwdata" &&
            reference.Reason == "dry-run-result-baseline-fwdata");
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Jobs/dry-run-job/DryRunJson" &&
            item.Reason == "published-dry-run-anchor-has-no-artifact-identity");
    }

    [Fact]
    public void BudgetStopsAreRecognizedAndKeepTheTrialBaseline()
    {
        using var database = OpenDatabase("budget-stops.fwdata");
        var token = NewBaseline('d');
        var result = JsonSerializer.Serialize(new
        {
            code = "assessment.stopped-by-budget", message = "Stopped.", wordsDone = 1, wordsNotDone = 2,
            baselineToken = token,
        }, MotifJson.CreateOptions());
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, "assess", "assess", "stopped-by-budget", "{}",
                "{\"code\":\"assessment.stopped-by-budget\",\"message\":\"Stopped.\",\"wordsDone\":1,\"wordsNotDone\":2}", null);
            var input = JsonSerializer.Serialize(new TrialJobInput("{}", null), MotifJson.CreateOptions());
            InsertJob(connection, transaction, "trial", TrialJobHandler.TrialKind, "stopped-by-budget", input, result, null);
            transaction.Commit();
        }
        var references = Capture(database);
        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs).IsComplete,
            string.Join(",", references.Diagnostics.Select(item => item.Code)));
        Assert.Contains(references.References, item => item.Target == token.BundleDigest &&
            item.Reason == "budget-stop-baseline-token");
    }

    [Fact]
    public void MissingSuccessfulJobResultsMarkJobsIncompleteAndRetainUncertainty()
    {
        using var database = OpenDatabase("missing-successful-job-results.fwdata");
        var token = NewBaseline('d');
        var source = new DryRunSourceBinding(token, "/private/result-baseline", "/private/result-baseline/project.fwdata",
            Digest('3'));
        var proposal = new FrozenProposalRevision(CanonicalId.Mint("proposal/").Value, Digest('1'), "{}", Digest('2'));
        var dryRunInput = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion, proposal, [], null);
        var dryRunResult = new DryRunJobCompletion(token, token.CapturedUtc, "known-old", source, Digest('4'));
        var trialInput = new TrialJobInput("{}", null, BaselineAssessmentIds: ["assessment-input"]);
        var anchor = new BoundDryRunAnchor(Digest('5'), Digest('6'), Digest('7'), "runner", "liblcm", "projection",
            token.CapturedUtc);
        var published = JsonSerializer.Serialize(new
        {
            intentDigest = Digest('8'),
            baselineNote = "synthetic",
            expectedEffects = JsonDocument.Parse("[]").RootElement,
            effectDigest = Digest('9'),
            anchor,
        }, MotifJson.CreateOptions());
        var inputJson = JsonSerializer.Serialize(dryRunInput, MotifJson.CreateOptions());

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, "missing-dry-run-result", "dry-run", "completed-dry-run-only",
                inputJson, null, published);
            InsertJob(connection, transaction, "missing-trial-result", TrialJobHandler.TrialKind, "completed",
                JsonSerializer.Serialize(trialInput, MotifJson.CreateOptions()), null, published);
            InsertJob(connection, transaction, "missing-partial-trial-result", TrialJobHandler.TrialKind,
                "completed-with-assessment-failure", JsonSerializer.Serialize(trialInput, MotifJson.CreateOptions()),
                null, published);
            InsertJob(connection, transaction, "malformed-dry-run-result", "dry-run", "completed-dry-run-only",
                inputJson, "{}", published);
            InsertJob(connection, transaction, "bound-dry-run-result", "dry-run", "completed-dry-run-only",
                inputJson, JsonSerializer.Serialize(dryRunResult, MotifJson.CreateOptions()), published);
            InsertJob(connection, transaction, "active-dry-run", "dry-run", "running", inputJson, null, null);
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs).IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Code == "job-success-result-missing" &&
            item.RecordId == "Jobs/missing-dry-run-result/ResultJson");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-success-result-missing" &&
            item.RecordId == "Jobs/missing-trial-result/ResultJson");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-success-result-missing" &&
            item.RecordId == "Jobs/missing-partial-trial-result/ResultJson");
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Jobs/missing-dry-run-result/ResultJson" &&
            item.FamilyScopedUncertainty);
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Jobs/missing-trial-result/ResultJson" &&
            item.FamilyScopedUncertainty);
        Assert.Contains(references.RetentionRecords,
            item => item.RecordId == "Jobs/missing-partial-trial-result/ResultJson" && item.FamilyScopedUncertainty);
        Assert.Contains(references.Diagnostics, item => item.Code == "job-result-shape-unrecognized" &&
            item.RecordId == "Jobs/malformed-dry-run-result/ResultJson");
        Assert.Contains(references.References, item => item.Target == source.RootDirectory &&
            item.Reason == "dry-run-result-baseline-root");
        Assert.DoesNotContain(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.Jobs &&
            item.RecordId == "Jobs/bound-dry-run-result/ResultJson");
        Assert.DoesNotContain(references.Diagnostics, item => item.RecordId == "Jobs/active-dry-run/ResultJson");
    }

    [Theory]
    [InlineData("future-status")]
    [InlineData("")]
    [InlineData("Completed")]
    [InlineData(" completed")]
    public void UnknownJobStatusesRetainUncertaintyWithoutSuppressingOtherRows(string status)
    {
        using var database = OpenDatabase("unknown-job-status.fwdata");
        var token = NewBaseline('d');
        var proposal = new FrozenProposalRevision(CanonicalId.Mint("proposal/").Value, Digest('1'), "{}", Digest('2'));
        var input = JsonSerializer.Serialize(
            new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion, proposal, [], null), MotifJson.CreateOptions());
        var anchor = new BoundDryRunAnchor(Digest('5'), Digest('6'), Digest('7'), "runner", "liblcm", "projection",
            token.CapturedUtc);
        var published = JsonSerializer.Serialize(new
        {
            intentDigest = Digest('8'),
            baselineNote = "synthetic",
            expectedEffects = JsonDocument.Parse("[]").RootElement,
            effectDigest = Digest('9'),
            anchor,
        }, MotifJson.CreateOptions());
        var phonologyInput = JsonSerializer.Serialize(
            new PhonologyCheckJobInput("revision-one", token, new PhonologyLimits(1, 1, 1)), MotifJson.CreateOptions());
        var assessmentResult = JsonSerializer.Serialize(new
        {
            assessmentIds = new[] { "assessment-after-unknown-status" },
            assessmentId = "assessment-after-unknown-status",
            revisionId = "revision-one",
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, "unknown-status-job", "dry-run", status, input, null, published,
                dryRunPublished: true);
            InsertJob(connection, transaction, "later-valid-job", PhonologyCheckJobHandler.JobKind, "completed",
                phonologyInput, assessmentResult, null);
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs).IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.Jobs &&
            item.Code == "job-status-unrecognized" && item.RecordId == "Jobs/unknown-status-job");
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Jobs/unknown-status-job" &&
            item.FamilyScopedUncertainty);
        Assert.Contains(references.References, item => item.Target == "assessment-after-unknown-status" &&
            item.Reason == "phonology-check-job-result-assessment");
    }

    [Fact]
    public void CurrentJobKindsAndOutcomesUseTheirTypedContracts()
    {
        using var database = OpenDatabase("current-job-outcomes.fwdata");
        var token = NewBaseline('d');
        var proposal = new FrozenProposalRevision(CanonicalId.Mint("proposal/").Value, Digest('1'), "{}", Digest('2'));
        var dryRunInput = JsonSerializer.Serialize(
            new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion, proposal, [], null), MotifJson.CreateOptions());
        var trialInput = JsonSerializer.Serialize(new TrialJobInput("{}", null), MotifJson.CreateOptions());
        var anchor = new BoundDryRunAnchor(Digest('5'), Digest('6'), Digest('7'), "runner", "liblcm", "projection",
            token.CapturedUtc);
        var published = JsonSerializer.Serialize(new
        {
            intentDigest = Digest('8'),
            baselineNote = "synthetic",
            expectedEffects = JsonDocument.Parse("[]").RootElement,
            effectDigest = Digest('9'),
            anchor,
        }, MotifJson.CreateOptions());
        var detail = JsonSerializer.Serialize(new { detail = "Assessment could not finish." }, MotifJson.CreateOptions());
        var cancelledPublished = "{\"assessmentDisposition\":\"cancelled\"}";
        var detailWithException = JsonSerializer.Serialize(new
        {
            detail = "Assessment could not finish.",
            exceptionType = "System.IO.IOException",
            exception = "System.IO.IOException: Assessment could not finish.\n   at Stack.Frame()",
        }, MotifJson.CreateOptions());
        var failedRefresh = JsonSerializer.Serialize(new
        {
            detail = "The project is in use.",
            outcome = "project-in-use",
        }, MotifJson.CreateOptions());
        var validSource = new DryRunSourceBinding(token, "/private/job-baseline", "/private/job-baseline/project.fwdata",
            Digest('3'));
        var mismatchedDryRunResult = JsonSerializer.Serialize(
            new DryRunJobCompletion(token, token.CapturedUtc, "known-old", validSource, Digest('4')),
            MotifJson.CreateOptions());

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, "baseline-refresh-success", ProjectJobHandlers.BaselineRefreshKind,
                "completed", "{}", null, null);
            InsertJob(connection, transaction, "baseline-refresh-failure", ProjectJobHandlers.BaselineRefreshKind,
                "failed", "{}", failedRefresh, null);
            InsertJob(connection, transaction, "trial-partial", TrialJobHandler.TrialKind,
                "completed-with-assessment-failure", trialInput, detail, published, dryRunPublished: true);
            InsertJob(connection, transaction, "trial-partial-exception", TrialJobHandler.TrialKind,
                "completed-with-assessment-failure", trialInput, detailWithException, published, dryRunPublished: true);
            InsertJob(connection, transaction, "trial-runner-failed-exception", TrialJobHandler.TrialKind,
                "failed", trialInput, detailWithException, null);
            InsertJob(connection, transaction, "baseline-refresh-runner-failure", ProjectJobHandlers.BaselineRefreshKind,
                "failed", "{}", detailWithException, null);
            InsertJob(connection, transaction, "trial-cancelled-before-publication", TrialJobHandler.TrialKind,
                "cancelled", trialInput, null, null);
            InsertJob(connection, transaction, "trial-cancelled-after-publication", TrialJobHandler.TrialKind,
                "cancelled", trialInput, cancelledPublished, published, dryRunPublished: true);
            InsertJob(connection, transaction, "trial-interrupted", TrialJobHandler.TrialKind,
                "interrupted", trialInput, null, null);
            InsertJob(connection, transaction, "dry-run-failed-detail", "dry-run", "failed", dryRunInput, detail, null);
            InsertJob(connection, transaction, "dry-run-failed-exhausted", "dry-run", "failed", dryRunInput,
                "{\"failure\":\"infrastructure-retry-exhausted\"}", null);
            InsertJob(connection, transaction, "dry-run-wrong-success-status", "dry-run", "completed",
                dryRunInput, mismatchedDryRunResult, published, dryRunPublished: true);
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Jobs/baseline-refresh-success" &&
            item.Reason == "retained-job-record");
        Assert.DoesNotContain(references.Diagnostics, item => item.RecordId == "Jobs/baseline-refresh-success");
        Assert.DoesNotContain(references.Diagnostics, item => item.RecordId == "Jobs/baseline-refresh-failure");
        Assert.DoesNotContain(references.Diagnostics, item => item.Code == "job-result-shape-unrecognized");
        Assert.DoesNotContain(references.Diagnostics, item => item.RecordId == "Jobs/baseline-refresh-runner-failure");
        Assert.DoesNotContain(references.Diagnostics, item => item.RecordId == "Jobs/trial-runner-failed-exception/ResultJson");
        Assert.DoesNotContain(references.Diagnostics, item => item.RecordId == "Jobs/trial-partial-exception/ResultJson");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-baseline-binding-missing" &&
            item.RecordId == "Jobs/trial-partial");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-baseline-binding-missing" &&
            item.RecordId == "Jobs/trial-cancelled-before-publication");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-baseline-binding-missing" &&
            item.RecordId == "Jobs/trial-cancelled-after-publication");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-baseline-binding-missing" &&
            item.RecordId == "Jobs/trial-interrupted");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-baseline-binding-missing" &&
            item.RecordId == "Jobs/dry-run-failed-detail");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-baseline-binding-missing" &&
            item.RecordId == "Jobs/dry-run-failed-exhausted");
        Assert.Contains(references.Diagnostics, item => item.Code == "job-result-status-kind-unrecognized" &&
            item.RecordId == "Jobs/dry-run-wrong-success-status/ResultJson");
        Assert.Contains(references.References, item => item.Target == validSource.RootDirectory &&
            item.Reason == "dry-run-result-baseline-root");
    }

    [Fact]
    public void CapturesReferencesFromCurrentParsimonyAndPhonologyJobPayloads()
    {
        using var database = OpenDatabase("current-jobs.fwdata");
        var baseline = NewBaseline('c');
        var reportInputAssessment = "assessment-report-input";
        var reportResultAssessment = "assessment-report-result";
        var reportBundle = "bundle-report-result";
        var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var reportInput = new ParsimonyJobInput("coverage", scope, [reportInputAssessment]);
        var reportResult = JsonSerializer.Serialize(new
        {
            reportId = "report-one",
            reportIds = new[] { "report-one" },
            bundleId = reportBundle,
            assessmentIds = new[] { reportResultAssessment },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        var multiReportResult = JsonSerializer.Serialize(new
        {
            reportId = (string?)null,
            reportIds = new[] { "report-two-a", "report-two-b" },
            bundleId = "bundle-multi-report-result",
            assessmentIds = Array.Empty<string>(),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

        var candidateInputAssessment = "assessment-candidate-input";
        var candidateBeforeRoot = "assessment-candidate-before-root";
        var candidateBeforeInput = "assessment-candidate-before-input";
        var candidateAfterRoot = "assessment-candidate-after-root";
        var candidateAfterInput = "assessment-candidate-after-input";
        var candidateInputDryRun = "dry-run-input-job";
        var candidateResultDryRun = "dry-run-result-job";
        var candidateInput = new ParsimonyCandidateJobInput(ParsimonyCandidateJobInput.CurrentSchemaVersion,
            candidateInputDryRun, "coverage", scope, [candidateInputAssessment], NewRetirementTranslation(baseline));
        var candidateInputWithoutAssessments = new ParsimonyCandidateJobInput(
            ParsimonyCandidateJobInput.CurrentSchemaVersion, "dry-run-optional-assessments-job", "coverage", scope);
        var beforeInputs = NewParsimonyInputs("bundle-candidate-before", baseline, [candidateBeforeInput]);
        var afterInputs = NewParsimonyInputs("bundle-candidate-after", baseline, [candidateAfterInput]);
        using var candidateFindingArguments = JsonDocument.Parse("{}");
        var candidateFinding = new ParsimonyFinding("candidate-finding", "measure-one", ParsimonyAxis.Parsimony,
            ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project-one"),
            null, new ParsimonyMeasureNumber(1, 1, "items"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "threshold/v1"),
            Digest('9'), [new ParsimonyEvidenceReference("bundle-candidate-finding", ParsimonyViews.ParserCases,
                candidateFindingArguments.RootElement.Clone())], "guide:measure-one", [], ParsimonyVerification.NotRun);
        var beforeReport = new ParsimonyReportResponse("candidate-before-report", beforeInputs, [candidateFinding], "before")
        {
            AssessmentIds = [candidateBeforeRoot],
            Notes = [],
        };
        var afterReport = new ParsimonyReportResponse("candidate-after-report", afterInputs, [], "after")
        {
            AssessmentIds = [candidateAfterRoot],
            Notes = [],
        };
        var candidateBinding = new ParsimonyCandidateBinding(Digest('1'), baseline, candidateResultDryRun,
            CanonicalId.Mint("proposal/").Value, Digest('2'), Digest('3'), [], Digest('4'), Digest('5'),
            Digest('6'), Digest('7'), Digest('8'));
        var candidateResult = new ParsimonyCandidateEvidenceResponse(1, "coverage", candidateBinding,
            beforeReport, afterReport, [], new ParsimonyApprovedAnalysisPreservation("preserved", 0, 0, []));

        var phonologyAssessment = "assessment-phonology-result";
        var phonologyRevision = "phonology-revision-one";
        var phonologyInput = JsonSerializer.Serialize(new PhonologyCheckJobInput(phonologyRevision, baseline,
            new PhonologyLimits(1, 1, 1)), MotifJson.CreateOptions());
        var phonologyResult = JsonSerializer.Serialize(new
        {
            assessmentIds = new[] { phonologyAssessment },
            assessmentId = phonologyAssessment,
            revisionId = phonologyRevision,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, "parsimony-report-job", ParsimonyJobHandler.JobKind, "completed",
                JsonSerializer.Serialize(reportInput, MotifJson.CreateOptions()), reportResult, null);
            InsertJob(connection, transaction, "parsimony-multi-report-job", ParsimonyJobHandler.JobKind, "completed",
                JsonSerializer.Serialize(new ParsimonyJobInput("all", scope, null), MotifJson.CreateOptions()),
                multiReportResult, null);
            InsertJob(connection, transaction, "parsimony-candidate-job", ParsimonyCandidateEvidenceBuilder.JobKind,
                "completed", JsonSerializer.Serialize(candidateInput, MotifJson.CreateOptions()),
                JsonSerializer.Serialize(candidateResult, MotifJson.CreateOptions()), null);
            InsertJob(connection, transaction, "parsimony-candidate-optional-assessments-job",
                ParsimonyCandidateEvidenceBuilder.JobKind, "queued",
                JsonSerializer.Serialize(candidateInputWithoutAssessments, MotifJson.CreateOptions()), null, null);
            InsertJob(connection, transaction, "phonology-check-job", PhonologyCheckJobHandler.JobKind, "completed",
                phonologyInput, phonologyResult, null);
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs).IsComplete,
            string.Join(",", references.Diagnostics.Where(item => item.Family == ArtifactReferenceFamily.Jobs)
                .Select(item => item.Code)));
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == reportInputAssessment && reference.Reason == "parsimony-report-job-input-assessment");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == reportResultAssessment && reference.Reason == "parsimony-report-job-result-assessment");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == reportBundle && reference.Reason == "parsimony-report-job-result-bundle");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == "bundle-multi-report-result" && reference.Reason == "parsimony-report-job-result-bundle");
        Assert.Contains(references.References, reference => reference.Target == "dry-run-optional-assessments-job" &&
            reference.Reason == "parsimony-candidate-job-input-dry-run-job");
        Assert.Contains(references.References, reference => reference.Target == candidateInputDryRun &&
            reference.Reason == "parsimony-candidate-job-input-dry-run-job");
        Assert.Contains(references.References, reference => reference.Target == candidateInputAssessment &&
            reference.Reason == "parsimony-candidate-job-input-assessment");
        Assert.Contains(references.References, reference => reference.Target == baseline.BundleDigest &&
            reference.Reason == "parsimony-candidate-job-input-retirement-baseline-token");
        Assert.Contains(references.References, reference => reference.Target == candidateResultDryRun &&
            reference.Reason == "parsimony-candidate-job-result-dry-run-job");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == candidateBeforeRoot && reference.Reason == "parsimony-candidate-before-report");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == candidateBeforeInput && reference.Reason == "parsimony-candidate-before-input");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == candidateAfterRoot && reference.Reason == "parsimony-candidate-after-report");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == candidateAfterInput && reference.Reason == "parsimony-candidate-after-input");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == "bundle-candidate-before" && reference.Reason == "parsimony-candidate-before-bundle");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == "bundle-candidate-finding" && reference.Reason == "parsimony-candidate-before-finding-evidence");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            reference.Target == "bundle-candidate-after" && reference.Reason == "parsimony-candidate-after-bundle");
        Assert.Contains(references.References, reference => reference.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            reference.Target == phonologyAssessment && reference.Reason == "phonology-check-job-result-assessment");
        Assert.Contains(references.RetentionRecords, record => record.RecordId == "Jobs/parsimony-report-job");
        Assert.Contains(references.RetentionRecords, record => record.RecordId == "Jobs/parsimony-multi-report-job");
        Assert.Contains(references.RetentionRecords, record => record.RecordId == "Jobs/parsimony-candidate-job");
        Assert.Contains(references.RetentionRecords, record => record.RecordId == "Jobs/phonology-check-job");
    }

    [Theory]
    [InlineData("after-null")]
    [InlineData("before-null")]
    [InlineData("candidate-null")]
    [InlineData("before-direct-citations-null")]
    public void CandidateJobPartialReportsRetainEveryReadableBinding(string defect)
    {
        using var database = OpenDatabase("candidate-partial-" + defect + ".fwdata");
        var candidateBaseline = NewBaseline('a');
        var beforeBaseline = NewBaseline('b');
        var afterBaseline = NewBaseline('c');
        var prefix = "candidate-" + defect;
        var result = NewCandidateResponse(prefix, candidateBaseline, beforeBaseline, afterBaseline);
        var resultNode = JsonNode.Parse(JsonSerializer.Serialize(result, MotifJson.CreateOptions()))!.AsObject();
        if (defect == "after-null") resultNode["After"] = null;
        if (defect == "before-null") resultNode["Before"] = null;
        if (defect == "candidate-null") resultNode["Candidate"] = null;
        if (defect == "before-direct-citations-null") resultNode["Before"]!["AssessmentIds"] = null;

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, prefix, ParsimonyCandidateEvidenceBuilder.JobKind, "completed",
                "{}", resultNode.ToJsonString(), null);
            transaction.Commit();
        }

        var references = Capture(database);
        var jobs = references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs);

        Assert.False(jobs.IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.Jobs &&
            item.Code == "job-result-shape-unrecognized" && item.RecordId == "Jobs/" + prefix + "/ResultJson");
        Assert.Contains(references.RetentionRecords, item => item.Family == ArtifactReferenceFamily.Jobs &&
            item.RecordId == "Jobs/" + prefix + "/ResultJson" && item.FamilyScopedUncertainty);

        if (defect != "candidate-null")
        {
            Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Job &&
                item.Target == prefix + "-dry-run" && item.Reason == "parsimony-candidate-job-result-dry-run-job");
            Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest &&
                item.Target == candidateBaseline.BundleDigest && item.Reason == "parsimony-candidate-job-result-baseline-token");
        }

        if (defect is "after-null" or "before-direct-citations-null")
            AssertCandidateReportReferences(references, prefix, "before", beforeBaseline, expectRootAssessment: defect == "after-null");
        if (defect is "before-null" or "candidate-null" or "before-direct-citations-null")
            AssertCandidateReportReferences(references, prefix, "after", afterBaseline, expectRootAssessment: true);
        if (defect == "candidate-null")
            AssertCandidateReportReferences(references, prefix, "before", beforeBaseline, expectRootAssessment: true);
    }

    [Theory]
    [InlineData("before-text-null", false)]
    [InlineData("after-text-null", false)]
    [InlineData("before-text-empty", true)]
    [InlineData("before-finding-id-empty", false)]
    [InlineData("after-finding-id-empty", false)]
    [InlineData("before-finding-id-whitespace", false)]
    [InlineData("after-finding-id-whitespace", false)]
    [InlineData("before-finding-digest-empty", false)]
    [InlineData("after-finding-digest-empty", false)]
    [InlineData("before-finding-digest-whitespace", false)]
    [InlineData("after-finding-digest-whitespace", false)]
    public void CandidateJobRequiredReportScalarsDetermineCompleteness(string defect, bool expectedComplete)
    {
        using var database = OpenDatabase("candidate-required-scalars-" + defect + ".fwdata");
        var candidateBaseline = NewBaseline('a');
        var beforeBaseline = NewBaseline('b');
        var afterBaseline = NewBaseline('c');
        var prefix = "candidate-" + defect;
        var result = NewCandidateResponse(prefix, candidateBaseline, beforeBaseline, afterBaseline);
        var resultNode = JsonNode.Parse(JsonSerializer.Serialize(result, MotifJson.CreateOptions()))!.AsObject();
        var side = defect.StartsWith("after-", StringComparison.Ordinal) ? "After" : "Before";
        if (defect.EndsWith("text-null", StringComparison.Ordinal)) resultNode[side]!["Text"] = null;
        if (defect.EndsWith("text-empty", StringComparison.Ordinal)) resultNode[side]!["Text"] = "";
        if (defect.EndsWith("finding-id-empty", StringComparison.Ordinal))
            resultNode[side]!["Findings"]![0]!["FindingId"] = "";
        if (defect.EndsWith("finding-id-whitespace", StringComparison.Ordinal))
            resultNode[side]!["Findings"]![0]!["FindingId"] = " \t";
        if (defect.EndsWith("finding-digest-empty", StringComparison.Ordinal))
            resultNode[side]!["Findings"]![0]!["EvidenceDigest"] = "";
        if (defect.EndsWith("finding-digest-whitespace", StringComparison.Ordinal))
            resultNode[side]!["Findings"]![0]!["EvidenceDigest"] = " \t";
        var candidateInput = new ParsimonyCandidateJobInput(ParsimonyCandidateJobInput.CurrentSchemaVersion,
            prefix + "-dry-run", "coverage", new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, prefix, ParsimonyCandidateEvidenceBuilder.JobKind, "completed",
                JsonSerializer.Serialize(candidateInput, MotifJson.CreateOptions()), resultNode.ToJsonString(), null);
            transaction.Commit();
        }

        var references = Capture(database);
        var jobs = references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs);

        Assert.Equal(expectedComplete, jobs.IsComplete);
        AssertCandidateReportReferences(references, prefix, "before", beforeBaseline, expectRootAssessment: true);
        AssertCandidateReportReferences(references, prefix, "after", afterBaseline, expectRootAssessment: true);
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Job &&
            item.Target == prefix + "-dry-run" && item.Reason == "parsimony-candidate-job-result-dry-run-job");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest &&
            item.Target == candidateBaseline.BundleDigest && item.Reason == "parsimony-candidate-job-result-baseline-token");
        if (expectedComplete)
        {
            Assert.DoesNotContain(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.Jobs &&
                item.Code == "job-result-shape-unrecognized" && item.RecordId == "Jobs/" + prefix + "/ResultJson");
            Assert.DoesNotContain(references.RetentionRecords, item => item.Family == ArtifactReferenceFamily.Jobs &&
                item.RecordId == "Jobs/" + prefix + "/ResultJson" && item.FamilyScopedUncertainty);
        }
        else
        {
            Assert.Contains(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.Jobs &&
                item.Code == "job-result-shape-unrecognized" && item.RecordId == "Jobs/" + prefix + "/ResultJson");
            Assert.Contains(references.RetentionRecords, item => item.Family == ArtifactReferenceFamily.Jobs &&
                item.RecordId == "Jobs/" + prefix + "/ResultJson" && item.FamilyScopedUncertainty);
        }
    }

    [Fact]
    public void UnknownAndMalformedCurrentJobPayloadsRemainIncompleteAndRetained()
    {
        using var database = OpenDatabase("invalid-current-jobs.fwdata");
        var baseline = NewBaseline('d');
        var candidateInput = new ParsimonyCandidateJobInput(ParsimonyCandidateJobInput.CurrentSchemaVersion,
            "dry-run-job", "coverage", new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var malformedCandidate = JsonNode.Parse(JsonSerializer.Serialize(candidateInput, MotifJson.CreateOptions()))!
            .AsObject();
        malformedCandidate["schemaVersion"] = ParsimonyCandidateJobInput.CurrentSchemaVersion - 1;
        var phonologyInput = JsonSerializer.Serialize(new PhonologyCheckJobInput("revision-one", baseline,
            new PhonologyLimits(1, 1, 1)), MotifJson.CreateOptions());
        var malformedPhonologyResult = JsonSerializer.Serialize(new
        {
            assessmentIds = (string[]?)null,
            assessmentId = "assessment-should-not-be-referenced",
            revisionId = "revision-one",
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertJob(connection, transaction, "unknown-current-kind", "future-job", "completed",
                "{\"futureInput\":true}", "{\"futureResult\":true}", null);
            InsertJob(connection, transaction, "malformed-candidate", ParsimonyCandidateEvidenceBuilder.JobKind,
                "queued", malformedCandidate.ToJsonString(), null, null);
            InsertJob(connection, transaction, "malformed-phonology-result", PhonologyCheckJobHandler.JobKind,
                "completed", phonologyInput, malformedPhonologyResult, null);
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.Jobs).IsComplete);
        Assert.Contains(references.Diagnostics, diagnostic => diagnostic.Code == "job-input-kind-unrecognized");
        Assert.Contains(references.Diagnostics, diagnostic => diagnostic.Code == "job-result-kind-unrecognized");
        Assert.Contains(references.Diagnostics, diagnostic => diagnostic.Code == "job-input-shape-unrecognized" &&
            diagnostic.RecordId == "Jobs/malformed-candidate/InputJson");
        Assert.Contains(references.Diagnostics, diagnostic => diagnostic.Code == "job-result-shape-unrecognized" &&
            diagnostic.RecordId == "Jobs/malformed-phonology-result/ResultJson");
        Assert.Contains(references.RetentionRecords, record => record.RecordId == "Jobs/unknown-current-kind" &&
            record.Reason == "retained-job-record");
        Assert.Contains(references.RetentionRecords, record => record.RecordId == "Jobs/malformed-candidate/InputJson" &&
            record.FamilyScopedUncertainty);
        Assert.Contains(references.RetentionRecords,
            record => record.RecordId == "Jobs/malformed-phonology-result/ResultJson" && record.FamilyScopedUncertainty);
        Assert.DoesNotContain(references.AssessmentIds, id => id == "assessment-should-not-be-referenced");
    }

    [Fact]
    public void RetainsWorkflowDecisionRevisionReceiptAndAnchorRecordsAsTypedRecords()
    {
        using var database = OpenDatabase("workflow.fwdata");
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = JsonSerializer.Serialize(new
        {
            contractVersions = new Dictionary<string, string>(),
            proposalId = proposalId.Value,
            operations = Array.Empty<object>(),
        });
        _ = ProposalJsonParser.Parse(proposalJson);
        var revisionDigest = Digest('l');
        var anchor = new BoundDryRunAnchor(Digest('a'), Digest('b'), Digest('c'), "runner", "liblcm", "projection",
            "2026-01-01T00:00:00Z");
        var receiptDigest = Digest('d');
        var receipt = new Receipt(proposalId, receiptDigest, false, "baseline note", "applied", [], Digest('e'),
            new AppliedLogEntry(proposalId.ToGuid(), 2, "2026-01-01T00:00:00Z", "user", "d".PadLeft(64, 'd'),
                "applied proposal"));
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO Proposals (ProposalId, Status, AnchorJson) VALUES ($id, 'applied', $anchor);",
                ("$id", proposalId.Value), ("$anchor", JsonSerializer.Serialize(anchor)));
            using (var revision = connection.CreateCommand())
            {
                revision.Transaction = transaction;
                revision.CommandText = "INSERT INTO ProposalRevisions (ProposalId, IntentDigest, ProposalJson, CreatedUtc) " +
                    "VALUES ($proposal, $digest, $json, '2026-01-01T00:00:00Z');";
                revision.Parameters.AddWithValue("$proposal", proposalId.Value);
                revision.Parameters.AddWithValue("$digest", revisionDigest);
                revision.Parameters.AddWithValue("$json", System.Text.Encoding.UTF8.GetBytes(proposalJson));
                revision.ExecuteNonQuery();
            }
            Execute(connection, transaction,
                "INSERT INTO ReviewMarks (ProposalId, EntryKey, ContentDigest, EvidenceDigest, LookedAtUtc) " +
                "VALUES ($proposal, 'entry-one', $content, $evidence, '2026-01-01T00:00:00Z');",
                ("$proposal", proposalId.Value), ("$content", Digest('f')), ("$evidence", Digest('g')));
            Execute(connection, transaction,
                "INSERT INTO Receipts (ReceiptId, ProposalId, IntentDigest, ReceiptJson, RecordedUtc) " +
                "VALUES ('receipt-one', $proposal, $digest, $json, '2026-01-01T00:00:00Z');",
                ("$proposal", proposalId.Value), ("$digest", receiptDigest),
                ("$json", JsonSerializer.Serialize(receipt)));
            Execute(connection, transaction,
                "INSERT INTO AppliedIndex (ProposalId, IntentDigest, AppliedUtc, RecordJson) " +
                "VALUES ($proposal, $digest, '2026-01-01T00:00:00Z', NULL);",
                ("$proposal", proposalId.Value), ("$digest", receiptDigest));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.WorkflowRecords).IsComplete,
            string.Join(",", references.Diagnostics.Where(item => item.Family == ArtifactReferenceFamily.WorkflowRecords)
                .Select(item => item.Code)));
        Assert.Contains(references.RetentionRecords, item => item.RecordId.StartsWith("ProposalRevisions/", StringComparison.Ordinal));
        Assert.Contains(references.RetentionRecords, item => item.RecordId == $"Proposals/{proposalId.Value}/AnchorJson");
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "ReviewMarks/" + proposalId.Value + "/entry-one");
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Receipts/receipt-one");
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "AppliedIndex/" + proposalId.Value + "/" + receiptDigest);
    }

    [Fact]
    public void NullReceiptReferenceNodesRemainUncertainAndDoNotSuppressOtherReceipts()
    {
        using var database = OpenDatabase("receipt-null-nodes.fwdata");
        var proposalId = CanonicalId.Mint("proposal/");
        var digest = Digest('d');
        var receipt = new Receipt(proposalId, digest, false, "baseline", "applied", [], Digest('e'),
            new AppliedLogEntry(proposalId.ToGuid(), 2, "2026-01-01T00:00:00Z", "user", "d".PadLeft(64, 'd'),
                "applied proposal"));
        var malformed = JsonNode.Parse(JsonSerializer.Serialize(receipt))!.AsObject();
        malformed["ActualEffects"] = null;
        malformed["AppliedLogEntry"] = null;
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO Proposals (ProposalId, Status) VALUES ($proposal, 'applied');",
                ("$proposal", proposalId.Value));
            Execute(connection, transaction,
                "INSERT INTO Receipts (ReceiptId, ProposalId, IntentDigest, ReceiptJson, RecordedUtc) VALUES " +
                "('receipt-null', $proposal, $digest, $malformed, '2026-01-01T00:00:00Z'), " +
                "('receipt-valid', $proposal, $digest, $valid, '2026-01-01T00:00:00Z');",
                ("$proposal", proposalId.Value), ("$digest", digest), ("$malformed", malformed.ToJsonString()),
                ("$valid", JsonSerializer.Serialize(receipt)));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.WorkflowRecords)
            .IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.WorkflowRecords &&
            item.Code == "receipt-shape-unrecognized" && item.RecordId == "Receipts/receipt-null");
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Receipts/receipt-null" &&
            item.FamilyScopedUncertainty);
        Assert.Contains(references.RetentionRecords, item => item.RecordId == "Receipts/receipt-valid" &&
            item.Reason == "typed-receipt-has-no-artifact-binding");
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("incomplete", false)]
    [InlineData("missing", false)]
    [InlineData("mismatch", false)]
    public void CapturesParserRunAssessmentAndInvocationWithoutInventingAPath(string metadataCase,
        bool expectedEvidenceComplete)
    {
        using var database = OpenDatabase("parser-run.fwdata");
        var token = NewBaseline('c');
        const string bundleId = "bundle-evidence";
        const string rootPath = "/private/baselines/parser-run";
        const string fwDataPath = "/private/baselines/parser-run/project.fwdata";
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            InsertBaseline(connection, transaction, token, rootPath, fwDataPath);
            var tokenJson = JsonSerializer.Serialize(token, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Execute(connection, transaction,
                "INSERT INTO ParsimonyBundles (BundleId, BaselineTokenJson, ProjectKey, BaselineDigest, MaterialKey, " +
                "EvidenceDirectory, GrammarFactsPath, EvidencePath, BaselineRootDirectory, InputKind, CandidateIdentity, " +
                "ModelFingerprint, GrammarFactsSchemaVersion, GrammarFactsSha256, EvidenceSchemaVersion, EvidenceSha256, " +
                "CreatedUtc) VALUES ($id, $token, 'project-one', $baseline, $material, $directory, $facts, $evidence, " +
                "$root, 'baseline', NULL, 'model', 1, $factsDigest, 1, $evidenceDigest, '2026-01-01T00:00:00Z');",
                ("$id", bundleId), ("$token", tokenJson), ("$baseline", token.BundleDigest),
                ("$material", Digest('1')), ("$directory", "/private/parsimony/bundle-evidence"),
                ("$facts", "/private/parsimony/bundle-evidence/grammar-facts.sqlite"),
                ("$evidence", "/private/parsimony/bundle-evidence/evidence.sqlite"), ("$root", rootPath),
                ("$factsDigest", Digest('2')), ("$evidenceDigest", Digest('3')));
            Execute(connection, transaction,
                "INSERT INTO CurrentParsimonyBundles (ProjectKey, InputKind, BundleId) " +
                "VALUES ('project-one', 'baseline', $bundle);", ("$bundle", bundleId));
            Execute(connection, transaction,
                "INSERT INTO ParsimonyBundleReferences (BundleId, ReferenceKind, ReferenceId, CreatedUtc) " +
                "VALUES ($bundle, 'report', 'report-evidence', '2026-01-01T00:00:00Z');", ("$bundle", bundleId));
            transaction.Commit();
        }

        using var projectSnapshot = OpenSnapshot(database, "project-one", '4');
        using var evidenceConnection = new SqliteConnection("Data Source=:memory:");
        evidenceConnection.Open();
        Execute(evidenceConnection, null, $"PRAGMA application_id={EvidenceSchema.ApplicationId};");
        Execute(evidenceConnection, null, $"PRAGMA user_version={EvidenceSchema.Version};");
        using var evidenceTransaction = evidenceConnection.BeginTransaction();
        using (var schema = evidenceConnection.CreateCommand())
        {
            schema.Transaction = evidenceTransaction;
            schema.CommandText = EvidenceSchema.Ddl;
            schema.ExecuteNonQuery();
        }
        if (metadataCase != "missing")
            Execute(evidenceConnection, evidenceTransaction,
                "INSERT INTO artifact_metadata (singleton, application_id, schema_version, projection_version, " +
                "baseline_token_json, baseline_key, input_kind, candidate_identity, source_sha256, model_fingerprint, complete, " +
                "text_count, occurrence_count, wordform_count, analysis_count, scope_count, scope_word_count, capability_count, " +
                "parser_run_count, parser_case_count, parser_analysis_count, parser_morph_count, parser_disapproved_count, " +
                "parser_match_count, parser_reviewed_negative_count) " +
                "VALUES (1, $app, $schema, 1, $token, 'baseline-key', 'baseline', NULL, $source, 'model', $complete, " +
                "0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0);",
                ("$app", (metadataCase == "mismatch" ? 0 : EvidenceSchema.ApplicationId).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                ("$schema", (metadataCase == "mismatch" ? EvidenceSchema.Version - 1 : EvidenceSchema.Version)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("$token", JsonSerializer.Serialize(token, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
                ("$source", Digest('5')), ("$complete", metadataCase == "incomplete" ? "0" : "1"));
        Execute(evidenceConnection, evidenceTransaction,
            "INSERT INTO parser_runs (assessment_id, invocation_id, source_sha256, parser_sha256, model_fingerprint, " +
            "status, timeout_ms, step_limit, threads, requested_case_count, completed_case_count, reason) " +
            "VALUES ('assessment-parser-run', 'invocation-parser-run', $source, $parser, 'model', 'complete', NULL, " +
            "'default', 1, 0, 0, NULL);", ("$source", Digest('5')), ("$parser", Digest('6')));
        Execute(evidenceConnection, evidenceTransaction,
            "INSERT INTO parser_runs (assessment_id, invocation_id, source_sha256, parser_sha256, model_fingerprint, " +
            "status, timeout_ms, step_limit, threads, requested_case_count, completed_case_count, reason) " +
            "VALUES ('assessment-parser-unavailable', 'invocation-parser-unavailable', $source, $parser, 'model', " +
            "'unavailable', NULL, 'default', 1, 0, 0, 'parser-unavailable');",
            ("$source", Digest('7')), ("$parser", Digest('8')));
        evidenceTransaction.Commit();
        using var evidenceReadTransaction = evidenceConnection.BeginTransaction();
        var evidenceSnapshot = PrivateArtifactStoreSnapshot.FromPrivateCopy("evidence-one", ArtifactStoreKind.ParsimonyEvidence,
            EvidenceSchema.Version, Digest('7'), evidenceConnection, evidenceReadTransaction, bundleId);
        var references = GeneratedArtifactReferences.Capture([projectSnapshot.Snapshot, evidenceSnapshot],
            ArtifactReferenceDomainDeclaration.Explicit(["project-one", "evidence-one"], true, "paired private fixture stores"));

        Assert.True(references.ReferenceDomain.IsComplete);
        Assert.Equal(expectedEvidenceComplete,
            references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.ParsimonyEvidence).IsComplete);
        if (!expectedEvidenceComplete)
            Assert.Contains(references.Diagnostics, item => item.Family == ArtifactReferenceFamily.ParsimonyEvidence &&
                item.Code == "parsimony-evidence-publication-unrecognized");
        if (expectedEvidenceComplete)
            Assert.True(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.ParsimonyEvidence).IsComplete,
            string.Join(",", references.Diagnostics.Where(item => item.Family == ArtifactReferenceFamily.ParsimonyEvidence)
                .Select(item => item.Code)));
        Assert.Contains(references.References, reference => reference.StoreIdentity == "evidence-one" &&
            reference.Target == "assessment-parser-run" && reference.Reason == "parser-run-assessment");
        Assert.Contains(references.References, reference => reference.StoreIdentity == "evidence-one" &&
            reference.Target == "assessment-parser-unavailable" && reference.Reason == "parser-run-assessment");
        Assert.Contains(references.References, reference => reference.StoreIdentity == "evidence-one" &&
            reference.Target == "invocation-parser-run" && reference.Reason == "parser-run-invocation");
        Assert.Contains(references.References, reference => reference.StoreIdentity == "evidence-one" &&
            reference.Target == Digest('5') && reference.Reason == "parser-run-source-digest");
        Assert.DoesNotContain(references.References, reference => reference.StoreIdentity == "evidence-one" &&
            reference.TargetKind == ArtifactReferenceTargetKind.Path && reference.RetainingRecordId.StartsWith("parser_runs/", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownParsimonyBundleStateRetainsKnownBindingsAndMarksFamilyIncomplete()
    {
        using var database = OpenDatabase("unknown-bundle-state.fwdata");
        var token = NewBaseline('b');
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction, "PRAGMA ignore_check_constraints=ON;");
            Execute(connection, transaction,
                "INSERT INTO ParsimonyBundles (BundleId, BaselineTokenJson, ProjectKey, BaselineDigest, MaterialKey, " +
                "EvidenceDirectory, GrammarFactsPath, EvidencePath, BaselineRootDirectory, InputKind, CandidateIdentity, " +
                "ModelFingerprint, GrammarFactsSchemaVersion, GrammarFactsSha256, EvidenceSchemaVersion, EvidenceSha256, " +
                "CreatedUtc, State) VALUES ('bundle-unknown-state', $token, 'project-one', 'baseline-digest', 'material', " +
                "'/private/bundles/unknown', '/private/bundles/unknown/grammar-facts.sqlite', " +
                "'/private/bundles/unknown/evidence.sqlite', '/private/baselines/root', 'baseline', NULL, 'model', " +
                "1, $digest, 4, $digest, '2026-01-01T00:00:00Z', 'future-state');",
                ("$token", JsonSerializer.Serialize(token, MotifJson.CreateOptions())), ("$digest", Digest('c')));
            transaction.Commit();
        }

        var references = Capture(database);

        Assert.False(references.FamilyCompleteness.Single(item => item.Family == ArtifactReferenceFamily.ParsimonyBundles)
            .IsComplete);
        Assert.Contains(references.Diagnostics, item => item.Code == "parsimony-bundle-descriptor-unrecognized" &&
            item.RecordId == "ParsimonyBundles/bundle-unknown-state");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            item.Target == "bundle-unknown-state" && item.Reason == "parsimony-bundle-row");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Path &&
            item.Target == "/private/bundles/unknown/evidence.sqlite" && item.Reason == "parsimony-evidence-path");
    }

    public void Dispose()
    {
        foreach (var path in Directory.EnumerateFiles(_root)) File.Delete(path);
        Directory.Delete(_root);
    }

    private MotifDatabase OpenDatabase(string fileName) => MotifDatabase.OpenOwned(
        Path.Combine(_root, Path.GetFileNameWithoutExtension(fileName) + ".motif.db"),
        new ProjectLocator(Path.Combine(_root, fileName), "project-one"), MotifSchema.CurrentSchema, new Version(1, 0));

    private static ArtifactReferenceSnapshot Capture(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var snapshot = PrivateArtifactStoreSnapshot.FromPrivateCopy("project-one", ArtifactStoreKind.Project,
            MotifSchema.CurrentSchema, Digest('f'), connection, transaction);
        return GeneratedArtifactReferences.Capture([snapshot],
            ArtifactReferenceDomainDeclaration.Explicit(["project-one"], true, "synthetic private fixture store"));
    }

    private static OpenSnapshotLease OpenSnapshot(MotifDatabase database, string identity, char fingerprint,
        int schemaVersion = MotifSchema.CurrentSchema)
    {
        var connection = database.OpenConnection();
        var transaction = connection.BeginTransaction();
        var snapshot = PrivateArtifactStoreSnapshot.FromPrivateCopy(identity, ArtifactStoreKind.Project,
            schemaVersion, Digest(fingerprint), connection, transaction);
        return new OpenSnapshotLease(connection, transaction, snapshot);
    }

    private static void InsertInvocation(SqliteConnection connection, SqliteTransaction transaction,
        BatchInvocationEvidence invocation) => Execute(connection, transaction,
        "INSERT INTO AssessmentInvocations (InvocationId, EvidenceJson) VALUES ($id, $evidence);",
        ("$id", invocation.InvocationId), ("$evidence", JsonSerializer.Serialize(invocation, MotifJson.CreateOptions())));

    private static void InsertBaseline(SqliteConnection connection, SqliteTransaction transaction,
        BaselineToken token, string rootPath, string fwDataPath) => Execute(connection, transaction,
        "INSERT INTO Baselines (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc, " +
        "BundleDigest, CapturedHostSessionId, CapturedEditGeneration, RootDirectory, FwDataPath, PublishedUtc, " +
        "SourceLastWriteUtc) VALUES ('project-one', $project, $semantic, $projection, $captured, $bundle, NULL, NULL, " +
        "$root, $path, $published, $source);", ("$project", token.ProjectIdentity),
        ("$semantic", token.SemanticSnapshotDigest), ("$projection", token.ProjectionVersion),
        ("$captured", token.CapturedUtc), ("$bundle", token.BundleDigest), ("$root", rootPath),
        ("$path", fwDataPath), ("$published", token.CapturedUtc), ("$source", token.CapturedUtc));

    private static void InsertGrammarCheck(SqliteConnection connection, SqliteTransaction transaction,
        BaselineToken token, GrammarCheckResponse response) => Execute(connection, transaction,
        "INSERT INTO GrammarChecks (BaselineToken, SelectionSha256, ResponseGzip, CheckedUtc) " +
        "VALUES ($token, $selection, $response, '2026-01-01T00:00:00Z');",
        ("$token", JsonSerializer.Serialize(token, MotifJson.CreateOptions())), ("$selection", Digest('2')),
        ("$response", GrammarCheckStorage.Compress(JsonSerializer.Serialize(response, MotifJson.CreateOptions()))));

    private void InsertInvocation(MotifDatabase database, BatchInvocationEvidence invocation)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        InsertInvocation(connection, transaction, invocation);
        transaction.Commit();
    }

    private static void InsertJob(SqliteConnection connection, SqliteTransaction transaction, string jobId,
        string kind, string status, string inputJson, string? resultJson, string? dryRunJson,
        bool? dryRunPublished = null) => Execute(connection, transaction,
        "INSERT INTO Jobs (JobId, ProjectKey, Kind, Status, LineageId, InputJson, ResultJson, DryRunJson, " +
        "DryRunPublished, CreatedUtc, UpdatedUtc) VALUES ($id, 'project-one', $kind, $status, $id, $input, " +
        "$result, $dryRun, $published, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');", ("$id", jobId),
        ("$kind", kind), ("$status", status), ("$input", inputJson), ("$result", resultJson),
        ("$dryRun", dryRunJson), ("$published", (dryRunPublished ?? dryRunJson is not null) ? "1" : "0"));

    private static BatchInvocationEvidence NewInvocation(string id, string sourcePath) =>
        new(id, sourcePath, Digest('1'), Digest('2'), "/private/run/words.txt", Digest('3'),
            "/private/run/result.tsv", Digest('4'), "/private/run/stderr.txt", Digest('5'), null,
            StepCap.Default, 2, false);

    private static AssessmentFixture NewAssessment(string id, BaselineToken token, string? invocationId,
        AssessmentFixture? replacement) => new(id, token, invocationId, replacement?.Id);

    private static void InsertAssessment(SqliteConnection connection, SqliteTransaction transaction,
        AssessmentFixture assessment)
    {
        Execute(connection, transaction,
            "INSERT INTO Assessments (AssessmentId, SelectionName, SelectionWordsJson, SelectionSha256, " +
            "GrammarSourceSha256, SavedUtc, Assessor, Kind, ScopeJson, ScopeDigest, TokeniserName, " +
            "TokeniserVersion, BaselineToken, InvocationId, ReplacesAssessmentId, SubjectKind) " +
            "VALUES ($id, 'selection', '[]', $selection, $source, '2026-01-01T00:00:00Z', 'pangloss', " +
            "'ParseTime', '{}', $scope, 'none', '1', $baseline, $invocation, $replacement, 'words');",
            ("$id", assessment.Id), ("$selection", Digest('7')), ("$source", Digest('8')),
            ("$scope", Digest('9')), ("$baseline", JsonSerializer.Serialize(assessment.Baseline, MotifJson.CreateOptions())),
            ("$invocation", assessment.InvocationId), ("$replacement", assessment.ReplacementId));
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static BaselineToken NewBaseline(char marker) => new("project-identity", Digest('a'), "projection-1",
        "2026-01-01T00:00:00Z", Digest(marker));

    private static ParsimonyReportInputs NewParsimonyInputs(string bundleId, BaselineToken baseline,
        IReadOnlyList<string> assessmentIds) => new(bundleId, baseline, "candidate", "candidate/one", "model/1",
        new ParsimonyArtifactDigest(1, new string('b', 64)), new ParsimonyArtifactDigest(1, new string('c', 64)),
        null, null, assessmentIds);

    private static ParsimonyCandidateEvidenceResponse NewCandidateResponse(string prefix,
        BaselineToken candidateBaseline, BaselineToken beforeBaseline, BaselineToken afterBaseline)
    {
        using var arguments = JsonDocument.Parse("{}");
        var candidate = new ParsimonyCandidateBinding(Digest('1'), candidateBaseline, prefix + "-dry-run",
            CanonicalId.Mint("proposal/" + prefix).Value, Digest('2'), Digest('3'), [], Digest('4'), Digest('5'),
            Digest('6'), Digest('7'), Digest('8'));
        var beforeReport = NewCandidateReport(prefix, "before", beforeBaseline, arguments.RootElement.Clone());
        var afterReport = NewCandidateReport(prefix, "after", afterBaseline, arguments.RootElement.Clone());
        return new ParsimonyCandidateEvidenceResponse(1, "coverage", candidate, beforeReport, afterReport, [],
            new ParsimonyApprovedAnalysisPreservation("preserved", 0, 0, []));
    }

    private static ParsimonyReportResponse NewCandidateReport(string prefix, string side, BaselineToken baseline,
        JsonElement arguments)
    {
        var finding = new ParsimonyFinding(prefix + "-" + side + "-finding", "measure-one", ParsimonyAxis.Parsimony,
            ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project-one"),
            null, new ParsimonyMeasureNumber(1, 1, "items"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "threshold/v1"),
            Digest('9'), [new ParsimonyEvidenceReference(prefix + "-" + side + "-finding-bundle",
                ParsimonyViews.ParserCases, arguments)], "guide:measure-one", [], ParsimonyVerification.NotRun);
        return new ParsimonyReportResponse(prefix + "-" + side + "-report",
            NewParsimonyInputs(prefix + "-" + side + "-bundle", baseline, [prefix + "-" + side + "-input"]),
            [finding], side)
        {
            AssessmentIds = [prefix + "-" + side + "-root"],
            Notes = [],
        };
    }

    private static void AssertCandidateReportReferences(ArtifactReferenceSnapshot references, string prefix,
        string side, BaselineToken baseline, bool expectRootAssessment)
    {
        var reportPrefix = "parsimony-candidate-" + side;
        if (expectRootAssessment)
            Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Assessment &&
                item.Target == prefix + "-" + side + "-root" && item.Reason == reportPrefix + "-report");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.Assessment &&
            item.Target == prefix + "-" + side + "-input" && item.Reason == reportPrefix + "-input");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest &&
            item.Target == baseline.BundleDigest && item.Reason == reportPrefix + "-baseline-token");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            item.Target == prefix + "-" + side + "-bundle" && item.Reason == reportPrefix + "-bundle");
        Assert.Contains(references.References, item => item.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
            item.Target == prefix + "-" + side + "-finding-bundle" &&
            item.Reason == reportPrefix + "-finding-evidence");
    }

    private static RetirementExpectationTranslation NewRetirementTranslation(BaselineToken baseline)
    {
        var original = new FrozenExpectationSet(baseline, Digest('d'), Digest('e'),
            "frozen-expectations/v1/test", [], [], []);
        var translated = new FrozenExpectationSet(baseline, Digest('f'), Digest('1'),
            "frozen-expectations/v1/test-translated", [], [], []);
        var mapping = new RetirementExpectationMapping(CanonicalId.Mint("form/").Value,
            CanonicalId.Mint("msa/").Value, null, "whole", CanonicalId.Mint("form/").Value);
        return new RetirementExpectationTranslation(original, translated, [mapping],
            [new RetirementBundleTextEffect(CanonicalId.Mint("bundle/").Value,
                new Dictionary<string, string>(), new Dictionary<string, string>())],
            RetirementExpectationTranslationCodec.ComputeMappingDigest([mapping]), Digest('2'), Digest('3'), Digest('4'));
    }

    private static string Digest(char marker) => "sha256:" + new string(marker, 64);

    private sealed record AssessmentFixture(string Id, BaselineToken Baseline, string? InvocationId,
        string? ReplacementId);

    private sealed class OpenSnapshotLease(SqliteConnection connection, SqliteTransaction transaction,
        PrivateArtifactStoreSnapshot snapshot) : IDisposable
    {
        internal SqliteConnection Connection { get; } = connection;
        internal SqliteTransaction Transaction { get; } = transaction;
        internal PrivateArtifactStoreSnapshot Snapshot { get; } = snapshot;

        public void Dispose()
        {
            Transaction.Dispose();
            Connection.Dispose();
        }
    }
}
