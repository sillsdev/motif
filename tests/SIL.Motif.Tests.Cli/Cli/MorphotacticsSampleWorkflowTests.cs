using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Jobs;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
[Trait("MotifTestLevel", "System")]
public sealed class MorphotacticsSampleWorkflowTests(ITestOutputHelper output)
{
    private const string ProductVersion = "0.1.0";

    [MorphotacticsSampleFact]
    public async Task SlotOptionalityAndTemplateOrderTrialOnACopyAndApplyReadBack()
    {
        var samples = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var parser = PanGlossExecutable.TryLocate()!;
        var root = Path.Combine(Path.GetTempPath(), "motif-indonesian-morphotactics-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "project");
        var workerRoot = Path.Combine(root, "worker");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(workerRoot);
        CopyTree(Path.Combine(samples, "WritingSystemStore"), Path.Combine(projectRoot, "WritingSystemStore"));
        var projectPath = Path.Combine(projectRoot, "mbugwe.fwdata");
        File.Copy(Path.Combine(samples, "mbugwe.fwdata"), projectPath);
        var wordsPath = Path.Combine(root, "trial-words.txt");
        var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var runner = new SIL.Motif.Commands.JobRunnerLaunchOptions(workerRoot, parser)
        {
            WorkerExecutable = BuildOutput.Worker,
            OwnerNamespace = "motif-indonesian-" + Guid.NewGuid().ToString("N"),
            IdleTimeout = TimeSpan.FromSeconds(1),
        };

        try
        {
            var (slotId, expectedOptional, templateId, expectedPrefix, prefix, expectedSuffix, suffix, words) =
                SelectEditsAndWords(projectPath);
            File.WriteAllLines(wordsPath, words);

            var baseline = await RunAsync(runner, preferencePath,
                "baseline", "capture", projectPath, "--json");
            Assert.True(baseline.ExitCode == 0, baseline.Error + Environment.NewLine + baseline.Output);
            var before = await RunAsync(runner, preferencePath,
                "assess", projectPath, "--words", wordsPath, "--json");
            Assert.True(before.ExitCode == 0, before.Error + Environment.NewLine + before.Output);
            var beforeResponse = ProjectionJson.Deserialize<AssessCommandResponse>(before.Output)!;
            var beforeId = beforeResponse.Measurements.Single(item => item.Kind == "ParseTime").AssessmentId;

            var created = ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion,
                "morphotactics-sample", "Repair one slot and template order"));
            Assert.True(created.Succeeded, created.Refusal?.Message);
            var commented = ProposalCommands.Comment(new CommentRequest(projectPath, ProductVersion,
                "morphotactics-sample", "The copied sample provides a bounded parser comparison."));
            Assert.True(commented.Succeeded, commented.Refusal?.Message);

            var slotIntent = JsonSerializer.Serialize(new
            {
                target = slotId.Value,
                expectedOptional,
                optional = !expectedOptional,
            });
            var slotEdit = await RunAsync(runner, preferencePath, "compose-edit-affix-slot", "--project", projectPath,
                "--draft", "morphotactics-sample", "--intent", slotIntent);
            Assert.True(slotEdit.ExitCode == 0, slotEdit.Error + Environment.NewLine + slotEdit.Output);

            var templateIntent = JsonSerializer.Serialize(new
            {
                target = templateId.Value,
                expectedPrefixSlots = expectedPrefix,
                prefixSlots = prefix,
                expectedSuffixSlots = expectedSuffix,
                suffixSlots = suffix,
            });
            var templateEdit = await RunAsync(runner, preferencePath, "compose-edit-affix-template", "--project",
                projectPath, "--draft", "morphotactics-sample", "--intent", templateIntent);
            Assert.True(templateEdit.ExitCode == 0, templateEdit.Error + Environment.NewLine + templateEdit.Output);

            var finalizedDraft = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion,
                "morphotactics-sample"));
            Assert.True(finalizedDraft.Succeeded, finalizedDraft.Refusal?.Message);
            var proposalId = finalizedDraft.Value!.ProposalId;

            var dryRun = await RunAsync(runner, preferencePath, "dry-run", "--project", projectPath,
                proposalId, "--wait", "--json");
            Assert.True(dryRun.ExitCode == 0, dryRun.Error + Environment.NewLine + dryRun.Output);

            var trial = await RunAsync(runner, preferencePath, "trial", "--project", projectPath, proposalId,
                "--words", wordsPath, "--wait", "--wait-timeout-ms", "55000", "--json");
            Assert.True(trial.ExitCode == 0, trial.Error + Environment.NewLine + trial.Output);
            var trialStatus = ProjectionJson.Deserialize<JobStatusResponse>(trial.Output)!;
            Assert.Equal(JobStatus.Completed, trialStatus.Status);

            using (var database = ProjectMotifDatabase.Open(projectPath))
            {
                var job = new JobRepository(database).Get(trialStatus.JobId)!;
                var trialResult = JsonDocument.Parse(job.ResultJson!).RootElement;
                var repository = new AssessmentRepository(database);
                var afterId = trialResult.GetProperty("assessmentIds").EnumerateArray()
                    .Select(value => value.GetString()!)
                    .Single(id => repository.Get(id).Kind == AssessmentKind.ParseTime.ToStoredKind());
                var beforeRecord = repository.Get(beforeId);
                var afterRecord = repository.Get(afterId);
                Assert.NotEqual(beforeRecord.GrammarSourceSha256, afterRecord.GrammarSourceSha256);
                var changes = ReadChanges(beforeRecord.Words!, afterRecord.Words!);
                output.WriteLine($"PanGloss {Path.GetFileName(parser)}: {words.Length} bounded Trial words; " +
                    $"{changes.Count} words changed parser readings.");
                foreach (var change in changes)
                    output.WriteLine($"{change.Word}: -[{string.Join("; ", change.Before)}] +[{string.Join("; ", change.After)}]");
            }

            var applied = await RunAsync(runner, preferencePath, "apply", proposalId, "--project", projectPath,
                "--user", "Motif sample verification", "--force", "--json");
            Assert.True(applied.ExitCode == 0, applied.Error + Environment.NewLine + applied.Output);
            using var reopened = new FwDataProjectLoader().LoadCache(projectPath);
            var objects = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
            var slot = (IMoInflAffixSlot)objects.GetObject(slotId.ToGuid());
            var template = (IMoInflAffixTemplate)objects.GetObject(templateId.ToGuid());
            Assert.Equal(!expectedOptional, slot.Optional);
            Assert.Equal(prefix.Select(ParseId), template.PrefixSlotsRS.Select(item => item.Guid));
            Assert.Equal(suffix.Select(ParseId), template.SuffixSlotsRS.Select(item => item.Guid));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [MorphotacticsSampleFact]
    public async Task NewOwnedSlotAndAffixAssignmentTrialOnACopyAndApplyReadBack()
    {
        var samples = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var parser = PanGlossExecutable.TryLocate()!;
        var sourceProject = Path.Combine(samples, "mbugwe.fwdata");
        var sourceWritingSystems = Path.Combine(samples, "WritingSystemStore");
        var sourceProjectDigest = FileDigest(sourceProject);
        var sourceWritingSystemsDigest = DirectoryDigest(sourceWritingSystems);
        var root = Path.Combine(Path.GetTempPath(), "motif-mbugwe-author-affix-slot-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "project");
        var workerRoot = Path.Combine(root, "worker");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(workerRoot);
        CopyTree(sourceWritingSystems, Path.Combine(projectRoot, "WritingSystemStore"));
        var sharedSettings = Path.Combine(samples, "SharedSettings");
        if (Directory.Exists(sharedSettings)) CopyTree(sharedSettings, Path.Combine(projectRoot, "SharedSettings"));
        var projectPath = Path.Combine(projectRoot, "mbugwe.fwdata");
        File.Copy(sourceProject, projectPath);
        var wordsPath = Path.Combine(root, "trial-words.txt");
        var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var runner = new SIL.Motif.Commands.JobRunnerLaunchOptions(workerRoot, parser)
        {
            WorkerExecutable = BuildOutput.Worker,
            OwnerNamespace = "motif-mbugwe-slot-" + Guid.NewGuid().ToString("N"),
            IdleTimeout = TimeSpan.FromSeconds(1),
        };

        try
        {
            CanonicalId categoryId;
            CanonicalId msaId;
            string writingSystem;
            using (var cache = new FwDataProjectLoader().LoadCache(projectPath))
            {
                var selectedMsa = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
                    .OfType<IMoInflAffMsa>()
                    .Where(candidate => candidate.PartOfSpeechRA is not null && candidate.Owner is ILexEntry)
                    .OrderBy(candidate => candidate.Guid.ToString("N"), StringComparer.Ordinal)
                    .FirstOrDefault() ?? throw new InvalidOperationException(
                    "The Mbugwe sample has no existing inflectional affix MSA to assign.");
                categoryId = CanonicalId.FromGuid(selectedMsa.PartOfSpeechRA!.Guid);
                msaId = CanonicalId.FromGuid(selectedMsa.Guid);
                writingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
            }

            var words = File.ReadLines(Path.Combine(samples, "mbugwe-words.txt"))
                .Where(word => !string.IsNullOrWhiteSpace(word) && !word.Contains(' '))
                .Distinct(StringComparer.Ordinal).Take(24).ToArray();
            Assert.NotEmpty(words);
            File.WriteAllLines(wordsPath, words);

            var baseline = await RunAsync(runner, preferencePath,
                "baseline", "capture", projectPath, "--json");
            Assert.True(baseline.ExitCode == 0, baseline.Error + Environment.NewLine + baseline.Output);
            var before = await RunAsync(runner, preferencePath,
                "assess", projectPath, "--words", wordsPath, "--json");
            Assert.True(before.ExitCode == 0, before.Error + Environment.NewLine + before.Output);
            var beforeResponse = ProjectionJson.Deserialize<AssessCommandResponse>(before.Output)!;
            var beforeId = beforeResponse.Measurements.Single(item => item.Kind == "ParseTime").AssessmentId;

            const string draft = "author-affix-slot";
            var created = ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion,
                draft, "Add an owned inflectional slot"));
            Assert.True(created.Succeeded, created.Refusal?.Message);
            var intent = JsonSerializer.Serialize(new
            {
                category = categoryId.Value,
                name = "motif author slot",
                ws = writingSystem,
                optional = false,
                assignments = new[] { msaId.Value },
            });
            var composed = await RunAsync(runner, preferencePath, "compose-author-affix-slot",
                "--project", projectPath, "--draft", draft, "--intent", intent);
            Assert.True(composed.ExitCode == 0, composed.Error + Environment.NewLine + composed.Output);
            Assert.Contains("Draft now has 4 operation(s).", composed.Output, StringComparison.Ordinal);
            Assert.True(ProposalCommands.Label(new LabelRequest(projectPath, ProductVersion,
                draft, "Add an owned inflectional slot")).Succeeded);
            Assert.True(ProposalCommands.Comment(new CommentRequest(projectPath, ProductVersion,
                draft, "The existing affix belongs to this category and needs a dedicated slot.")).Succeeded);

            var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion, draft));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
            var proposalId = finalized.Value!.ProposalId;
            var proposal = ProposalCommands.Show(new ShowProposalRequest(projectPath, ProductVersion, proposalId));
            Assert.True(proposal.Succeeded, proposal.Refusal?.Message);
            var slotId = CanonicalId.Parse(proposal.Value!.Operations
                .Single(operation => operation.EntityId is not null).EntityId!);
            var dryRun = await RunAsync(runner, preferencePath, "dry-run", "--project", projectPath,
                proposalId, "--wait", "--json");
            Assert.True(dryRun.ExitCode == 0, dryRun.Error + Environment.NewLine + dryRun.Output);

            var trial = await RunAsync(runner, preferencePath, "trial", "--project", projectPath,
                proposalId, "--words", wordsPath, "--wait", "--wait-timeout-ms", "180000", "--json");
            Assert.True(trial.ExitCode == 0, trial.Error + Environment.NewLine + trial.Output);
            var trialStatus = ProjectionJson.Deserialize<JobStatusResponse>(trial.Output)!;
            Assert.Equal(JobStatus.Completed, trialStatus.Status);
            using (var database = ProjectMotifDatabase.Open(projectPath))
            {
                var job = new JobRepository(database).Get(trialStatus.JobId)!;
                var trialResult = JsonDocument.Parse(job.ResultJson!).RootElement;
                var repository = new AssessmentRepository(database);
                var afterId = trialResult.GetProperty("assessmentIds").EnumerateArray()
                    .Select(value => value.GetString()!)
                    .Single(id => repository.Get(id).Kind == AssessmentKind.ParseTime.ToStoredKind());
                var changes = ReadChanges(repository.Get(beforeId).Words!, repository.Get(afterId).Words!);
                output.WriteLine($"PanGloss {Path.GetFileName(parser)}: {words.Length} bounded Trial words; " +
                    $"{changes.Count} words changed parser readings.");
                if (changes.Count == 0) output.WriteLine("No parser readings changed for the Trial words.");
                foreach (var change in changes)
                    output.WriteLine($"{change.Word}: -[{string.Join("; ", change.Before)}] +[{string.Join("; ", change.After)}]");
            }

            var applied = await RunAsync(runner, preferencePath, "apply", proposalId,
                "--project", projectPath, "--user", "Motif sample verification", "--force", "--json");
            Assert.True(applied.ExitCode == 0, applied.Error + Environment.NewLine + applied.Output);
            using var reopened = new FwDataProjectLoader().LoadCache(projectPath);
            var objects = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
            var slot = (IMoInflAffixSlot)objects.GetObject(slotId.ToGuid());
            var msa = (IMoInflAffMsa)objects.GetObject(msaId.ToGuid());
            Assert.Equal(categoryId.ToGuid(), slot.Owner?.Guid);
            Assert.Equal("motif author slot", slot.Name.get_String(reopened.DefaultAnalWs)?.Text);
            Assert.False(slot.Optional);
            Assert.Contains(slot, msa.SlotsRC);
            Assert.Equal(sourceProjectDigest, FileDigest(sourceProject));
            Assert.Equal(sourceWritingSystemsDigest, DirectoryDigest(sourceWritingSystems));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [MorphotacticsSampleFact]
    public async Task NewAlternativeTemplateTrialOnMbugweCopyApplySaveAndReadBack()
    {
        var samples = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var parser = PanGlossExecutable.TryLocate()!;
        var sourceProject = Path.Combine(samples, "mbugwe.fwdata");
        var sourceWritingSystems = Path.Combine(samples, "WritingSystemStore");
        var sourceProjectDigest = FileDigest(sourceProject);
        var sourceWritingSystemsDigest = DirectoryDigest(sourceWritingSystems);
        var root = Path.Combine(Path.GetTempPath(), "motif-mbugwe-author-affix-template-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "project");
        var workerRoot = Path.Combine(root, "worker");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(workerRoot);
        CopyTree(sourceWritingSystems, Path.Combine(projectRoot, "WritingSystemStore"));
        var sharedSettings = Path.Combine(samples, "SharedSettings");
        if (Directory.Exists(sharedSettings)) CopyTree(sharedSettings, Path.Combine(projectRoot, "SharedSettings"));
        var projectPath = Path.Combine(projectRoot, "mbugwe.fwdata");
        File.Copy(sourceProject, projectPath);
        var wordsPath = Path.Combine(root, "trial-words.txt");
        var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var runner = new SIL.Motif.Commands.JobRunnerLaunchOptions(workerRoot, parser)
        {
            WorkerExecutable = BuildOutput.Worker,
            OwnerNamespace = "motif-mbugwe-template-" + Guid.NewGuid().ToString("N"),
            IdleTimeout = TimeSpan.FromSeconds(1),
        };

        try
        {
            CanonicalId categoryId;
            CanonicalId[] prefixSlots;
            CanonicalId[] suffixSlots;
            Guid[] originalTemplateOrder;
            bool final;
            string writingSystem;
            using (var cache = new FwDataProjectLoader().LoadCache(projectPath))
            {
                var selected = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
                    .OfType<IMoInflAffixTemplate>()
                    .Where(template => template.Owner is IPartOfSpeech &&
                        template.PrefixSlotsRS.Count + template.SuffixSlotsRS.Count >= 2)
                    .OrderByDescending(template => template.PrefixSlotsRS.Count + template.SuffixSlotsRS.Count)
                    .ThenBy(template => template.Guid.ToString("N"), StringComparer.Ordinal)
                    .FirstOrDefault() ?? throw new InvalidOperationException(
                        "The Mbugwe sample has no category template with two or more slots.");
                var category = (IPartOfSpeech)selected.Owner!;
                categoryId = CanonicalId.FromGuid(category.Guid);
                prefixSlots = selected.PrefixSlotsRS.Count > 0
                    ? [CanonicalId.FromGuid(selected.PrefixSlotsRS[0].Guid)]
                    : [];
                suffixSlots = selected.PrefixSlotsRS.Count > 0
                    ? []
                    : [CanonicalId.FromGuid(selected.SuffixSlotsRS[0].Guid)];
                originalTemplateOrder = category.AffixTemplatesOS.Select(template => template.Guid).ToArray();
                final = selected.Final;
                writingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
            }

            var words = File.ReadLines(Path.Combine(samples, "mbugwe-words.txt"))
                .Where(word => !string.IsNullOrWhiteSpace(word) && !word.Contains(' '))
                .Distinct(StringComparer.Ordinal).Take(16).ToArray();
            Assert.NotEmpty(words);
            File.WriteAllLines(wordsPath, words);
            var baseline = await RunAsync(runner, preferencePath,
                "baseline", "capture", projectPath, "--json");
            Assert.True(baseline.ExitCode == 0, baseline.Error + Environment.NewLine + baseline.Output);
            var before = await RunAsync(runner, preferencePath,
                "assess", projectPath, "--words", wordsPath, "--json");
            Assert.True(before.ExitCode == 0, before.Error + Environment.NewLine + before.Output);
            var beforeResponse = ProjectionJson.Deserialize<AssessCommandResponse>(before.Output)!;
            var beforeId = beforeResponse.Measurements.Single(item => item.Kind == "ParseTime").AssessmentId;

            const string draft = "author-alternative-template";
            var createdDraft = ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion,
                draft, "Add an alternative slot obligation"));
            Assert.True(createdDraft.Succeeded, createdDraft.Refusal?.Message);
            Assert.True(ProposalCommands.Label(new LabelRequest(projectPath, ProductVersion,
                draft, "Add an alternative affix template")).Succeeded);
            Assert.True(ProposalCommands.Comment(new CommentRequest(projectPath, ProductVersion,
                draft, "The selected existing slot is one confirmed branch of the category's inflectional pattern.")).Succeeded);
            var intent = JsonSerializer.Serialize(new
            {
                category = categoryId.Value,
                name = "Motif alternative template",
                ws = writingSystem,
                prefixSlots = prefixSlots.Select(id => id.Value).ToArray(),
                suffixSlots = suffixSlots.Select(id => id.Value).ToArray(),
                final,
            });
            var composed = await RunAsync(runner, preferencePath, "compose-author-affix-template",
                "--project", projectPath, "--draft", draft, "--intent", intent);
            Assert.True(composed.ExitCode == 0, composed.Error + Environment.NewLine + composed.Output);

            var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion, draft));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
            var proposalId = finalized.Value!.ProposalId;
            var proposal = ProposalCommands.Show(new ShowProposalRequest(projectPath, ProductVersion, proposalId));
            Assert.True(proposal.Succeeded, proposal.Refusal?.Message);
            var templateId = CanonicalId.Parse(proposal.Value!.Operations
                .Single(operation => operation.Kind == PartOfSpeechAffixTemplatesOperationKinds.Create)
                .EntityId ?? throw new InvalidOperationException("The template create has no entity id."));

            var dryRun = await RunAsync(runner, preferencePath, "dry-run", "--project", projectPath,
                proposalId, "--wait", "--json");
            Assert.True(dryRun.ExitCode == 0, dryRun.Error + Environment.NewLine + dryRun.Output);
            var trial = await RunAsync(runner, preferencePath, "trial", "--project", projectPath,
                proposalId, "--words", wordsPath, "--wait", "--wait-timeout-ms", "180000", "--json");
            Assert.True(trial.ExitCode == 0, trial.Error + Environment.NewLine + trial.Output);
            var trialStatus = ProjectionJson.Deserialize<JobStatusResponse>(trial.Output)!;
            Assert.Equal(JobStatus.Completed, trialStatus.Status);
            using (var database = ProjectMotifDatabase.Open(projectPath))
            {
                var job = new JobRepository(database).Get(trialStatus.JobId)!;
                var result = JsonDocument.Parse(job.ResultJson!).RootElement;
                var repository = new AssessmentRepository(database);
                var afterId = result.GetProperty("assessmentIds").EnumerateArray()
                    .Select(value => value.GetString()!)
                    .Single(id => repository.Get(id).Kind == AssessmentKind.ParseTime.ToStoredKind());
                var changes = ReadChanges(repository.Get(beforeId).Words!, repository.Get(afterId).Words!);
                output.WriteLine($"PanGloss {Path.GetFileName(parser)}: {words.Length} bounded Trial words; " +
                    $"{changes.Count} words changed parser readings.");
                if (changes.Count == 0) output.WriteLine("No parser readings changed for the Trial words.");
                foreach (var change in changes)
                    output.WriteLine($"{change.Word}: -[{string.Join("; ", change.Before)}] +[{string.Join("; ", change.After)}]");
            }

            var applied = await RunAsync(runner, preferencePath, "apply", proposalId,
                "--project", projectPath, "--user", "Motif sample verification", "--force", "--json");
            Assert.True(applied.ExitCode == 0, applied.Error + Environment.NewLine + applied.Output);
            using var reopened = new FwDataProjectLoader().LoadCache(projectPath);
            var objects = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
            var categoryAfter = (IPartOfSpeech)objects.GetObject(categoryId.ToGuid());
            var createdTemplate = (IMoInflAffixTemplate)objects.GetObject(templateId.ToGuid());
            Assert.Equal(originalTemplateOrder.Append(templateId.ToGuid()),
                categoryAfter.AffixTemplatesOS.Select(template => template.Guid));
            Assert.Equal(prefixSlots.Select(id => id.ToGuid()), createdTemplate.PrefixSlotsRS.Select(slot => slot.Guid));
            Assert.Equal(suffixSlots.Select(id => id.ToGuid()), createdTemplate.SuffixSlotsRS.Select(slot => slot.Guid));
            Assert.Equal("Motif alternative template", createdTemplate.Name.get_String(reopened.DefaultAnalWs)?.Text);
            Assert.Equal(final, createdTemplate.Final);
            Assert.False(createdTemplate.Disabled);
            Assert.Equal(sourceProjectDigest, FileDigest(sourceProject));
            Assert.Equal(sourceWritingSystemsDigest, DirectoryDigest(sourceWritingSystems));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static (CanonicalId SlotId, bool ExpectedOptional, CanonicalId TemplateId,
        string[] ExpectedPrefix, string[] Prefix, string[] ExpectedSuffix, string[] Suffix, string[] Words)
        SelectEditsAndWords(string projectPath)
    {
        using var cache = new FwDataProjectLoader().LoadCache(projectPath);
        var objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var templates = objects.AllInstances().OfType<IMoInflAffixTemplate>().ToArray();
        var orderedTemplate = templates
            .Select(template => new
            {
                Template = template,
                Prefix = template.PrefixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid).Value).ToArray(),
                Suffix = template.SuffixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid).Value).ToArray(),
            })
            .Where(item => item.Prefix.Length + item.Suffix.Length >= 2)
            .OrderByDescending(item => item.Prefix.Length + item.Suffix.Length)
            .First();
        var expectedPrefix = orderedTemplate.Prefix;
        var expectedSuffix = orderedTemplate.Suffix;
        var prefix = expectedPrefix.ToArray();
        var suffix = expectedSuffix.ToArray();
        if (prefix.Length >= 2) Array.Reverse(prefix);
        else Array.Reverse(suffix);

        var entries = objects.AllInstances().OfType<ILexEntry>().ToArray();
        var optionalTemplate = templates.FirstOrDefault(template =>
            template.PrefixSlotsRS.Count + template.SuffixSlotsRS.Count == 1 &&
            (template.PrefixSlotsRS.FirstOrDefault() ?? template.SuffixSlotsRS.First()).Optional == false &&
            template.Owner is IPartOfSpeech pos && entries.Any(entry => entry.MorphoSyntaxAnalysesOC
                .OfType<IMoStemMsa>().Any(msa => msa.PartOfSpeechRA == pos)));
        var slot = optionalTemplate is null
            ? orderedTemplate.Template.PrefixSlotsRS.FirstOrDefault() ?? orderedTemplate.Template.SuffixSlotsRS.First()
            : optionalTemplate.PrefixSlotsRS.FirstOrDefault() ?? optionalTemplate.SuffixSlotsRS.First();
        var category = (IPartOfSpeech)(optionalTemplate?.Owner ?? orderedTemplate.Template.Owner);
        var words = entries
            .Where(entry => entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>()
                .Any(msa => msa.PartOfSpeechRA == category))
            .Select(entry => entry.LexemeFormOA?.Form.VernacularDefaultWritingSystem?.Text)
            .Where(form => !string.IsNullOrWhiteSpace(form) && !form.Contains(' '))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(form => form, StringComparer.Ordinal)
            .Take(24)
            .ToArray()!;
        var sampleWords = File.ReadLines(Path.Combine(
                Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!, "mbugwe-words.txt"))
            .Where(word => !string.IsNullOrWhiteSpace(word) && !word.Contains(' '))
            .Distinct(StringComparer.Ordinal)
            // 32 sample words take 38 s on Linux and 63 s on Windows to parse, past the CLI's 60-second budget.
            .Take(16)
            .ToArray();
        if (sampleWords.Length > 0) words = sampleWords;
        else if (words.Length == 0)
            words = objects.AllInstances().OfType<IWfiWordform>()
                .Select(wordform => wordform.Form.VernacularDefaultWritingSystem?.Text)
                .Where(form => !string.IsNullOrWhiteSpace(form) && !form.Contains(' '))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(form => form, StringComparer.Ordinal)
                .Take(24)
                .ToArray()!;
        if (words.Length == 0) throw new InvalidOperationException("The sample has no single-word forms to Trial.");

        return (CanonicalId.FromGuid(slot.Guid), slot.Optional,
            CanonicalId.FromGuid(orderedTemplate.Template.Guid), expectedPrefix, prefix,
            expectedSuffix, suffix, words);
    }

    private static List<(string Word, string[] Before, string[] After)> ReadChanges(
        IReadOnlyList<AssessedWord> before, IReadOnlyList<AssessedWord> after)
    {
        var afterByWord = after.ToDictionary(word => word.Word, StringComparer.Ordinal);
        return before.Where(word => afterByWord.ContainsKey(word.Word))
            .Select(word =>
            {
                var beforeReadings = word.Analyses.Select(Reading).Order(StringComparer.Ordinal).ToArray();
                var afterReadings = afterByWord[word.Word].Analyses.Select(Reading)
                    .Order(StringComparer.Ordinal).ToArray();
                return (word.Word, beforeReadings, afterReadings);
            })
            .Where(change => !change.beforeReadings.SequenceEqual(change.afterReadings, StringComparer.Ordinal))
            .Select(change => (change.Word, Difference(change.beforeReadings, change.afterReadings),
                Difference(change.afterReadings, change.beforeReadings)))
            .Select(change => (change.Word, change.Item2, change.Item3))
            .ToList();
    }

    private static string Reading(ParsedAnalysis analysis) =>
        $"{analysis.CategoryGuid ?? "(no category)"}: {string.Join(" → ", analysis.MorphemeGuids)}";

    private static string[] Difference(IEnumerable<string> left, IEnumerable<string> right)
    {
        var rightSet = right.ToHashSet(StringComparer.Ordinal);
        return left.Where(item => !rightSet.Contains(item)).ToArray();
    }

    private static Guid ParseId(string id) => CanonicalId.Parse(id).ToGuid();

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(
        SIL.Motif.Commands.JobRunnerLaunchOptions runner, string preferencePath, params string[] arguments)
    {
        var start = CliProcess.Start(runner, arguments);
        start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath;
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        return CliProcess.RunAsync(start);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string FileDigest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string DirectoryDigest(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Directory.EnumerateFiles(path, "*.ldml").Order(StringComparer.Ordinal)
            .Select(file => Path.GetFileName(file) + ":" + FileDigest(file))))));
}

public sealed class MorphotacticsSampleFactAttribute : FactAttribute
{
    public MorphotacticsSampleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")))
            Skip = "Set MOTIF_PANGLOSS_SAMPLES to PanGloss samples/data for a disposable grammar check.";
        else if (PanGlossExecutable.TryLocate() is null)
            Skip = $"Set {PanGlossExecutable.PathVariable} to the pinned PanGloss executable.";
    }
}
