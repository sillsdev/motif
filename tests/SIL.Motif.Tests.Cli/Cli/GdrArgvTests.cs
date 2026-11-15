using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-gdr-argv-" + Guid.NewGuid().ToString("N"));

    private string ProjectWithBaseline()
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return path;
    }

    [Fact]
    public async Task ApplyOfFinalizedStaleTitleMatchesTheAppRefusalAndWritesNothing()
    {
        var path = ProjectWithBaseline();
        var shown = GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value!;
        var title = shown.TitleAlternatives.Single(item => item.WritingSystem == "en");
        var description = shown.DescriptionAlternatives.Single(item => item.WritingSystem == "en");
        TextRichStringPayload Edit(string field, RichStringAlternative before, string text) =>
            new(field, "en", [new TextRun(text, "en")], new(before.Text, before.Digest));
        var staged = ProposalCommands.StageGdrMarkdown(path, new(shown.Record.Id,
            [Edit("CmPossibility.Name", title, "mine"), Edit("CmPossibility.Description", description, "other")], null));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var version = MotifProductVersion.CurrentText;
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, version, PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            var record = SeededGdrs.Records(cache).PossibilitiesOS[0];
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => record.Name.set_String(cache.DefaultAnalWs, "outside"));
            new FwDataProjectLoader().Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);
        var proposalId = finalized.Value!.ProposalId;
        var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(path, version, proposalId));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        await using (var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(_root)))
        {
            runner.Start(path);
            await runner.WhenIdleAsync();
            var bound = JobCommands.WaitForDryRun(new WaitForDryRunRequest(path, version, proposalId,
                queued.Value!.JobId, TimeSpan.FromMinutes(2)));
            Assert.True(bound.Succeeded, bound.Refusal?.Message);
        }
        var before = File.ReadAllBytes(path);
        var app = PendingChangesWorkflow.Apply(new ApplyRequest(path, version, proposalId));
        Assert.False(app.Succeeded);
        Assert.Equal(RefusalCodes.TextFieldStale, app.Refusal!.Code);
        Assert.Contains("CmPossibility.Name", app.Refusal.Message, StringComparison.Ordinal);

        var cli = await CliProcess.RunAsync(_root, null, false, "apply", proposalId, "--project", path, "--json");

        Assert.True(cli.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.Refused), cli.Error + cli.Output);
        Assert.Empty(cli.Output);
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(cli.Error)!;
        Assert.Equal(app.Refusal.Code, failure.Code);
        Assert.Equal(app.Refusal.Message, failure.Message);
        Assert.Equal("CmPossibility.Name", failure.Detail!["field"]);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);
        var after = GdrQuery.Show(new GdrShowRequest(path, shown.Record.Id)).Value!;
        Assert.Equal("outside", after.Record.Title);
        Assert.Equal(shown.Record.Description, after.Record.Description);
    }

    [Fact]
    public async Task ListAndShowAgreeWithTheQueriesInJsonAndReadAsTextForAPerson()
    {
        var path = ProjectWithBaseline();

        var list = await CliProcess.RunAsync(_root, null, true, "gdr", "list", "--project", path, "--json");
        Assert.True(list.ExitCode == 0, list.Error);
        Assert.Equal(ProjectionJson.Serialize(GdrQuery.List(new GdrListRequest(path)).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrListResponse>(list.Output)));

        var show = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path, "GDR-1", "--json");
        Assert.True(show.ExitCode == 0, show.Error);
        Assert.Equal(ProjectionJson.Serialize(GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrShowResponse>(show.Output)));

        var text = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path, "GDR-1");
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("GDR-1  title-1", text.Output);
        Assert.Contains("xylink", text.Output);
        Assert.Contains("Link 'link' -> https://example.invalid/x", text.Output);
    }

    [Fact]
    public async Task CheckAgreesWithTheQueryInJsonAndReadsAsTextForAPerson()
    {
        var path = ProjectWithBaseline();

        var json = await CliProcess.RunAsync(_root, null, true, "gdr", "check", "--project", path, "--json");
        Assert.True(json.ExitCode == 0, json.Error);
        var wire = ProjectionJson.Deserialize<GdrCheckResponse>(json.Output)!;
        var query = GdrQuery.Check(new GdrCheckRequest(path)).Value!;
        Assert.Equal(ProjectionJson.Serialize(query with { Timing = null }), ProjectionJson.Serialize(wire with { Timing = null }));

        var text = await CliProcess.RunAsync(_root, null, true, "gdr", "check", "--project", path);
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("GDRs are in sync with the grammar.", text.Output);
        Assert.Contains("Read 4 record(s)", text.Output);
    }

    [Fact]
    public async Task ShowNeedsAnIdAndAnUnknownRecordIsRefused()
    {
        var path = ProjectWithBaseline();

        var missing = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path);
        Assert.True(missing.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), missing.Error);

        var unknown = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path, "GDR-99");
        Assert.True(unknown.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), unknown.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
