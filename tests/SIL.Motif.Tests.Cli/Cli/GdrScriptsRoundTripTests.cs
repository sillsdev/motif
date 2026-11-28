using System.Diagnostics;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.RichText;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Complex-script and right-to-left Grammar Guide text through the real path a grammar lead takes:
/// <c>guide show --format fw-md</c>, edit the file, <c>guide edit --from-file</c>, dry run and apply, then show again.
/// The runs are read back from LibLCM, not from Motif's own view.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrScriptsRoundTripTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-gdr-scripts-" + Guid.NewGuid().ToString("N"));

    private const string TamilTitle = "வினைச்சொல் இணைப்பு";
    private const string Conjuncts = "க்ஷ ஸ்ரீ";

    // The Discussion a lead writes: Tamil vernacular in Swahili and English prose.
    private static readonly string Body = string.Join("\n\n",
        "### " + TamilTitle,
        "Kitenzi [வினைச்சொல்]{.v} na [க்ஷ]{ws=ta} pamoja na [ஸ்ரீ]{ws=ta}: the verb joins.",
        "[مرحبا بالعالم]{ws=ar}",
        "Mixed: the word [كتاب]{ws=ar} means book, like [புத்தகம்]{ws=ta}, in one run.",
        "| Form | Gloss |\n|---|---|\n| [புத்தகம்]{ws=ta} | kitabu |\n| [வினை]{ws=ta} | kitenzi |");

    private static void SeedScripts(LcmCache cache)
    {
        SeededGdrs.Seed(cache);
        SeededGdrs.SeedTableStyles(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystemManager.GetOrSet("ta", out var tamil);
            var systems = cache.ServiceLocator.WritingSystems;
            systems.VernacularWritingSystems.Add(tamil);
            systems.CurrentVernacularWritingSystems.Add(tamil);
            foreach (var name in new[] { "Heading 3", "Bulleted List" })
            {
                if (cache.LangProject.StylesOC.Any(style => style.Name == name)) continue;
                var style = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
                cache.LangProject.StylesOC.Add(style);
                style.Name = name;
                style.Type = StyleType.kstParagraph;
            }
        });
    }

    private string Project()
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeedScripts(cache);
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return path;
    }

    // FieldWorks stores and shows text decomposed (NFD), so what comes back is the NFD of what went in.
    private static string D(string text) => text.Normalize(NormalizationForm.FormD);

    private static void Same(string expected, string actual) =>
        Assert.True(expected == actual, "expected " + System.Text.Json.JsonSerializer.Serialize(expected) +
            " but was " + System.Text.Json.JsonSerializer.Serialize(actual));

    private async Task<string> ShowAsync(string path, string id, string? asOf = null)
    {
        var arguments = new List<string> { "guide", "show", "--project", path, id, "--format", "fw-md" };
        if (asOf is not null) arguments.AddRange(["--as-of", asOf]);
        using var process = Process.Start(CliProcess.CreateStartInfo(_root, null, true, arguments.ToArray()))!;
        using var bytes = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(bytes);
        var stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(copy, stderr, process.WaitForExitAsync()).WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(2)));
        Assert.True(process.ExitCode == 0, await stderr);
        return new UTF8Encoding(false, true).GetString(bytes.ToArray());
    }

    private async Task<GdrEditResponse> EditAsync(string path, string id, string markdown)
    {
        var file = Path.Combine(_root, "edit-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(markdown));
        var edit = await CliProcess.RunAsync(_root, null, true, "guide", "edit", "--project", path, id, "--from-file", file, "--json");
        Assert.True(edit.ExitCode == 0, edit.Error + edit.Output);
        return ProjectionJson.Deserialize<GdrEditResponse>(edit.Output)!;
    }

    // Replaces the title and the Discussion of the shown whole record.
    private static string WithContent(string shown, string title, string body)
    {
        var text = shown.Replace("title-2", title, StringComparison.Ordinal);
        var start = text.IndexOf("::: {field=discussion}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, text);
        return text[..start] + "::: {field=discussion}\n" + body + "\n:::\n";
    }

    private void Apply(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(_root));
        try
        {
            var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null),
                dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner);
            Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
            Assert.True(applied.Value!.Applied, applied.Value.Summary);
        }
        finally
        {
            runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);
    }

    /// <summary>What LibLCM holds: the title, and each paragraph's runs as text|tag.</summary>
    private static (string Title, string[][] Paragraphs, string[] Styles) FromLcm(string path, string abbreviation)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var record = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances()
            .Single(item => item.Abbreviation.AnalysisDefaultWritingSystem.Text == abbreviation);
        var wsm = cache.ServiceLocator.WritingSystemManager;
        var title = record.Name.AnalysisDefaultWritingSystem.Text;
        var paragraphs = record.DiscussionOA.ParagraphsOS.Cast<IStTxtPara>().Select(paragraph =>
        {
            var contents = paragraph.Contents;
            return Enumerable.Range(0, contents.RunCount).Select(i =>
            {
                var run = contents.get_RunText(i);
                var ws = contents.get_Properties(i).GetIntPropValues((int)FwTextPropType.ktptWs, out _);
                return run + "|" + wsm.Get(ws).Id;
            }).ToArray();
        }).ToArray();
        return (title, paragraphs, record.DiscussionOA.ParagraphsOS.Cast<IStTxtPara>().Select(p => p.StyleName).ToArray());
    }

    [Fact]
    public async Task ATamilTitleAndBodyWithVowelSignsAndViramaRoundTripThroughShowEditApplyAndShow()
    {
        var path = Project();
        var markdown = WithContent(await ShowAsync(path, "GDR-2"), TamilTitle, Body);

        var staged = await EditAsync(path, "GDR-2", markdown);
        Assert.Contains("title", staged.Fields);
        Assert.Contains("discussion", staged.Fields);
        Apply(path);

        // FieldWorks stores text decomposed, so it comes back as the NFD of what was written.
        Same(D(markdown), await ShowAsync(path, "GDR-2"));
        var held = FromLcm(path, "GDR-2");
        Same(D(TamilTitle), held.Title);
        foreach (var conjunct in Conjuncts.Split(' '))
            Assert.Contains(D(conjunct) + "|ta", held.Paragraphs.SelectMany(p => p));
    }

    [Fact]
    public async Task EditingTheSameFileAgainAfterApplyStagesNothingEvenThoughFieldWorksStoredItDecomposed()
    {
        var path = Project();
        var markdown = WithContent(await ShowAsync(path, "GDR-2"), TamilTitle, Body);
        await EditAsync(path, "GDR-2", markdown);
        Apply(path);

        var again = await EditAsync(path, "GDR-2", markdown);

        Assert.Empty(again.Fields);
        Assert.Equal(0, again.OperationCount);
    }

    [Fact]
    public async Task InlineVernacularAndExplicitWsSpansKeepTheirWritingSystemInLibLcmAfterApply()
    {
        var path = Project();
        var markdown = WithContent(await ShowAsync(path, "GDR-2"), "title-2", Body);

        await EditAsync(path, "GDR-2", markdown);
        Apply(path);

        var runs = FromLcm(path, "GDR-2").Paragraphs.SelectMany(p => p).ToArray();
        Assert.Contains(D("க்ஷ") + "|ta", runs);
        Assert.Contains(D("ஸ்ரீ") + "|ta", runs);
        Assert.Contains(D("புத்தகம்") + "|ta", runs);
        Assert.Contains("مرحبا بالعالم|ar", runs);
        Assert.Contains("كتاب|ar", runs);
        // The {.v} span is the project's default vernacular writing system, not the analysis language.
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var defaultVernacular = cache.ServiceLocator.WritingSystemManager.Get(cache.DefaultVernWs).Id;
        Assert.Contains(D("வினைச்சொல்") + "|" + defaultVernacular, runs);
        Assert.DoesNotContain(runs, run => run.EndsWith("|en", StringComparison.Ordinal) && run.Contains('க'));
    }

    [Fact]
    public async Task AnNfdFileRoundTripsByteExactWhileAnNfcFileComesBackDecomposed()
    {
        var path = Project();
        // Two-part Tamil vowel signs are the letters whose NFC and NFD forms differ.
        var nfc = "கொ கோ கௌ".Normalize(NormalizationForm.FormC);
        var nfd = nfc.Normalize(NormalizationForm.FormD);
        Assert.NotEqual(nfc, nfd);
        var markdown = WithContent(await ShowAsync(path, "GDR-2"), nfd, nfd);

        await EditAsync(path, "GDR-2", markdown);
        Apply(path);

        var held = FromLcm(path, "GDR-2");
        Same(nfd, held.Title);
        Same(nfd, held.Paragraphs.Single().Single().Split('|')[0]);
        Same(markdown, await ShowAsync(path, "GDR-2"));
    }

    [Fact]
    public async Task ARightToLeftParagraphKeepsItsDirectionAndAMixedRunKeepsItsOrderAndWritingSystems()
    {
        var path = Project();
        Assert.Contains("rightToLeft=1", await ShowAsync(path, "GDR-1"), StringComparison.Ordinal);
        var markdown = WithContent(await ShowAsync(path, "GDR-2"), "title-2",
            "::: {rightToLeft=1}\n[مرحبا بالعالم]{ws=ar}\n:::\n\nEnglish [كتاب]{ws=ar} then [புத்தகம்]{ws=ta} and ASCII.");

        await EditAsync(path, "GDR-2", markdown);
        Apply(path);

        Same(D(markdown), await ShowAsync(path, "GDR-2"));
        var held = FromLcm(path, "GDR-2").Paragraphs;
        Assert.Equal(["مرحبا بالعالم|ar"], held[0]);
        Assert.Equal(["English |en", "كتاب|ar", " then |en", D("புத்தகம்") + "|ta", " and ASCII.|en"], held[1]);
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var record = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances()
            .Single(item => item.Abbreviation.AnalysisDefaultWritingSystem.Text == "GDR-2");
        var paragraphs = record.DiscussionOA.ParagraphsOS.Cast<IStTxtPara>().ToArray();
        Assert.Equal(1, paragraphs[0].StyleRules.GetIntPropValues((int)FwTextPropType.ktptRightToLeft, out _));
        Assert.NotEqual(1, paragraphs[1].StyleRules?.GetIntPropValues((int)FwTextPropType.ktptRightToLeft, out _) ?? -1);
    }

    [Fact]
    public async Task ATableOfTamilCellsKeepsTheTableStylesAndEveryCell()
    {
        var path = Project();
        var table = "| Form | Gloss |\n|---|---|\n| [புத்தகம்]{ws=ta} | kitabu |\n| [வினைச்சொல்]{ws=ta} | kitenzi |";
        var markdown = WithContent(await ShowAsync(path, "GDR-2"), "title-2", table);

        await EditAsync(path, "GDR-2", markdown);
        Apply(path);

        var held = FromLcm(path, "GDR-2");
        Assert.Equal(["Table Header", "Table Row", "Table Row"], held.Styles);
        Assert.Contains(D("புத்தகம்") + "|ta", held.Paragraphs[1]);
        Assert.Contains(D("வினைச்சொல்") + "|ta", held.Paragraphs[2]);
        Same(D(markdown), await ShowAsync(path, "GDR-2"));
    }

    [Fact]
    public async Task SwahiliProseWithATamilExampleInOneSectionRoundTripsAndGuideCheckHasNoFalseFindings()
    {
        var path = Project();
        var prose = "Kitenzi cha Kiswahili huchukua viambishi: *ni-na-soma*. Mfano wa Kitamil: [வினைச்சொல்]{ws=ta} (\"verb\").";
        var markdown = WithContent(await ShowAsync(path, "GDR-2"), "title-2", prose + "\n\n" + Body);
        var before = await CliProcess.RunAsync(_root, null, true, "guide", "check", "--project", path, "--json", "--autonomous");

        await EditAsync(path, "GDR-2", markdown);
        Apply(path);

        Same(D(markdown), await ShowAsync(path, "GDR-2"));
        var check = await CliProcess.RunAsync(_root, null, true, "guide", "check", "--project", path, "--json", "--autonomous");
        Assert.True(check.ExitCode == 0, check.Error + check.Output);
        Assert.DoesNotContain("fwmd.", check.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("fwmd.", before.Output, StringComparison.Ordinal);
    }

    private string ProjectWithQueryBlock(string expect)
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var record = SeededGdrs.Records(cache).PossibilitiesOS.Single(item => item.Abbreviation.get_String(cache.DefaultAnalWs).Text == "GDR-2");
                record.DiscussionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                foreach (var line in new[] { "```motif-query", "id: all", "title: All", "run: motif query forms", "expect: " + expect, "```" })
                {
                    var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                    record.DiscussionOA.ParagraphsOS.Add(paragraph);
                    paragraph.Contents = TsStringUtils.MakeString(line, cache.DefaultAnalWs);
                }
            });
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return path;
    }

    [Fact]
    public async Task GuideCheckExitsSevenWhenAQueryBlocksCountIsOutsideItsExpectAndZeroWhenItIsInside()
    {
        var failing = await CliProcess.RunAsync(_root, null, true, "guide", "check", "--project", ProjectWithQueryBlock(">=1000000"), "--json", "--autonomous");
        Assert.True(failing.ExitCode == 7, failing.FailureDetails);
        Assert.Contains("guide.query-count-mismatch", failing.Output, StringComparison.Ordinal);
        var passing = await CliProcess.RunAsync(_root, null, true, "guide", "check", "--project", ProjectWithQueryBlock(">=0"), "--json", "--autonomous");
        Assert.True(passing.ExitCode == 0, passing.FailureDetails);
        Assert.DoesNotContain("guide.query-count-mismatch", passing.Output, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
