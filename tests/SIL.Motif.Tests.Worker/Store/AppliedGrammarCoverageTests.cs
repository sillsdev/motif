using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.Scripture.Fixtures;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Corpus;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

[Collection(LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "System")]
public sealed class AppliedGrammarCoverageTests(PristineProjectFixture pristine)
{
    [Fact]
    public void RetryOnlyCapabilityWarningWithholdsNegativeCoverageOnlyForRetriedWords()
    {
        const string warning = "capability: an affix rule was omitted on retry";
        var root = Directory.CreateTempSubdirectory("motif-retry-grammar-coverage-").FullName;
        try
        {
            var parser = FakeParser.Copy(Path.Combine(root, "parser"));
            object PathWithMorph() => new
            {
                morphs = new[] { new { form = Guid.NewGuid(), msa = Guid.NewGuid(), inflType = (string?)null, guessedString = (string?)null } },
            };
            FakeParser.BehaveBesideExecutable(parser, new
            {
                retryStandardError = warning + "\n",
                words = new object[]
                {
                    new { word = "empty", outcome = "timed-out", recoversAtTimeoutMs = 5000, recoveredOutcome = "no-analysis" },
                    new { word = "single", outcome = "timed-out", recoversAtTimeoutMs = 5000, analyses = new[] { PathWithMorph() } },
                    new { word = "multiple", outcome = "timed-out", recoversAtTimeoutMs = 5000, analyses = new[] { PathWithMorph(), PathWithMorph() } },
                    new { word = "untouched", outcome = "no-analysis" },
                },
            });
            string path;
            string writingSystem;
            using (var cache = pristine.NewScratch())
            {
                path = cache.ProjectId.Path;
                writingSystem = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
            }
            var workerRoot = Path.Combine(root, "worker");
            var request = new AssessRequest(path, new(false, [], ["empty", "single", "multiple", "untouched"], false, null),
                PerWordLimitMs: 700, PerWordParserWorkLimit: ParserWorkLimit.Default, Collect: ["ParseTime"]);
            var run = AssessCommand.Assess(request, workerRoot, parser, null, CancellationToken.None);
            Assert.True(run.Succeeded, run.Refusal?.Message);
            var assessmentId = Assert.Single(run.Value!.AssessmentIds);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = Open(project);
            var baseline = new BaselineRepository(database).GetLatestCaptured(ProjectWorkspaceKey.Compute(project))!;
            var invocation = new AssessmentRepository(database).Get(assessmentId).Invocation!;
            var store = new GrammarCoverageStore(project);
            var segmentation = store.Prepare(database, new(new(baseline.Token.ProjectIdentity, "source", "source-digest",
                "scope-digest", "weights", "wordmaker", "tokenizer-hash", WritingSystemProfileDigest.Read(baseline.FwDataPath),
                false, "exact", "NFD", writingSystem, new Dictionary<string, long>(), AllowsSurfaceStorage: true),
                [new("1", writingSystem, "empty", "empty", 5), new("2", writingSystem, "single", "single", 7),
                 new("3", writingSystem, "multiple", "multiple", 11), new("4", writingSystem, "untouched", "untouched", 3)]));
            var basis = new GrammarCoverageBasis(baseline.Token.ProjectIdentity, baseline.Token.SemanticSnapshotDigest,
                baseline.Token, BatchInvocationEvidence.DigestFile(baseline.FwDataPath), GrammarCoverageAssessment.ParserId(invocation),
                GrammarCoverageAssessment.LimitsId(invocation), segmentation.Id, segmentation.MappingDigest);
            var summary = store.Build(database, assessmentId, basis);

            Assert.Equal(3, summary.ProvedOccurrences.NoAnalysis);
            Assert.Equal(18, summary.ProvedOccurrences.Analysed);
            Assert.Equal(11, summary.ProvedOccurrences.Ambiguous);
            Assert.Equal(5, summary.UnknownOccurrences.NoAnalysis);
            Assert.Equal(12, summary.UnknownOccurrences.Ambiguous);
            Assert.Null(summary.Aggregate.OccurrenceBuckets.Ambiguous);
            Assert.Contains(summary.Limitations, item => item is { Code: "unsupported-grammar", Count: 23 });
            Assert.Equal("4", Assert.Single(store.ReadNoAnalysis(summary.Stamp.Id, new()).Page.Items).WordformId);
            var stderr = Path.Combine(Path.GetDirectoryName(invocation.TsvPath)!, "retry-stderr.txt");
            Assert.Equal(warning + "\n", File.ReadAllText(stderr));
            Assert.Equal(BatchInvocationEvidence.DigestFile(stderr), invocation.StoppedWordRetry!.RetryStandardErrorSha256);
            Assert.Equal(warning, Assert.Single(invocation.StoppedWordRetry.GrammarWarningLines));
            Assert.Empty(invocation.FirstPassGrammarWarningLines);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LimitedRetryKeepsTheMatchedApprovedReadingAndLeavesTheOtherUnmeasured()
    {
        const string warning = "capability: an affix rule was omitted on retry";
        const string word = "approved";
        var root = Directory.CreateTempSubdirectory("motif-retry-correctness-").FullName;
        try
        {
            string path;
            Guid formId;
            Guid msaId;
            using (var cache = pristine.NewScratch())
            {
                path = cache.ProjectId.Path;
                var services = cache.ServiceLocator;
                var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
                var form = entry.LexemeFormOA;
                var msa = entry.MorphoSyntaxAnalysesOC.First();
                formId = form.Guid;
                msaId = msa.Guid;
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    var alternate = services.GetInstance<IMoStemAllomorphFactory>().Create();
                    entry.AlternateFormsOS.Add(alternate);
                    alternate.MorphTypeRA = form.MorphTypeRA;
                    alternate.Form.set_String(cache.DefaultVernWs, word);
                    var wordform = services.GetInstance<IWfiWordformFactory>().Create(
                        TsStringUtils.MakeString(word, cache.DefaultVernWs));
                    foreach (var approved in new IMoForm[] { form, alternate })
                    {
                        var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                        wordform.AnalysesOC.Add(analysis);
                        var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                        analysis.MorphBundlesOS.Add(bundle);
                        bundle.MorphRA = approved;
                        bundle.MsaRA = msa;
                        bundle.SenseRA = entry.SensesOS[0];
                        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
                    }
                });
                new FwDataProjectLoader().Save(cache);
            }

            var parser = FakeParser.Copy(Path.Combine(root, "parser"));
            FakeParser.BehaveBesideExecutable(parser, new
            {
                retryStandardError = warning + "\n",
                words = new[] { new
                {
                    word, outcome = "timed-out", recoversAtTimeoutMs = 5000,
                    analyses = new[] { new
                    {
                        morphs = new[] { new { form = formId, msa = msaId, inflType = (string?)null, guessedString = (string?)null } },
                    } },
                } },
            });
            var request = new AssessRequest(path, new(false, [], [word], false, null),
                PerWordLimitMs: 700, PerWordParserWorkLimit: ParserWorkLimit.Default, Collect: ["Correctness"]);
            var run = AssessCommand.Assess(request, Path.Combine(root, "worker"), parser, null, CancellationToken.None);
            Assert.True(run.Succeeded, run.Refusal?.Message);
            using var database = Open(new ProjectLocator(path, Path.GetFileNameWithoutExtension(path)));
            var repository = new AssessmentRepository(database);
            var assessment = Assert.Single(run.Value!.AssessmentIds.Select(id => repository.Get(id)),
                item => item.Kind == "Correctness");
            Assert.Equal(1, assessment.Invocation!.StoppedWordRetry!.Recovered);
            var row = Assert.Single(assessment.Words!);

            Assert.Equal(2, row.Correctness!.Expected);
            Assert.Equal(1, row.Correctness.Matched);
            Assert.Equal("unavailable", row.Correctness.Status);
            Assert.Contains(warning, row.Correctness.Unavailable);
            Assert.Contains(warning, row.Morphology!.Unavailable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FirstPassCapabilityWarningLeavesAnAnalysedRowsCorrectnessUnmeasured()
    {
        const string warning = "capability: an affix rule was omitted in the first pass";
        const string word = "approved";
        var root = Directory.CreateTempSubdirectory("motif-firstpass-correctness-").FullName;
        try
        {
            string path;
            Guid formId;
            Guid msaId;
            using (var cache = pristine.NewScratch())
            {
                path = cache.ProjectId.Path;
                var services = cache.ServiceLocator;
                var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
                var form = entry.LexemeFormOA;
                var msa = entry.MorphoSyntaxAnalysesOC.First();
                formId = form.Guid;
                msaId = msa.Guid;
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    var alternate = services.GetInstance<IMoStemAllomorphFactory>().Create();
                    entry.AlternateFormsOS.Add(alternate);
                    alternate.MorphTypeRA = form.MorphTypeRA;
                    alternate.Form.set_String(cache.DefaultVernWs, word);
                    var wordform = services.GetInstance<IWfiWordformFactory>().Create(
                        TsStringUtils.MakeString(word, cache.DefaultVernWs));
                    foreach (var approved in new IMoForm[] { form, alternate })
                    {
                        var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                        wordform.AnalysesOC.Add(analysis);
                        var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                        analysis.MorphBundlesOS.Add(bundle);
                        bundle.MorphRA = approved;
                        bundle.MsaRA = msa;
                        bundle.SenseRA = entry.SensesOS[0];
                        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
                    }
                });
                new FwDataProjectLoader().Save(cache);
            }

            var parser = FakeParser.Copy(Path.Combine(root, "parser"));
            FakeParser.BehaveBesideExecutable(parser, new
            {
                firstPassStandardError = warning + "\n",
                words = new[] { new
                {
                    word, outcome = "complete",
                    analyses = new[] { new
                    {
                        morphs = new[] { new { form = formId, msa = msaId, inflType = (string?)null, guessedString = (string?)null } },
                    } },
                } },
            });
            var request = new AssessRequest(path, new(false, [], [word], false, null),
                PerWordLimitMs: 700, PerWordParserWorkLimit: ParserWorkLimit.Default, Collect: ["Correctness"]);
            var run = AssessCommand.Assess(request, Path.Combine(root, "worker"), parser, null, CancellationToken.None);
            Assert.True(run.Succeeded, run.Refusal?.Message);
            using var database = Open(new ProjectLocator(path, Path.GetFileNameWithoutExtension(path)));
            var repository = new AssessmentRepository(database);
            var assessment = Assert.Single(run.Value!.AssessmentIds.Select(id => repository.Get(id)),
                item => item.Kind == "Correctness");
            Assert.Null(assessment.Invocation!.StoppedWordRetry);
            Assert.Equal(warning, Assert.Single(assessment.Invocation.FirstPassGrammarWarningLines));
            var row = Assert.Single(assessment.Words!);

            Assert.Equal(2, row.Correctness!.Expected);
            Assert.Equal(1, row.Correctness.Matched);
            Assert.Equal("unavailable", row.Correctness.Status);
            Assert.Contains(warning, row.Correctness.Unavailable);
            Assert.Contains(warning, row.Morphology!.Unavailable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [RealParserFact]
    public void ApplyAndReassessReuseTheSegmentationAndChangeAnalysisCounts()
    {
        var root = Directory.CreateTempSubdirectory("motif-applied-grammar-coverage-").FullName;
        try
        {
            const string version = "1.0";
            const string draft = "digraph-grammar-coverage";
            string path;
            Guid phonemeId;
            using (var cache = pristine.NewScratch())
            {
                path = cache.ProjectId.Path;
                var form = cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(pristine.Seed.FirstLexemeFormId);
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    form.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("na", cache.DefaultVernWs)));
                RealParserProject.PrepareForParsing(cache, "n", "g", "a", "m", "o", "t", "i", "f", "b");
                phonemeId = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
                    .Single(phoneme => phoneme.CodesOS.Any(code =>
                        code.Representation.get_String(cache.DefaultVernWs)?.Text == "n")).Guid;
            }

            var request = new AssessRequest(path, new(false, [], ["nga", "na"], false, null), PerWordLimitMs: 5000,
                PerWordParserWorkLimit: ParserWorkLimit.Default, Collect: ["ParseTime"]);
            var workerRoot = Path.Combine(root, "worker");
            var parserPath = PanGlossExecutable.TryLocate()!;
            var parserId = ParserExecutableIdentity.ReadVersion(parserPath) + "/" + BatchInvocationEvidence.DigestFile(parserPath);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            var capturedBefore = BaselineCaptureCommand.Capture(new(path), workerRoot);
            Assert.True(capturedBefore.Succeeded, capturedBefore.Refusal?.Message);
            var baselineBefore = capturedBefore.Value!.Token;
            var beforeRun = AssessCommand.Assess(request, workerRoot, parserPath, null, CancellationToken.None);
            Assert.True(beforeRun.Succeeded, beforeRun.Refusal?.Message);

            var store = new GrammarCoverageStore(project);
            using var database = Open(project);
            using var local = AmbientEnvironment.Push(new Dictionary<string, string?>
            {
                [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(root, "scripture-local.json"),
            });
            using var source = TinyProject.Create();
            foreach (var file in Directory.EnumerateFiles(source.Folder, "*.SFM")) File.Delete(file);
            File.WriteAllLines(Path.Combine(source.Folder, "41MAT.SFM"),
                ["\\id MAT", "\\c 1", "\\v 1 nga nga nga nga nga nga nga na na"]);
            var entry = new SourceListEntry(TinyProject.Name, "Synthetic", SourceKind.Paratext, "MAT", "English", null, null);
            ScriptureLocalStore.ForInstallation().SetSourcePath(ProjectWorkspaceKey.Compute(project), entry.Id, source.Folder);
            var beforeSource = new BaselineRepository(database).GetLatestCaptured(ProjectWorkspaceKey.Compute(project))!;
            Assert.Equal(baselineBefore, beforeSource.Token);
            var inventory = RefreshInventory();

            var before = Build(store, database, inventory, Assert.Single(beforeRun.Value!.AssessmentIds),
                baselineBefore, parserId, request);
            Assert.Equal(2, before.Aggregate.OccurrenceBuckets.Analysed);
            Assert.Equal(7, before.Aggregate.OccurrenceBuckets.NoAnalysis);

            var created = ProposalCommands.New(new(path, version, draft, "Teach the grammar a digraph"));
            Assert.True(created.Succeeded, created.Refusal?.Message);
            var edit = JsonSerializer.Serialize(new
            {
                target = CanonicalId.FromGuid(phonemeId).Value,
                add = new[] { new { representation = "ng" } },
            });
            var staged = ProposalCommands.ComposeIntent(new(path, version, draft, "compose-edit-phoneme", edit));
            Assert.True(staged.Succeeded, staged.Refusal?.Message);
            DraftRationale.Author(path, draft, "Add a digraph", "The same sound is also written with two letters.");
            var finalized = ProposalCommands.Finalize(new(path, version, draft));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
            var dryRun = DryRunJobRunner.Run(path, version, finalized.Value!.ProposalId);
            Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
            var applied = ProposalCommands.Apply(new ApplyRequest(path, version, finalized.Value.ProposalId, Force: true));
            Assert.True(applied.Succeeded, applied.Refusal?.Message);

            var capturedAfter = BaselineCaptureCommand.Capture(new(path), workerRoot);
            Assert.True(capturedAfter.Succeeded, capturedAfter.Refusal?.Message);
            var afterRun = AssessCommand.Assess(request, workerRoot, parserPath, null, CancellationToken.None);
            Assert.True(afterRun.Succeeded, afterRun.Refusal?.Message);
            var after = Build(store, database, RefreshInventory(), Assert.Single(afterRun.Value!.AssessmentIds),
                capturedAfter.Value!.Token, parserId, request);

            Assert.NotEqual(before.Stamp.Revision, after.Stamp.Revision);
            Assert.Equal(before.Stamp.SegmentationId, after.Stamp.SegmentationId);
            Assert.Equal(before.WeightDigest, after.WeightDigest);
            Assert.Equal(before.MappingDigest, after.MappingDigest);
            Assert.Equal(before.Aggregate.Occurrences, after.Aggregate.Occurrences);
            Assert.Equal(before.Aggregate.DistinctWordforms, after.Aggregate.DistinctWordforms);
            Assert.Equal(9, after.Aggregate.OccurrenceBuckets.Analysed);
            Assert.Equal(0, after.Aggregate.OccurrenceBuckets.NoAnalysis);
            Assert.Single(store.ReadNoAnalysis(before.Stamp.Id, new()).Page.Items);
            Assert.Empty(store.ReadNoAnalysis(after.Stamp.Id, new()).Page.Items);
            Assert.NotEqual(before.Stamp.SourceGeneration, after.Stamp.SourceGeneration);
            var priorBasis = new GrammarCoverageBasis(baselineBefore.ProjectIdentity, baselineBefore.SemanticSnapshotDigest,
                baselineBefore, before.Stamp.GrammarId!, parserId,
                GrammarCoverageAssessment.LimitsId(request.PerWordLimitMs, request.PerWordParserWorkLimit!, null),
                inventory.Id, inventory.MappingDigest, SourceGeneration: inventory.SourceGeneration);
            var retried = store.Build(database, before.AssessmentId, priorBasis);
            Assert.Equal(before.Stamp.Id, retried.Stamp.Id);
            Assert.Equal(before.Aggregate, retried.Aggregate);

            GrammarCoverageSegmentation RefreshInventory()
            {
                using var profile = BaselineReadCache.Open(path);
                var writingSystem = profile.Cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
                var identity = ProgressSourceIdentity.Capture(path)!;
                Assert.True(SourceIndexRefresh.Build(database, entry, source.Folder, writingSystem.Id, identity,
                    SourceApprovals.Tokeniser(profile.Cache)));
                var generation = new SourceIndexRepository(database).ReadState(entry.Id)!.Generation;
                return store.PrepareParatext(database, baselineBefore.ProjectIdentity, entry.Id, generation,
                    identity.SidecarDigest!, writingSystem);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MotifDatabase Open(ProjectLocator project) =>
        new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);

    private static GrammarCoverageSummary Build(GrammarCoverageStore store, MotifDatabase database,
        GrammarCoverageSegmentation segmentation, string assessmentId, BaselineToken baseline, string parserId,
        AssessRequest request)
    {
        var registered = new BaselineRepository(database).GetLatestCaptured(ProjectWorkspaceKey.Compute(database.Project))!;
        Assert.Equal(baseline, registered.Token);
        var grammar = BatchInvocationEvidence.DigestFile(registered.FwDataPath);
        var basis = new GrammarCoverageBasis(baseline.ProjectIdentity, baseline.SemanticSnapshotDigest, baseline, grammar,
            parserId, GrammarCoverageAssessment.LimitsId(request.PerWordLimitMs, request.PerWordParserWorkLimit!, null),
            segmentation.Id, segmentation.MappingDigest, SourceGeneration: segmentation.SourceGeneration);
        return store.Build(database, assessmentId, basis);
    }
}
