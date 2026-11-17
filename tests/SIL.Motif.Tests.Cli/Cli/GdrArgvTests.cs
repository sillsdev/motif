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
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Ids;
using System.Text;
using System.Diagnostics;
using SIL.TextEditing.Markdown;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-gdr-argv-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "json")]
    [InlineData(false, "fw-md")]
    [InlineData(true, null)]
    [InlineData(true, "json")]
    [InlineData(true, "fw-md")]
    public async Task GdrShowLeavesMissingAndOlderStoresUnchanged(bool older, string? format)
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var store = Path.ChangeExtension(path, ".motif.db");
        if (older)
        {
            using var database = ProjectMotifDatabase.Open(path);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
            command.ExecuteNonQuery();
        }
        string[] StoreFiles() => Directory.GetFiles(Path.GetDirectoryName(store)!, Path.GetFileName(store) + "*")
            .Where(file => !file.EndsWith("-wal", StringComparison.Ordinal) && !file.EndsWith("-shm", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        var files = StoreFiles().ToDictionary(file => file, File.ReadAllBytes);
        var before = File.ReadAllBytes(path);
        var options = format switch { "json" => new[] { "--json" }, "fw-md" => ["--format", "fw-md"], _ => [] };

        var cli = await CliProcess.RunAsync(_root, null, true,
            ["guide", "show", "--project", path, "GDR-1", .. options]);

        Assert.Equal(files.Keys.Order(StringComparer.Ordinal).Select(Path.GetFileName), StoreFiles().Select(Path.GetFileName));
        foreach (var (file, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(older, File.Exists(store));
        Assert.Empty(cli.Output);
        Assert.True(cli.ExitCode == FailureEnvelope.ExitCodeFor(older ? FailureReason.Refused : FailureReason.NotFound), cli.Error);
        if (format == "json")
            Assert.Equal(older ? RefusalCodes.StoreUnsupported : RefusalCodes.GdrNoBaseline,
                ProjectionJson.Deserialize<FailureEnvelope>(cli.Error)!.Code);
    }

    private string ProjectWithBaseline(bool unicode = false, bool links = false)
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            if (links) SeededGdrs.SeedLinks(cache);
            if (unicode)
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    SeededGdrs.Records(cache).PossibilitiesOS[0].Description.set_String(cache.DefaultAnalWs, "abstract-α"));
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return path;
    }

    [Theory]
    [InlineData("GDR-1")]
    [InlineData("GDR-1a")]
    [InlineData("GDR-2")]
    [InlineData(SeededGdrs.LinksRecord)]
    public async Task MarkdownIsByteForByteTheAppDocumentAndReadsWithoutChangingTheProject(string id)
    {
        var path = ProjectWithBaseline(unicode: id == "GDR-1", links: id == SeededGdrs.LinksRecord);
        var shown = GdrQuery.Show(new GdrShowRequest(path, id)).Value!;
        var before = File.ReadAllBytes(path);
        var pendingRequest = new PendingChangesRequest(path, MotifProductVersion.CurrentText);
        var pending = ProjectionJson.Serialize(PendingChanges.Load(pendingRequest).Value);
        var app = new GdrMarkdownDocument(shown, Path.GetFileNameWithoutExtension(path));

        using var process = Process.Start(CliProcess.CreateStartInfo(_root, null, true,
            "guide", "show", "--project", path, id, "--format", "fw-md"))!;
        using var bytes = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(bytes);
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await Task.WhenAll(copy, stderr, process.WaitForExitAsync()).WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(2)));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        Assert.True(process.ExitCode == 0, await stderr);
        Assert.Empty(await stderr);
        Assert.Equal(Encoding.UTF8.GetBytes(app.Text), bytes.ToArray());
        var markdown = Encoding.UTF8.GetString(bytes.ToArray());
        Assert.False(app.Read(markdown).HasErrors);
        Assert.Empty(app.Read(markdown).Changes);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(pending, ProjectionJson.Serialize(PendingChanges.Load(pendingRequest).Value));
        Assert.Equal(ProjectionJson.Serialize(shown), ProjectionJson.Serialize(GdrQuery.Show(new GdrShowRequest(path, id)).Value));
        if (id is "GDR-2" or SeededGdrs.LinksRecord)
        {
            var normalized = markdown.Replace(CanonicalId.Parse(shown.Record.Id).ToGuid().ToString("D"),
                "00000000-0000-0000-0000-000000000000", StringComparison.Ordinal);
            if (id == SeededGdrs.LinksRecord)
            {
                var runs = shown.Discussion.SelectMany(paragraph => paragraph.Runs).ToArray();
                var resolved = runs.First(run => run.Target?.State == GdrLinkState.Resolved);
                var broken = Assert.Single(runs, run => run.Target?.State == GdrLinkState.Broken);
                var resolvedGuid = DefaultObjectMapper.Instance.Describe(resolved.ObjData!)!.Guid!.Value;
                var brokenGuid = DefaultObjectMapper.Instance.Describe(broken.ObjData!)!.Guid!.Value;
                normalized = normalized.Replace(resolvedGuid.ToString("D"), "11111111-1111-1111-1111-111111111111",
                    StringComparison.Ordinal).Replace(brokenGuid.ToString("D"), "22222222-2222-2222-2222-222222222222",
                    StringComparison.Ordinal);
                var discussion = GdrDiscussion.From(shown);
                Assert.Contains(discussion.Document.Paragraphs.SelectMany(paragraph => paragraph.Runs), GdrDiscussion.IsMarker);
                Assert.Equal(app.Text, new GdrMarkdownDocument(shown, Path.GetFileNameWithoutExtension(path),
                    discussion.Document.Paragraphs).Text);
                Assert.DoesNotContain(GdrDiscussion.BrokenMarker, markdown, StringComparison.Ordinal);
            }
            var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var golden = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.Cli",
                "Golden", id == "GDR-2" ? "gdr-show-fw-md.txt" : "gdr-show-links-fw-md.txt");
            if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(golden, normalized);
            Assert.Equal(File.ReadAllText(golden).Replace("\r\n", "\n", StringComparison.Ordinal), normalized);
        }
    }

    [Theory]
    [InlineData("show", "--format", "unknown")]
    [InlineData("show", "--format", "fw-md", "--json")]
    [InlineData("show", "--format")]
    [InlineData("list", "--format", "fw-md")]
    [InlineData("check", "--format", "fw-md")]
    public async Task MarkdownFormatRejectsInvalidOptionsWithoutOutput(params string[] options)
    {
        var path = ProjectWithBaseline();
        var arguments = new[] { "guide", options[0], "--project", path }
            .Concat(options[0] == "show" ? new[] { "GDR-1" } : [])
            .Concat(options.Skip(1)).ToArray();
        var cli = await CliProcess.RunAsync(_root, null, true, arguments);

        Assert.True(cli.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), cli.Error);
        Assert.Empty(cli.Output);
        Assert.Contains("Use --format fw-md with guide show, without --json.", cli.Error);
    }

    [Fact]
    public async Task MarkdownKeepsUnknownRecordAndMissingBaselineRefusals()
    {
        var path = ProjectWithBaseline();
        var unknown = await CliProcess.RunAsync(_root, null, true, "guide", "show", "--project", path, "GDR-99", "--format", "fw-md");
        Assert.True(unknown.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), unknown.Error);
        Assert.Empty(unknown.Output);
        var unknownRefusal = GdrQuery.Show(new GdrShowRequest(path, "GDR-99")).Refusal!;
        Assert.Equal(RefusalCodes.GdrNotFound, unknownRefusal.Code);
        Assert.Equal("error: " + unknownRefusal.Message + Environment.NewLine, unknown.Error);

        string absentPath;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            absentPath = cache.ProjectId.Path;
        }
        var absent = await CliProcess.RunAsync(Path.Combine(_root, "no-baseline"), null, true, "guide", "show", "--project", absentPath,
            "GDR-1", "--format", "fw-md");
        Assert.True(absent.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), absent.Error);
        Assert.Empty(absent.Output);
        var absentRefusal = GdrQuery.Show(new GdrShowRequest(absentPath, "GDR-1")).Refusal!;
        Assert.Equal(RefusalCodes.GdrNoBaseline, absentRefusal.Code);
        Assert.Equal("error: " + absentRefusal.Message + Environment.NewLine, absent.Error);
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

        var list = await CliProcess.RunAsync(_root, null, true, "guide", "list", "--project", path, "--json");
        Assert.True(list.ExitCode == 0, list.Error);
        Assert.Equal(ProjectionJson.Serialize(GdrQuery.List(new GdrListRequest(path)).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrListResponse>(list.Output)));

        var show = await CliProcess.RunAsync(_root, null, true, "guide", "show", "--project", path, "GDR-1", "--json");
        Assert.True(show.ExitCode == 0, show.Error);
        Assert.Equal(ProjectionJson.Serialize(GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrShowResponse>(show.Output)));

        var text = await CliProcess.RunAsync(_root, null, true, "guide", "show", "--project", path, "GDR-1");
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("GDR-1  title-1", text.Output);
        Assert.Contains("xylink", text.Output);
        Assert.Contains("Link 'link' -> https://example.invalid/x", text.Output);
    }

    [Fact]
    public async Task CheckAgreesWithTheQueryInJsonAndReadsAsTextForAPerson()
    {
        var path = ProjectWithBaseline();

        var json = await CliProcess.RunAsync(_root, null, true, "guide", "check", "--project", path, "--json");
        Assert.True(json.ExitCode == 0, json.Error);
        var wire = ProjectionJson.Deserialize<GdrCheckResponse>(json.Output)!;
        var query = GdrQuery.Check(new GdrCheckRequest(path)).Value!;
        Assert.Equal(ProjectionJson.Serialize(query with { Timing = null }), ProjectionJson.Serialize(wire with { Timing = null }));

        var text = await CliProcess.RunAsync(_root, null, true, "guide", "check", "--project", path);
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("The Grammar Guide is in sync with the grammar.", text.Output);
        Assert.Contains("Read 4 record(s)", text.Output);
    }

    [Fact]
    public async Task ShowNeedsAnIdAndAnUnknownRecordIsRefused()
    {
        var path = ProjectWithBaseline();

        var missing = await CliProcess.RunAsync(_root, null, true, "guide", "show", "--project", path);
        Assert.True(missing.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), missing.Error);

        var unknown = await CliProcess.RunAsync(_root, null, true, "guide", "show", "--project", path, "GDR-99");
        Assert.True(unknown.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), unknown.Error);
    }

    [Fact]
    public async Task TheRetiredGdrVerbIsAnUnknownCommand()
    {
        var old = await CliProcess.RunAsync(_root, null, true, "gdr", "list", "--project", ProjectWithBaseline());

        Assert.NotEqual(0, old.ExitCode);
        Assert.Contains("Unknown command 'gdr'", old.Error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
