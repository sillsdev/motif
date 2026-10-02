using SIL.Motif.Commands.Queries;
using System.Diagnostics;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ParseAdmissionRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AnotherClientCannotQueueASecondParseOrTraceForTheSameProject()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var release = Path.Combine(project.ManagedRoot, "parse-release");
        var started = Path.Combine(project.ManagedRoot, "parse-started");
        project.Behave(new { startedPath = started, holdUntilPath = release });
        var other = RealCommandClient.Create(project.ManagedRoot, project.ParserPath);
        using var cancellation = new CancellationTokenSource();
        var request = new AssessRequest(project.FwDataPath,
            new SelectionRequest(false, [], ["held"], false, null), 1000);
        var first = project.Client.AssessAsync(request, new Progress<AssessmentProgress>(), cancellation.Token);
        try
        {
            var deadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
            while (!File.Exists(started))
            {
                Assert.True(Stopwatch.GetTimestamp() < deadline, "The held batch never started.");
                await Task.Delay(25);
            }
            var duplicate = await other.AssessAsync(request, new Progress<AssessmentProgress>(), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(duplicate.Succeeded);
            Assert.Equal(FailureReason.Busy, duplicate.Refusal!.Reason);
            Assert.Contains("already", duplicate.Refusal.Message);
            var trace = await other.TraceWordAsync(new WordTraceRequest(project.FwDataPath, "held"), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(trace.Succeeded);
            Assert.Equal(FailureReason.Busy, trace.Refusal!.Reason);
            var wordsPath = Path.Combine(project.ManagedRoot, "parse-words.txt");
            File.WriteAllText(wordsPath, "held\n");
            var cli = await CliProcess.RunAsync(project.ManagedRoot, project.ParserPath, true,
                "assess", project.FwDataPath, "--words", wordsPath, "--json");
            Assert.True(cli.ExitCode != 0, cli.FailureDetails);
            Assert.Contains("parse.already-running", cli.Error);
            Assert.Equal(1, project.Invocations().Count(line => line == "batch"));
        }
        finally
        {
            cancellation.Cancel();
            await first.WaitAsync(TimeSpan.FromSeconds(20));
        }
        project.Behave(new { });
        var retry = await other.AssessAsync(request, new Progress<AssessmentProgress>(), CancellationToken.None);
        Assert.True(retry.Succeeded, retry.Refusal?.Message);
    }
}
