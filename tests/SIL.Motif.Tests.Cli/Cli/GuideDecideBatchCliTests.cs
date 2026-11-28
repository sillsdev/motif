using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>guide decide --from</c> through the real CLI: its file and stdin forms, flag checks, dry run and refusal envelope.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GuideDecideBatchCliTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Row = "{\"section\":\"GDR-2\",\"choosing\":\"a\",\"over\":\"b\",\"because\":\"c\"}\n";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-guide-batch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Project()
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var name in new[] { "Heading 3", "Bulleted List" })
                {
                    if (cache.LangProject.StylesOC.Any(style => style.Name == name)) continue;
                    var style = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
                    cache.LangProject.StylesOC.Add(style);
                    style.Name = name;
                    style.Type = StyleType.kstParagraph;
                }
            });
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);
        return path;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string? stdin, params string[] arguments)
    {
        var start = CliProcess.CreateStartInfo(_root, null, true, arguments);
        start.RedirectStandardInput = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(stdin ?? "");
        process.StandardInput.Close();
        await process.WaitForExitAsync().WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(5)));
        return (process.ExitCode, await output, await error);
    }

    private static bool Staged(string path) => GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: PendingChanges.DraftName)).Succeeded;

    [Fact]
    public async Task AFileWithDryRunPrintsEachLineAndStagesNothing()
    {
        var path = Project();
        var file = Path.Combine(_root, "d.jsonl");
        File.WriteAllText(file, Row);

        var run = await RunAsync(null, "guide", "decide", "--project", path, "--from", file, "--dry-run", "--autonomous");

        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        Assert.Contains("Dry run", run.Output, StringComparison.Ordinal);
        Assert.Contains("line 1 GDR-2: - a or b, I am choosing a, because c.", run.Output, StringComparison.Ordinal);
        Assert.False(Staged(path));
    }

    [Fact]
    public async Task StandardInputStagesTheBatchAndReportsItAsJson()
    {
        var path = Project();

        var run = await RunAsync(Row, "guide", "decide", "--project", path, "--from", "-", "--autonomous", "--json");

        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        using var json = JsonDocument.Parse(run.Output);
        Assert.Contains("I am choosing a", run.Output, StringComparison.Ordinal);
        Assert.True(Staged(path));
    }

    [Fact]
    public async Task ABadRowFailsWithTheJsonEnvelopeAndItsRow()
    {
        var path = Project();

        var run = await RunAsync(Row + "{\"section\":\"GDR-1\",\"choosing\":\"see @smith2001\",\"over\":\"b\",\"because\":\"c\"}\n",
            "guide", "decide", "--project", path, "--from", "-", "--autonomous", "--json");

        Assert.NotEqual(0, run.ExitCode);
        using var json = JsonDocument.Parse(run.Error.Trim().Length > 0 ? run.Error : run.Output);
        var text = json.RootElement.GetRawText();
        Assert.Contains("guide.decisions-invalid", text, StringComparison.Ordinal);
        Assert.Contains("\"row\"", text, StringComparison.Ordinal);
        Assert.NotNull(SIL.Motif.Contract.Commands.RefusalCatalog.Specs["guide.decisions-invalid"].Next);
        Assert.False(Staged(path));
    }

    [Theory]
    [InlineData("--nonsense")]
    [InlineData("--choosing")]
    public async Task AnUnknownFlagOrAMixedFormIsUsage(string flag)
    {
        var path = Project();
        var file = Path.Combine(_root, "d.jsonl");
        File.WriteAllText(file, Row);

        var run = await RunAsync(null, "guide", "decide", "--project", path, "--from", file, flag, "x", "--autonomous");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("guide decide", run.Error + run.Output, StringComparison.Ordinal);
        Assert.False(Staged(path));
    }

    [Fact]
    public async Task AnUnreadableFileIsUsageNamingTheFile()
    {
        var path = Project();
        var missing = Path.Combine(_root, "missing.jsonl");

        var run = await RunAsync(null, "guide", "decide", "--project", path, "--from", missing, "--autonomous");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("cannot be read", run.Error + run.Output, StringComparison.Ordinal);
    }
}
