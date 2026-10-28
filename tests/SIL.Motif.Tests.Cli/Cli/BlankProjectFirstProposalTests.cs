using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "System")]
public sealed class BlankProjectFirstProposalTests(ITestOutputHelper output)
{
    private const string ProductVersion = "0.1.0";

    /// <summary>
    /// A project <c>motif project create</c> made takes its first phonology through a Proposal: it can be Assessed
    /// before any phoneme exists, and the phoneme Proposal composes, Dry Runs and reaches the parser in a Trial.
    /// </summary>
    [RealParserFact]
    public async Task CreatedProjectIsAssessedThenTrialsItsFirstPhonemeProposal()
    {
        var parser = PanGlossExecutable.TryLocate()!;
        var root = Path.Combine(Path.GetTempPath(), "motif-blank-first-proposal-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "project");
        var workerRoot = Path.Combine(root, "worker");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(workerRoot);
        var wordsPath = Path.Combine(root, "trial-words.txt");
        File.WriteAllLines(wordsPath, ["kwa", "heri", "kiti", "kwa'heri"]);
        var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var runner = new JobRunnerLaunchOptions(workerRoot, parser)
        {
            WorkerExecutable = BuildOutput.Worker,
            OwnerNamespace = "motif-blank-first-" + Guid.NewGuid().ToString("N"),
            IdleTimeout = TimeSpan.FromSeconds(1),
        };

        try
        {
            var created = await RunAsync(runner, preferencePath, "project", "create", "--into", projectRoot,
                "--name", "Kiswahili", "--vernacular", "swh", "--word-forming", "'", "--json");
            Report("project create", created);
            Assert.True(created.ExitCode == 0, created.Error);
            var projectPath = JsonDocument.Parse(created.Output).RootElement.GetProperty("fwDataPath").GetString()!;

            var baseline = await RunAsync(runner, preferencePath,
                "baseline", "capture", "--project", projectPath, "--json");
            Report("baseline capture", baseline);
            Assert.True(baseline.ExitCode == 0, baseline.Error + Environment.NewLine + baseline.Output);

            var blank = await RunAsync(runner, preferencePath, "assess", projectPath, "--words", wordsPath, "--json");
            Report("assess before any phoneme", blank);
            Assert.True(blank.ExitCode == 0, blank.Error + Environment.NewLine + blank.Output);

            const string draft = "first-phonemes";
            var newDraft = ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion, draft, "First phonemes"));
            Assert.True(newDraft.Succeeded, newDraft.Refusal?.Message);
            foreach (var letter in new[] { "k", "w", "a", "h", "e", "r", "i", "t" })
            {
                var intent = JsonSerializer.Serialize(new { name = letter, representations = new[] { letter } });
                string[] arguments = ["compose-author-phoneme", "--project", projectPath,
                    "--draft", draft, "--intent", intent];
                var composed = letter == "k"
                    ? await RunAsync(runner, preferencePath, arguments)
                    : RunInProcess(runner, preferencePath, arguments);
                Report("compose " + letter, composed);
                Assert.True(composed.ExitCode == 0, composed.Error + Environment.NewLine + composed.Output);
            }

            Assert.True(ProposalCommands.Label(new LabelRequest(projectPath, ProductVersion, draft,
                "First phonemes")).Succeeded);
            Assert.True(DraftRationale.Write(projectPath, ProductVersion, draft,
                "A blank project's first sound system.").Succeeded);
            var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion, draft));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
            var proposalId = finalized.Value!.ProposalId;

            var dryRun = await RunAsync(runner, preferencePath, "dry-run", "--project", projectPath,
                proposalId, "--wait", "--json");
            Report("dry-run", dryRun);
            Assert.True(dryRun.ExitCode == 0, dryRun.Error + Environment.NewLine + dryRun.Output);

            var trial = await RunAsync(runner, preferencePath, "trial", "--project", projectPath,
                proposalId, "--words", wordsPath, "--wait", "--wait-timeout-ms", "180000", "--json");
            Report("trial", trial);
            Assert.True(trial.ExitCode == 0, trial.Error + Environment.NewLine + trial.Output);
            var trialStatus = ProjectionJson.Deserialize<JobStatusResponse>(trial.Output)!;
            Assert.Equal(JobStatus.Completed, trialStatus.Status);
            Assert.Equal(4, trialStatus.TrialProgress?.Completed);
            Assert.Equal(4, trialStatus.TrialProgress?.Total);

            using (var database = ProjectMotifDatabase.Open(projectPath))
            {
                using var completion = JsonDocument.Parse(new JobRepository(database).Get(trialStatus.JobId)!.ResultJson!);
                var assessmentIds = completion.RootElement.GetProperty("assessmentIds")
                    .EnumerateArray().Select(value => value.GetString()!).ToArray();
                Assert.NotEmpty(assessmentIds);
                Assert.Equal(assessmentIds.Length, assessmentIds.Distinct(StringComparer.Ordinal).Count());

                var assessments = new AssessmentRepository(database);
                var receipts = assessmentIds.Select(assessments.Get).ToArray();
                Assert.All(receipts, receipt => Assert.Equal(proposalId, receipt.ProposalId?.Value));
            }

            using var reopened = new FwDataProjectLoader().LoadScratchCache(projectPath);
            Assert.Empty(Assert.Single(reopened.LangProject.PhonologicalDataOA.PhonemeSetsOS).PhonemesOC);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Report(string step, (int ExitCode, string Output, string Error) result) =>
        output.WriteLine($"== {step}: exit {result.ExitCode}\n{result.Output}\n{result.Error}");

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(
        JobRunnerLaunchOptions runner, string preferencePath, params string[] arguments)
    {
        var start = CliProcess.Start(runner, arguments);
        start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath;
        start.Environment[SIL.Motif.Commands.Catalog.CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        return CliProcess.RunAsync(start);
    }

    private static (int ExitCode, string Output, string Error) RunInProcess(
        JobRunnerLaunchOptions runner, string preferencePath, params string[] arguments)
    {
        var start = CliProcess.Start(runner, arguments);
        start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath;
        start.Environment[SIL.Motif.Commands.Catalog.CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        var result = CliInProcess.Run(start, arguments);
        return (result.ExitCode, result.Output, result.Error);
    }
}
