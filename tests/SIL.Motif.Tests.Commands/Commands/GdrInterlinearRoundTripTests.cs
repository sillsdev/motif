using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>An interlinear block survives guide show, guide edit and guide show again (ADR 0005).</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrInterlinearRoundTripTests(PristineProjectFixture pristine)
{
    public static TheoryData<string, string> Examples => new()
    {
        { "swahili", "::: {.interlinear}\nword: [nilisoma kitabu]{.v}\nmorphemes: [ni-li-som-a kitabu]{.v}\ngloss: 1SG-PST-read-FV book\ntranslation: I read a book.\n:::" },
        { "tamil", "::: {.interlinear}\nword: [நான் புத்தகம் படித்தேன்]{.v}\nmorphemes: [நான் புத்தகம் படி-த்த்-ஏன்]{.v}\ngloss: 1SG book read-PST-1SG\ntranslation: I read the book.\n:::" },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void ABlockReturnsAsTheSameBlockAndIsStoredAsPlainLabelledLines(string name, string block)
    {
        _ = name;
        var path = ProjectWithGdrs();
        var body = "Before the example.\n\n" + block + "\n\nAfter the example.";

        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-9", body));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var preview = DiscussionBody(GdrQuery.Show(new GdrShowRequest(path, "GDR-9", AsOf: staged.Value!.DraftName)).Value!, path);
        var parsed = Assert.Single(GuideInterlinear.Parse(preview).Blocks);
        Assert.Equal(GuideInterlinear.Parse(block).Blocks[0].Columns.Select(column => (column.Word, column.Morphemes, column.Gloss)),
            parsed.Columns.Select(column => (column.Word, column.Morphemes, column.Gloss)));
        Assert.Equal(GuideInterlinear.Parse(block).Blocks[0].Translation, parsed.Translation);
        Assert.Contains("{.v}", preview, StringComparison.Ordinal);

        Assert.True(Apply(path).Succeeded);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

        var shown = Show(path, "GDR-9");
        var lines = shown.Discussion.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))).ToArray();
        Assert.Equal(6, lines.Length);
        Assert.Equal(["word: ", "morphemes: ", "gloss: ", "translation: "], lines[1..5].Select(line => line[..(line.IndexOf(':') + 2)]));
        var word = shown.Discussion[1].Runs;
        Assert.Equal(2, word.Count);
        Assert.NotEqual(word[0].WritingSystem, word[1].WritingSystem);
        Assert.Equal(word[0].WritingSystem, Assert.Single(shown.Discussion[3].Runs).WritingSystem);
        Assert.Single(GuideInterlinear.ParseStored(lines).Blocks);

        var markdown = Markdown(path, "GDR-9");
        Assert.True(markdown.Contains("::: {.interlinear}", StringComparison.Ordinal), markdown);
        var again = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-9", markdown));
        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Empty(again.Value!.Fields);
        Assert.Equal(markdown, Markdown(path, "GDR-9"));
    }

    [Fact]
    public void ChangingOnlyTheTranslationStagesOneParagraph()
    {
        var path = ProjectWithGdrs();
        var block = Examples.First(item => (string)item[0] == "swahili")[1]!.ToString()!;
        Assert.True(GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-9", block)).Succeeded);
        Assert.True(Apply(path).Succeeded);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        var markdown = Markdown(path, "GDR-9");

        var edited = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-9", markdown.Replace("I read a book.", "I was reading a book.", StringComparison.Ordinal)));

        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        Assert.Equal(["discussion"], edited.Value!.Fields);
        var preview = GdrQuery.Show(new GdrShowRequest(path, "GDR-9", AsOf: edited.Value.DraftName)).Value!;
        Assert.Contains("I was reading a book.", string.Concat(preview.Discussion[^1].Runs.Select(run => run.Text)), StringComparison.Ordinal);
        Assert.Equal(Show(path, "GDR-9").Discussion.Take(3).Select(paragraph => paragraph.Key), preview.Discussion.Take(3).Select(paragraph => paragraph.Key));
    }

    [Fact]
    public void ADivWhoseLinesAreNotTheLabelsIsRefusedAndNothingIsStaged()
    {
        var path = ProjectWithGdrs();

        var refused = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-9", "::: {.interlinear}\nword: a\nnonsense\n:::"));

        Assert.Equal(RefusalCodes.GdrMarkdownInvalid, refused.Refusal!.Code);
    }

    private static string DiscussionBody(GdrShowResponse shown, string path) =>
        new GdrMarkdownView(shown, Path.GetFileNameWithoutExtension(path)).Fields.Single(field => field.Name == "discussion").Body;

    private static GdrShowResponse Show(string path, string id)
    {
        var shown = GdrQuery.Show(new GdrShowRequest(path, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private static string Markdown(string path, string id) => new GdrMarkdownView(Show(path, id), Path.GetFileNameWithoutExtension(path)).Text;

    private static CommandOutcome<ApplyPendingResult> Apply(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(RootFor(path)));
        try
        {
            return PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null),
                dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner);
        }
        finally
        {
            runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private string ProjectWithGdrs()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        SeededGdrs.SeedRecord(cache, "GDR-9", "Interlinear");
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "gdr-edit-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
