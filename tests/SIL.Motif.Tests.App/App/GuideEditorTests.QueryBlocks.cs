using SIL.Motif.App.Views.Guide;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed partial class GuideEditorTests
{
    private static GdrShowResponse WithQueryBlock()
    {
        var record = First with { Id = CanonicalId.FromGuid(Guid.NewGuid()).Value };
        GdrParagraph Line(string text, string key) =>
            new("Normal", false, "en", Props(style: "Normal"), [new GdrRun(text, "en", null, Props())], key);
        return Canned(record) with
        {
            Discussion = [..GuideQueryTestSupport.SectionLines.Select((text, index) => Line(text, "q" + index))],
        };
    }

    [Fact]
    public void ReplacingAQueryBodyStagesTheDocumentWithOnlyThatBlocksLinesChanged()
    {
        _avalonia.Invoke(() =>
        {
            var shown = WithQueryBlock();
            var store = new MarkdownStore();
            var window = new GuideEditorWindow([shown.Record], _ => shown, store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            Completed(window.Loading);

            var refusal = Completed(window.ReplaceQueryBodyAsync("alternates",
                "id: alternates\ntitle: Alternates\nrun: motif query forms"));

            Assert.Null(refusal);
            var batch = Assert.Single(store.Batches);
            var changed = batch.Paragraphs!.Changed;
            Assert.Equal([3, 4], changed.Select(change => change.Index));
            Assert.Equal(["title: Alternates", "run: motif query forms"],
                changed.Select(change => string.Concat(change.Runs.Select(run => run.Text))));
            Assert.Empty(batch.Paragraphs.Removed);
        });
    }

    [Fact]
    public void AnUnknownQueryIsRefusedWithAMessageAndStagesNothing()
    {
        _avalonia.Invoke(() =>
        {
            var shown = WithQueryBlock();
            var store = new MarkdownStore();
            var window = new GuideEditorWindow([shown.Record], _ => shown, store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            Completed(window.Loading);

            var refusal = Completed(window.ReplaceQueryBodyAsync("missing", "id: missing\nrun: motif query forms"));

            Assert.NotEmpty(refusal!);
            Assert.Empty(store.Batches);
        });
    }
}
