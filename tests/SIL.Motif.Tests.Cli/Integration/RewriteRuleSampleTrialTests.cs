using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Cli.Integration;

[Trait("MotifTestLevel", "System")]
public sealed class RewriteRuleSampleTrialTests(ITestOutputHelper output)
{
    [RewriteRuleSampleFact]
    public async Task EnabledRuleComposerChangesSampleReadingsAndCompletesBoundedTrial()
    {
        var sourceRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var root = Path.Combine(Path.GetTempPath(), "motif-rewrite-rule-sample-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "project-copy");
        var workerRoot = Path.Combine(root, "worker");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(workerRoot);
        CopyTree(Path.Combine(sourceRoot, "WritingSystemStore"), Path.Combine(projectRoot, "WritingSystemStore"));
        var projectPath = Path.Combine(projectRoot, "mbugwe.fwdata");
        File.Copy(Path.Combine(sourceRoot, "mbugwe.fwdata"), projectPath);
        var words = new[] { "kaana", "kena" };
        var loader = new FwDataProjectLoader();

        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), workerRoot);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);

            Guid existingRuleGuid;
            Guid? followingRuleGuid;
            int existingRuleIndex;
            string existingRuleName;
            CanonicalId inputPhonemeId;
            CanonicalId outputPhonemeId;
            CanonicalId leftPhonemeId;
            CanonicalId rightPhonemeId;
            IReadOnlyDictionary<CanonicalId, ObjectSnapshot> beforeSnapshot;
            using (var cache = loader.LoadCache(projectPath))
            {
                var data = cache.LangProject.PhonologicalDataOA;
                var phonemes = data.PhonemeSetsOS.SelectMany(set => set.PhonemesOC).ToArray();
                var inputPhoneme = FindPhoneme(cache, phonemes, "aa");
                var outputPhoneme = FindPhoneme(cache, phonemes, "e");
                var leftPhoneme = FindPhoneme(cache, phonemes, "k");
                var rightPhoneme = FindPhoneme(cache, phonemes, "n");
                inputPhonemeId = CanonicalId.FromGuid(inputPhoneme.Guid);
                outputPhonemeId = CanonicalId.FromGuid(outputPhoneme.Guid);
                leftPhonemeId = CanonicalId.FromGuid(leftPhoneme.Guid);
                rightPhonemeId = CanonicalId.FromGuid(rightPhoneme.Guid);
                beforeSnapshot = SoundSystemSnapshots.Read(cache);
                var existing = data.PhonRulesOS.Select((rule, index) => (rule, index))
                    .SingleOrDefault(pair => pair.rule.Name.get_String(cache.DefaultAnalWs)?.Text == "fix ay in verbs");
                Assert.NotNull(existing.rule);
                existingRuleGuid = existing.rule.Guid;
                existingRuleIndex = existing.index;
                followingRuleGuid = existing.index + 1 < data.PhonRulesOS.Count
                    ? data.PhonRulesOS[existing.index + 1].Guid
                    : null;
                existingRuleName = existing.rule.Name.get_String(cache.DefaultAnalWs)!.Text;
            }

            var before = await RunParserAsync(projectPath, words, "o7-sample-before");
            Assert.NotEmpty(before.Single(word => word.Word == words[0]).Analyses);
            Assert.Empty(before.Single(word => word.Word == words[1]).Analyses);
            var baselineAssessment = AssessCommand.Assess(
                new AssessRequest(projectPath,
                    new SelectionRequest(false, [], words, false, null,
                        PerWordStepLimit: new StepCap(2_000_000)),
                    PerWordLimitMs: 5000, PerWordStepLimit: new StepCap(2_000_000)),
                workerRoot, PanGlossExecutable.TryLocate(), null, CancellationToken.None);
            Assert.True(baselineAssessment.Succeeded, baselineAssessment.Refusal?.Message);
            var beforeCorrectnessId = baselineAssessment.Value!.Measurements
                .Single(measurement => measurement.Kind == AssessmentKinds.Correctness).AssessmentId;
            var createdDraft = ProposalCommands.New(new NewDraftRequest(
                projectPath, MotifProductVersion.CurrentText, PendingChanges.DraftName, "Rewrite rule sample check"));
            Assert.True(createdDraft.Succeeded, createdDraft.Refusal?.Message);
            var proposalId = CanonicalId.Parse(createdDraft.Value!.ProposalId);
            var afterRule = followingRuleGuid is { } followingRule
                ? CanonicalId.FromGuid(followingRule)
                : (CanonicalId?)null;
            IReadOnlyList<OperationEnvelope> operations;
            using (var cache = loader.LoadCache(projectPath))
            {
                var phonemes = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS
                    .SelectMany(set => set.PhonemesOC).ToArray();
                operations = AuthorPhonologicalRuleComposer.Build(cache,
                    new("long a becomes e between k and n", PhonologicalRuleDirection.LeftToRightIterative,
                        [new(Phoneme: inputPhonemeId)], [new(Phoneme: outputPhonemeId)],
                        [new(Phoneme: leftPhonemeId)], [new(Phoneme: rightPhonemeId)],
                        new(CanonicalId.FromGuid(existingRuleGuid), afterRule)));
            }
            var ruleId = operations.Single(operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create)
                .EntityId!.Value;
            var rhsId = operations.Single(operation => operation.Kind == PhRegularRuleRightHandSidesOperationKinds.Create)
                .EntityId!.Value;
            var proposal = new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" },
                proposalId, null, operations);
            SavePendingDraft(projectPath, proposal);

            using (var cache = loader.LoadCache(projectPath))
            {
                var dryRun = ScratchDryRun.Of(cache, proposal);
                Assert.Contains(dryRun.ExpectedEffects, effect =>
                    effect.Field == SIL.Motif.Model.Snapshot.SnapshotFields.PhPhonDataPhonRules);
                ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "rewrite-rule-sample");
                loader.Save(cache);
            }

            Guid[] expectedOrder;
            using (var reopened = loader.LoadCache(projectPath))
            {
                var rules = reopened.LangProject.PhonologicalDataOA.PhonRulesOS;
                expectedOrder = rules.Select(rule => rule.Guid).ToArray();
                Assert.Equal(existingRuleGuid, expectedOrder[existingRuleIndex]);
                Assert.Equal(ruleId.ToGuid(), expectedOrder[existingRuleIndex + 1]);
                var rule = Assert.IsAssignableFrom<IPhRegularRule>(reopened.ServiceLocator
                    .GetInstance<ICmObjectRepository>().GetObject(ruleId.ToGuid()));
                Assert.False(rule.Disabled);
                Assert.Equal("long a becomes e between k and n", rule.Name.get_String(reopened.DefaultAnalWs)?.Text);
                Assert.Equal(0, rule.Direction);
                var rhs = Assert.Single(rule.RightHandSidesOS);
                Assert.Equal(rhsId.ToGuid(), rhs.Guid);
                Assert.Equal(inputPhonemeId.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(Assert.Single(rule.StrucDescOS)).FeatureStructureRA.Guid);
                Assert.Equal(leftPhonemeId.ToGuid(),
                    Assert.IsAssignableFrom<IPhSimpleContextSeg>(rhs.LeftContextOA).FeatureStructureRA.Guid);
                Assert.Equal(rightPhonemeId.ToGuid(),
                    Assert.IsAssignableFrom<IPhSimpleContextSeg>(rhs.RightContextOA).FeatureStructureRA.Guid);
                Assert.Equal(outputPhonemeId.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(
                    Assert.Single(rhs.StrucChangeOS)).FeatureStructureRA.Guid);
            }

            IReadOnlyDictionary<CanonicalId, ObjectSnapshot> afterSnapshot;
            using (var reopened = loader.LoadCache(projectPath)) afterSnapshot = SoundSystemSnapshots.Read(reopened);
            Assert.NotEmpty(SoundSystemSnapshots.Compare(beforeSnapshot, afterSnapshot));

            var after = await RunParserAsync(projectPath, words, "o7-sample-after");
            Assert.Empty(after.Single(word => word.Word == words[0]).Analyses);
            Assert.NotEmpty(after.Single(word => word.Word == words[1]).Analyses);
            output.WriteLine($"parserReadings={string.Join(";", before.Select((word, index) =>
                $"{word.Word}:{word.Analyses.Count}->{after[index].Analyses.Count}"))}");

            var trialStart = CliProcess.CreateStartInfoWithAdvancedAiModePath(workerRoot,
                PanGlossExecutable.TryLocate(), true, Path.Combine(root, "advanced-ai-mode.json"),
                "trial", "--pending", "--project", projectPath, "--words", string.Join(',', words),
                "--wait", "--wait-timeout-ms", "30000", "--before-correctness", beforeCorrectnessId, "--json");
            trialStart.Environment[ProcessRunnerLauncher.ExecutableVariable] = BuildOutput.Worker;
            trialStart.Environment[RunnerOptions.NamespaceVariable] = Guid.NewGuid().ToString("N");
            trialStart.Environment[RunnerOptions.IdleVariable] = "1";
            var trial = await CliProcess.RunAsync(trialStart);
            Assert.True(trial.ExitCode == 0, trial.Error + Environment.NewLine + trial.Output);
            using var response = JsonDocument.Parse(trial.Output);
            Assert.True(response.RootElement.GetProperty("evidenceComplete").GetBoolean());
            var numbers = response.RootElement.GetProperty("numbers");
            Assert.Equal(2, numbers.GetProperty("sharedWordCount").GetInt32());
            Assert.Equal(0, numbers.GetProperty("touchedWordsCovered").GetInt32());
            output.WriteLine("trial=" + response.RootElement.GetRawText());

            DraftRationale.Author(projectPath, PendingChanges.DraftName,
                "Create an enabled sample rule", "Verify rewrite-rule Preflight after Apply.");
            var finalized = ProposalCommands.Finalize(new FinalizeRequest(
                projectPath, MotifProductVersion.CurrentText, PendingChanges.DraftName));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
            var preflight = ProposalCommands.Preflight(new PreflightRequest(
                projectPath, MotifProductVersion.CurrentText, finalized.Value!.ProposalId));
            Assert.True(preflight.Succeeded, preflight.Refusal?.Message);
            Assert.Empty(preflight.Value!.Changes);

            output.WriteLine($"existingRule={existingRuleName}; insertedIndex={existingRuleIndex + 1}; " +
                $"input=aa; output=e; leftContext=k; rightContext=n; " +
                $"ruleDisabled=false; readings={string.Join(";", before.Select((word, index) =>
                    $"{word.Word}:{word.Analyses.Count}->{after[index].Analyses.Count}"))}; " +
                "boundedTrial=completed; preflight=clear");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<IReadOnlyList<SIL.Motif.Contract.Responses.ParseWordEvidence>> RunParserAsync(
        string projectPath, IReadOnlyList<string> words, string namespaceName)
    {
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(projectPath, words, TimeSpan.FromSeconds(5),
                PerWordStepLimit: new StepCap(2_000_000)) { CollectAnalyses = true },
            "test:" + namespaceName, cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
        Assert.True(outcome is PanGlossOutcome.Completed, $"Expected a completed batch, received {outcome}.");
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var evidence = ParseMorphEvidence.Read(completed.MorphologyOutput!, words);
        Assert.All(evidence, word => Assert.False(word.Capped || word.TimedOut || word.InvalidShape));
        return evidence;
    }

    private static IPhPhoneme FindPhoneme(LcmCache cache, IReadOnlyList<IPhPhoneme> phonemes, string representation) =>
        phonemes.Single(phoneme => phoneme.CodesOS.Any(code =>
            code.Representation.get_String(cache.DefaultVernWs)?.Text == representation));

    private static void SavePendingDraft(string projectPath, Proposal proposal)
    {
        var draft = new DraftDocument
        {
            ProposalId = proposal.ProposalId.Value,
            ContractVersions = new Dictionary<string, string>(proposal.ContractVersions, StringComparer.Ordinal),
            Requires = proposal.Requires.Select(id => id.Value).ToList(),
            Operations = proposal.Operations.Select(operation => new DraftOperation
            {
                OperationId = operation.OperationId.Value,
                Kind = operation.Kind,
                Target = operation.Target?.Value,
                EntityId = operation.EntityId?.Value,
                Placement = operation.Placement is { } placement
                    ? new DraftPlacement(placement.After?.Value, placement.Before?.Value)
                    : null,
                DependsOn = operation.DependsOn.Select(dependency => dependency.OperationId.Value).ToList(),
                After = operation.After is { } after
                    ? after.EnumerateObject().ToDictionary(property => property.Name,
                        property => property.Value.Clone(), StringComparer.Ordinal)
                    : new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            }).ToList(),
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var database = ProjectMotifDatabase.Open(projectPath);
        new ProposalRepository(database).SaveDraft(PendingChanges.DraftName,
            JsonSerializer.Serialize(draft, options));
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

public sealed class RewriteRuleSampleFactAttribute : FactAttribute
{
    public RewriteRuleSampleFactAttribute()
    {
        var samplePath = Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES");
        if (string.IsNullOrWhiteSpace(samplePath))
        {
            Skip = "Set MOTIF_PANGLOSS_SAMPLES to PanGloss samples/data for the disposable rewrite-rule check.";
            return;
        }
        if (PanGlossExecutable.TryLocate() is null)
        {
            Skip = $"Set {PanGlossExecutable.PathVariable} to the pinned PanGloss executable for the Trial check.";
            return;
        }
        if (!File.Exists(Path.Combine(samplePath, "mbugwe.fwdata")) ||
            !File.Exists(Path.Combine(samplePath, "mbugwe-words.txt")) ||
            !Directory.Exists(Path.Combine(samplePath, "WritingSystemStore")))
            Skip = "The configured PanGloss sample directory lacks the Mbugwe project, word list, or writing systems.";
    }
}
