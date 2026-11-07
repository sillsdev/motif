using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TrialReviewArgvTests
{
    [Fact]
    public async Task NoTrialReturnsTheTypedResultAndAnExplicitStartTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-cli-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "project.fwdata");
            File.WriteAllText(project, "");
            var start = new ProcessStartInfo(BuildOutput.Cli)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "proposal", "trial", "--project", project, "proposal/one", "--json" })
                start.ArgumentList.Add(argument);
            start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(process.ExitCode == 0, await error);
            using var json = JsonDocument.Parse(await output);
            Assert.Equal("no-trial", json.RootElement.GetProperty("state").GetString());
            Assert.Equal("motif_trial", json.RootElement.GetProperty("next")[0].GetProperty("tool").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FullPageKeepsTheSummaryAndReturnsTheCapturedCase()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-cli-review-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "project.fwdata");
            File.WriteAllText(project, "");
            var proposal = CanonicalId.Mint("proposal/").Value;
            var reportId = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, locator) =>
            {
                var key = ProjectWorkspaceKey.Compute(locator);
                using (var connection = database.OpenConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO Proposals (ProposalId,CurrentIntentDigest,Status) VALUES ($id,'intent','proposed');";
                    command.Parameters.AddWithValue("$id", proposal);
                    command.ExecuteNonQuery();
                }
                var inputs = new FrozenReviewInputs(key, proposal, "scope", "manifest", null, [], [],
                    [new ReviewPlannedCase("case", "word-key", "word", "ws", null, [], []),
                        new ReviewPlannedCase("case-two", "word-key-two", "word-two", "ws", null, [], [])]);
                var digest = "sha256:" + new string('a', 64);
                var baseline = new BaselineToken(key, digest, "v1", "2026-01-01T00:00:00Z", digest);
                var reference = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
                var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = proposal, operations = Array.Empty<object>() });
                var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference);
                const string job = "job/cli-review";
                new JobRepository(database).RecordInterrupted(job, key, "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
                    "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
                TrialReviewEvidence.Save(database, new TrialReviewLedger(job, reference, [], [], []));
                var id = new TrialReviewPublisher(database).PublishAsync(new ReviewPublishRequest(job)).GetAwaiter().GetResult();
                return CommandOutcome<string>.Success(id.Value);
            });
            Assert.True(reportId.Succeeded, reportId.Refusal?.Message);
            async Task<JsonDocument> Read(params string[] options)
            {
                var start = new ProcessStartInfo(BuildOutput.Cli)
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "proposal", "trial", "--project", project, proposal, "--report", reportId.Value!, "--json" }.Concat(options))
                    start.ArgumentList.Add(arg);
                start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
                Assert.True(process.ExitCode == 0, await error);
                return JsonDocument.Parse(await output);
            }
            using var summary = await Read();
            using var full = await Read("--detail", "full");
            var first = summary.RootElement.GetProperty("report");
            var second = full.RootElement.GetProperty("report");
            Assert.Equal(first.GetProperty("summary").GetRawText(), second.GetProperty("summary").GetRawText());
            Assert.Equal("case", second.GetProperty("page").GetProperty("items")[0].GetProperty("caseKey").GetString());
            Assert.Equal(2, second.GetProperty("page").GetProperty("totalMatching").GetInt32());
            using var pageOne = await Read("--detail", "full", "--limit", "1");
            var cursor = pageOne.RootElement.GetProperty("report").GetProperty("page").GetProperty("nextCursor").GetString();
            Assert.NotNull(cursor);
            using var pageTwo = await Read("--detail", "full", "--limit", "1", "--cursor", cursor!);
            Assert.Equal("case-two", pageTwo.RootElement.GetProperty("report").GetProperty("page")
                .GetProperty("items")[0].GetProperty("caseKey").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("--job", "job/one", "--report", "report/one")]
    [InlineData("--word", "word", "--detail", "summary")]
    [InlineData("--filter", "changed", "--detail", "summary")]
    [InlineData("--word", "word", "--case", "case/one")]
    public async Task InvalidSelectorCombinationsDoNoWork(string first, string firstValue, string second, string secondValue)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "proposal", "trial", "--project", "missing.fwdata", "proposal/one",
                     first, firstValue, second, secondValue }) start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotEqual(0, process.ExitCode);
        Assert.DoesNotContain("Project file not found", await output + await error, StringComparison.Ordinal);
    }
}
