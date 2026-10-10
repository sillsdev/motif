using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker;
using CandidateEvidenceBuilder = SIL.Motif.Worker.Parsimony.ParsimonyCandidateEvidenceBuilder;
using Xunit;

namespace SIL.Motif.Tests.Worker;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyCandidateEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyCandidateEvidenceTests),
        Guid.NewGuid().ToString("N"));
    private readonly PristineProjectFixture _pristine;
    private readonly SeededProject _seed;
    private readonly ProjectLocator _project;
    private readonly MotifDatabase _database;
    private readonly JobRepository _jobs;
    private readonly BaselineRepository _baselines;
    private readonly BaselineToken _token;
    private readonly string _projectKey;
    private readonly string _publishedRoot;
    private readonly string _publishedFwDataPath;
    private Guid _prohibitionGuid;

    public ParsimonyCandidateEvidenceTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _seed = pristine.Seed;
        Directory.CreateDirectory(_root);
        using var master = pristine.NewScratch();
        SeedAdhocProhibition(master);
        SeededProject.SeedText(master, _seed);
        new FwDataProjectLoader().Save(master);
        _publishedRoot = Path.Combine(_root, "baseline");
        _publishedFwDataPath = PublishedBaselineFixture.PublishAsync(master, _publishedRoot)
            .GetAwaiter().GetResult();
        _project = new ProjectLocator(Path.Combine(_root, "live", NewLangProjFixture.ProjectName + ".fwdata"),
            "candidate-evidence-project");
        Directory.CreateDirectory(Path.GetDirectoryName(_project.FullFwDataPath)!);
        File.WriteAllText(_project.FullFwDataPath, "the live project is not candidate storage");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _jobs = new JobRepository(_database);
        _baselines = new BaselineRepository(_database);
        _projectKey = ProjectWorkspaceKey.Compute(_project);
        _token = new BaselineToken("candidate-evidence-project", "sha256:" + new string('1', 64),
            "projection-v1", "2026-10-07T00:00:00Z", "sha256:" + new string('2', 64));
        using var saved = new FwDataProjectLoader().LoadScratchCache(_publishedFwDataPath);
        _baselines.Record(_projectKey,
            new BaselinePublication(_publishedRoot, _publishedFwDataPath, _token),
            DateTimeOffset.Parse("2026-10-07T00:00:00Z"), DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
            TextWordsProjectionBuilder.Build(saved, CancellationToken.None), ProjectSummaryReader.Read(saved));
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void PrerequisiteRevisionRemainsPinnedAfterItsDraftChanges()
    {
        var prerequisiteId = CanonicalId.Mint("proposal/");
        var prerequisiteJson = Proposal(prerequisiteId, Guid.NewGuid(), "original prerequisite");
        var prerequisite = FrozenProposalRevision.Create(prerequisiteJson);
        var requested = Proposal(CanonicalId.Mint("proposal/"), Guid.NewGuid(), "requested change",
            [prerequisiteId.Value]);
        var frozenRequested = FrozenProposalRevision.Create(requested);
        var frozenJob = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion, frozenRequested,
            [prerequisite], null);
        var serialized = JsonSerializer.Serialize(frozenJob);

        var laterDraft = Proposal(prerequisiteId, Guid.NewGuid(), "edited draft");
        Assert.NotEqual(prerequisiteJson, laterDraft);

        var reloaded = DryRunJobInput.Parse(serialized);
        var plan = reloaded.BuildExecutionPlan([]);

        Assert.Equal(prerequisite.ProposalJson, reloaded.Prerequisites.Single().ProposalJson);
        Assert.Equal("original prerequisite", plan.Prerequisites.Single().Operations.Single().After!
            .Value.GetProperty("text").GetString());
    }

    [Fact]
    public void CandidateIdentityChangesWithProposalMaterialAtTheSameParserFingerprint()
    {
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalOne = FrozenProposalRevision.Create(Proposal(proposalId, Guid.NewGuid(), "first value"));
        var proposalTwo = FrozenProposalRevision.Create(Proposal(proposalId, Guid.NewGuid(), "second value"));
        var token = new BaselineToken("project", "sha256:" + new string('1', 64), "projection-v1",
            "2026-10-07T00:00:00Z", "sha256:" + new string('2', 64));
        var anchorDigest = "sha256:" + new string('3', 64);
        var effectDigest = "sha256:" + new string('4', 64);
        var sourceDigest = "sha256:" + new string('5', 64);
        var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);

        var one = CandidateEvidenceBuilder.ComputeCandidateIdentity(proposalOne, [], token, effectDigest,
            anchorDigest, sourceDigest, "sha256:" + new string('6', 64), "sha256:model", "sha256:parser", 6,
            1, scope, "sha256:judgments");
        var two = CandidateEvidenceBuilder.ComputeCandidateIdentity(proposalTwo, [], token, effectDigest,
            anchorDigest, sourceDigest, "sha256:" + new string('6', 64), "sha256:model", "sha256:parser", 6,
            1, scope, "sha256:judgments");

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void CandidateReadbackMustMatchThePublishedDryRunAnchorAndEffectDigest()
    {
        var effectDigest = "sha256:" + new string('4', 64);
        var intentDigest = "sha256:" + new string('7', 64);
        var anchor = new BoundDryRunAnchor(intentDigest, "sha256:" + new string('8', 64), effectDigest,
            "1.0.0.0", "1.0.0.0", "projection-v1", "20261007T000000Z");
        var dryRun = new DryRun(intentDigest, "note", [], effectDigest, anchor);
        var expected = JsonSerializer.Serialize(new
        {
            intentDigest,
            baselineNote = "note",
            expectedEffects = Array.Empty<object>(),
            effectDigest,
            anchor,
        }, SIL.Motif.Contract.MotifJson.CreateOptions());

        CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, dryRun);
        var rerun = dryRun with { Anchor = anchor with { DryRunAtUtc = "20261007T000001Z" } };
        Assert.Equal(anchor, CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, rerun));

        var changedAnchor = anchor with { FootprintDigest = "sha256:" + new string('9', 64) };
        var changed = dryRun with { Anchor = changedAnchor };
        Assert.Throws<InvalidDataException>(() => CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, changed));
        var changedEffect = dryRun with { EffectDigest = "sha256:" + new string('a', 64) };
        Assert.Throws<InvalidDataException>(() => CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, changedEffect));
    }

    [Fact]
    public void DeletedApprovedAnalysisIsInconclusiveUntilAnExplicitMappingExists()
    {
        var approved = new ParsimonyApprovedAnalysisTuple("wordform-guid", "analysis-guid",
            "sha256:" + new string('a', 64));

        var preservation = CandidateEvidenceBuilder.CompareApprovedAnalyses([approved], []);

        Assert.Equal("inconclusive", preservation.Status);
        Assert.Equal(1, preservation.BaselineCount);
        Assert.Equal(0, preservation.PreservedCount);
        Assert.Equal(approved, Assert.Single(preservation.UnmatchedBaselineTuples));
    }

    [Fact]
    public async Task CandidateFactsAndEvidenceUseTheFrozenDisabledProhibitionAndKeepTheBaselineUnchanged()
    {
        var sourceDigest = DigestDirectory(_publishedRoot);
        var liveDigest = DigestFile(_project.FullFwDataPath);
        var proposalJson = DisableProhibitionProposal(_prohibitionGuid);
        var source = new DryRunSourceBinding(_token, _publishedRoot, _publishedFwDataPath,
            "sha256:" + DigestFile(_publishedFwDataPath));
        var dryRunInput = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion,
            FrozenProposalRevision.Create(proposalJson), [], source);
        var dryRunJob = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey,
            ProjectJobHandlers.DryRunKind, JsonSerializer.Serialize(dryRunInput,
                SIL.Motif.Contract.MotifJson.CreateOptions()), "2026-10-07T00:00:00Z");
        var lanes = new ProjectLaneRegistry(_ => _token);
        try
        {
            var loader = new FwDataProjectLoader();
            var scratchFactory = new BaselineScratchFactory(loader);
            var dryRunHandler = new DryRunJobHandler(_baselines, new ProposalRepository(_database), lanes,
                _ => null,
                (path, _) =>
                {
                    var scratch = scratchFactory.OpenSingleUse(path);
                    var applied = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                        .Select(entry => entry.ProposalId).ToArray();
                    return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((applied, scratch));
                },
                (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)));
            var dryRunClaim = DryRunJobTestHarness.Claim(_jobs, dryRunJob.JobId);
            var completedDryRun = await DryRunJobTestHarness.RunAndFinishAsync(_jobs, dryRunHandler,
                dryRunClaim, _project);
            Assert.Equal(JobStatus.CompletedDryRunOnly, completedDryRun.Status);

            var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser"));
            FakeParser.WriteFactsFixture(parserPath, FactsFixture(disableChangedRule: false));
            FakeParser.WriteCandidateFactsFixture(parserPath, FactsFixture(disableChangedRule: true));
            using var invoker = new PanGlossInvoker(parserPath);
            var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
            var candidateInput = new ParsimonyCandidateJobInput(ParsimonyCandidateJobInput.CurrentSchemaVersion,
                dryRunJob.JobId, "P-adhoc-duplicate", scope);
            var candidateJob = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey,
                ParsimonyCandidateEvidenceBuilder.JobKind,
                JsonSerializer.Serialize(candidateInput, SIL.Motif.Contract.MotifJson.CreateOptions()),
                "2026-10-07T00:01:00Z");
            var candidateClaim = Claim(candidateJob.JobId);
            var handler = new ParsimonyCandidateEvidenceBuilder(_database, _projectKey, _project,
                new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker, lanes);

            var outcome = await handler.RunAsync(candidateClaim, CancellationToken.None);

            var completed = Assert.IsType<JobOutcome>(outcome);
            Assert.True(completed.Status == JobStatus.Completed, completed.ResultJson);
            candidateClaim.Transition(completed.Status, completed.Category, completed.ResultJson);
            var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(completed.ResultJson!,
                SIL.Motif.Contract.MotifJson.CreateOptions())!;
            Assert.Equal("baseline", response.Before.Inputs.InputKind);
            Assert.Equal("candidate", response.After.Inputs.InputKind);
            Assert.NotEqual(response.Before.Inputs.ModelFingerprint, response.After.Inputs.ModelFingerprint);
            Assert.NotEqual(response.Before.Inputs.Evidence.Sha256, response.After.Inputs.Evidence.Sha256);
            Assert.Contains(response.FindingChanges, item => item.Change is "evidence-changed" or "removed");
            Assert.Equal("preserved", response.ApprovedAnalysisPreservation.Status);

            var artifacts = new EvidenceArtifactRepository(_database);
            var before = artifacts.Get(response.Before.Inputs.BundleId)!;
            var after = artifacts.Get(response.After.Inputs.BundleId)!;
            Assert.Equal("0", ReadText(before.GrammarFactsPath,
                $"SELECT disabled FROM adhoc_prohibition WHERE prohibition_guid='{_prohibitionGuid:D}';"));
            Assert.Equal("1", ReadText(after.GrammarFactsPath,
                $"SELECT disabled FROM adhoc_prohibition WHERE prohibition_guid='{_prohibitionGuid:D}';"));
            Assert.Equal("complete", ReadText(before.GrammarFactsPath,
                "SELECT status FROM artifact_section WHERE section='adhoc_groups';"));
            Assert.Equal("complete", ReadText(after.GrammarFactsPath,
                "SELECT status FROM artifact_section WHERE section='adhoc_groups';"));
            Assert.Equal("one authored group rationale", ReadText(before.GrammarFactsPath,
                "SELECT text FROM adhoc_group_text WHERE group_guid=" +
                "'40000000-0000-0000-0000-000000000001' AND field='description' AND writing_system='qaa';"));
            Assert.Equal("3", ReadText(before.GrammarFactsPath, "SELECT COUNT(*) FROM adhoc_group_member;"));
            Assert.Equal("baseline", ReadText(before.EvidencePath,
                "SELECT input_kind FROM artifact_metadata WHERE singleton=1;"));
            Assert.Equal("candidate", ReadText(after.EvidencePath,
                "SELECT input_kind FROM artifact_metadata WHERE singleton=1;"));
            Assert.Equal(sourceDigest, DigestDirectory(_publishedRoot));
            Assert.Equal(liveDigest, DigestFile(_project.FullFwDataPath));
            Assert.DoesNotContain("batch", FakeParser.Invocations(parserPath));
        }
        finally
        {
            lanes.Dispose();
        }
    }

    private static string Proposal(CanonicalId proposalId, Guid targetId, string text,
        IReadOnlyList<string>? requires = null)
    {
        var requiresJson = requires is null ? "" :
            "\"requires\": [" + string.Join(",", requires.Select(id => JsonSerializer.Serialize(id))) + "],";
        var operationId = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.FromGuid(targetId).Value;
        var textJson = JsonSerializer.Serialize(text);
        return $$"""
            {
              "contractVersions": {"lexical": "1.0"},
              "proposalId": "{{proposalId.Value}}",
              {{requiresJson}}
              "operations": [
                {
                  "operationId": "{{operationId}}",
                  "kind": "lexical/lexSense/setGloss",
                  "target": "{{target}}",
                  "after": {"ws": "en", "text": {{textJson}}}
                }
              ]
            }
            """;
    }

    private void SeedAdhocProhibition(LcmCache cache)
    {
        var services = cache.ServiceLocator;
        var entries = services.GetInstance<ILexEntryRepository>();
        var first = entries.GetObject(_seed.FirstEntryId);
        var second = entries.GetObject(_seed.SecondEntryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var prohibition = services.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
            prohibition.FirstAllomorphRA = first.LexemeFormOA;
            prohibition.RestOfAllosRS.Add(second.LexemeFormOA);
            _prohibitionGuid = prohibition.Guid;
        });
    }

    private string DisableProhibitionProposal(Guid targetId)
    {
        var proposalId = CanonicalId.Mint("proposal/").Value;
        var operationId = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.FromGuid(targetId).Value;
        return $$"""
            {
              "contractVersions": {"grammar": "1.0"},
              "proposalId": "{{proposalId}}",
              "operations": [
                {
                  "operationId": "{{operationId}}",
                  "kind": "grammar/moAdhocProhib/setDisabled",
                  "target": "{{target}}",
                  "after": {"value": true}
                }
              ]
            }
            """;
    }

    private object FactsFixture(bool disableChangedRule)
    {
        var primary = _seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = _seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        var ids = new[] { _prohibitionGuid.ToString("D").ToLowerInvariant(),
            "30000000-0000-0000-0000-000000000001", "30000000-0000-0000-0000-000000000002" };
        return new
        {
            prohibitions = ids.Select((id, index) => new
            {
                prohibitionGuid = id,
                kind = "allomorph",
                disabled = index == 0 && disableChangedRule,
                adjacency = "somewhereToLeft",
                primaryGuid = primary,
                targetKind = "allomorph",
                others = new[] { new { targetGuid = other, targetKind = "allomorph" } },
                loaded = true,
                compacted = false,
            }).ToArray(),
            groups = new[]
            {
                new
                {
                    groupGuid = "40000000-0000-0000-0000-000000000001",
                    texts = new[]
                    {
                        new { field = "name", writingSystem = "qaa", text = "Fixture group" },
                        new
                        {
                            field = "description", writingSystem = "qaa", text = "one authored group rationale",
                        },
                    },
                    members = ids,
                },
            },
        };
    }

    private ClaimedJob Claim(string jobId)
    {
        var claimed = new JobClaims(_database).Claim(_projectKey, "candidate-evidence-test",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        Assert.Equal(jobId, claimed.JobId);
        return new ClaimedJob(_jobs, claimed);
    }

    private static string ReadText(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static string DigestFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string DigestDirectory(string root) => string.Join("\n",
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + ":" + DigestFile(path)));
}
