using SIL.Motif.App;
using SIL.Motif.App.Views.Guide;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Host.Gdr;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed partial class GuideEditorTests
{
    [Fact]
    public void WholeRecordMarkdownRoundTripsWithoutChanges()
    {
        var record = First with { Id = CanonicalId.FromGuid(Guid.NewGuid()).Value };
        var shown = Canned(record) with
        {
            TitleAlternatives = [new RichStringAlternative("en", [new GdrRun("title", "en", null, Props())])],
            DescriptionAlternatives = [new RichStringAlternative("en", [new GdrRun("abstract", "en", null, Props())])],
        };
        var markdown = new GdrMarkdownDocument(shown, null);

        Assert.Contains("fwmd: 1", markdown.Text);
        Assert.Contains("::: {field=title}", markdown.Text);
        Assert.Contains("::: {field=description}", markdown.Text);
        Assert.Contains("::: {field=discussion}", markdown.Text);
        Assert.False(markdown.Read(markdown.Text).HasErrors);
        Assert.Empty(markdown.Read(markdown.Text).Changes);
    }

    [Theory]
    [InlineData("**title**")]
    [InlineData("[title](https://example.invalid)")]
    [InlineData("[title]{.v}")]
    public void TitleFormattingProducesAFinding(string edited)
    {
        var document = new GdrMarkdownDocument(MarkdownRecord(), null);
        var read = document.Read(document.Text.Replace("title-text", edited, StringComparison.Ordinal));
        Assert.True(read.HasErrors);
        Assert.Contains(read.Errors, error => error.Contains("title", StringComparison.OrdinalIgnoreCase));
    }
    private sealed class MarkdownStore : IGdrEditStore, IGdrMarkdownEditStore
    {
        public readonly List<GdrMarkdownEdits> Batches = [];
        public string? Refusal { get; set; }
        public Task<string?> StageAsync(string recordId, TextParagraphsPayload paragraphs) => Task.FromResult<string?>(null);
        public Task<string?> DiscardAsync(string recordId) => Task.FromResult<string?>(null);
        public Task<GdrPendingRead> PendingAsync(string recordId) => Task.FromResult(new GdrPendingRead(null));
        public Task<GdrApplyRefusal?> ApplyAsync() => Task.FromResult<GdrApplyRefusal?>(null);
        public Task<RichStringPendingRead> PendingRichStringAsync(string recordId, string field, string ws) =>
            Task.FromResult(new RichStringPendingRead(null));
        public Task<GdrMarkdownStageResult> StageMarkdownAsync(GdrMarkdownEdits edits)
        {
            if (Refusal is null) Batches.Add(edits);
            return Task.FromResult(new GdrMarkdownStageResult(edits.RichStrings.Count + (edits.Paragraphs is null ? 0 : 1), Refusal));
        }
        public Task<string?> RefreshAsync() => Task.FromResult<string?>(null);
    }

    private static GdrShowResponse MarkdownRecord()
    {
        var record = First with { Id = CanonicalId.FromGuid(Guid.NewGuid()).Value };
        return Canned(record) with
        {
            TitleAlternatives = [new RichStringAlternative("en", [new GdrRun("title-text", "en", null, Props())])],
            DescriptionAlternatives =
            [
                new RichStringAlternative("en", [new GdrRun("abstract-text", "en", null, Props())]),
                new RichStringAlternative("ar", [new GdrRun("ar-text", "ar", null, Props())]),
            ],
            Position = 1,
        };
    }

    [Fact]
    public void CopyMarkdownCopiesTheCurrentUnsavedText()
    {
        _avalonia.Invoke(() =>
        {
            var shown = MarkdownRecord();
            var window = new GuideEditorWindow([shown.Record], _ => shown, new MarkdownStore());
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            Completed(window.Loading);
            Assert.True(Completed(window.OpenMarkdownAsync()));
            window.MarkdownText += "unsaved";
            var copy = Assert.Single(window.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, MotifAppStrings.GuideMarkdownCopy));
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            Assert.Equal(window.MarkdownText, Completed(clipboard.TryGetTextAsync()));
        });
    }

    [Fact]
    public void WholeRecordMarkdownSavesChangedFieldsAsOneBatchAndUndoesThemTogether()
    {
        _avalonia.Invoke(() =>
        {
            var shown = MarkdownRecord();
            var store = new MarkdownStore();
            var window = new GuideEditorWindow([shown.Record], _ => shown, store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            Completed(window.Loading);
            Assert.True(Completed(window.OpenMarkdownAsync()));
            Assert.Contains("::: {field=description ws=ar}", window.MarkdownText);
            window.MarkdownText = window.MarkdownText.Replace("title-text", "new-title", StringComparison.Ordinal)
                .Replace("abstract-text", "new-abstract", StringComparison.Ordinal)
                .Replace("Words end at", "Words begin at", StringComparison.Ordinal);

            Assert.True(Completed(window.SaveMarkdownAsync()));
            var batch = Assert.Single(store.Batches);
            Assert.Equal(["CmPossibility.Name", "CmPossibility.Description"], batch.RichStrings.Select(edit => edit.Field));
            Assert.NotNull(batch.Paragraphs);
            Assert.False(window.IsMarkdownOpen);
            Completed(window.Loading);
            Assert.True(Completed(window.UndoMarkdownAsync()));
            Assert.Equal(2, store.Batches.Count);
            Assert.All(store.Batches[1].RichStrings, edit => Assert.Equal(edit.Expected.Text, edit.Text));
        });
    }

    [Fact]
    public void UnsavedWholeRecordMarkdownStaysWhenSwitchingOrClosingAndCanBeDiscarded()
    {
        _avalonia.Invoke(() =>
        {
            var shown = MarkdownRecord();
            var store = new MarkdownStore();
            var window = new GuideEditorWindow([shown.Record, Second], id => id == shown.Record.Id ? shown : Canned(Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
            list.SelectedIndex = 0;
            Completed(window.Loading);
            Assert.True(window.ToggleSourceMode());
            Assert.True(Completed(window.OpenMarkdownAsync()));
            window.MarkdownText += "edited";
            var kept = window.MarkdownText;
            window.Show(Second.Id);
            Assert.True(window.IsMarkdownOpen);
            Assert.Equal(kept, window.MarkdownText);
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBox>(), box => box.Text == kept);
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, MotifAppStrings.GuideMarkdownSave));
            list.SelectedIndex = 1;
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBox>(), box => box.Text == kept);
            var discard = Assert.Single(window.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, MotifAppStrings.GuideMarkdownThrowAway));
            window.Close();
            Assert.True(window.IsVisible);
            discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Show(Second.Id);
            Assert.False(window.IsMarkdownOpen);
        });
    }

    [Fact]
    public void AnUnchangedDocumentSavesNothingAndAnInvalidDocumentKeepsItsText()
    {
        _avalonia.Invoke(() =>
        {
            var shown = MarkdownRecord();
            var store = new MarkdownStore();
            var window = new GuideEditorWindow([shown.Record], _ => shown, store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            Completed(window.Loading);
            Assert.True(Completed(window.OpenMarkdownAsync()));
            Assert.True(Completed(window.SaveMarkdownAsync()));
            Assert.Empty(store.Batches);
            Assert.True(Completed(window.OpenMarkdownAsync()));
            window.MarkdownText = window.MarkdownText.Replace("::: {field=title}", "::: {field=unknown}", StringComparison.Ordinal);
            var invalid = window.MarkdownText;
            Assert.False(Completed(window.SaveMarkdownAsync()));
            Assert.Equal(invalid, window.MarkdownText);
            Assert.Contains("field", window.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(store.Batches);
            window.DiscardMarkdown();
        });
    }

    [Fact]
    public void MarkdownWarningIsShownButDoesNotBlockTheBatch()
    {
        _avalonia.Invoke(() =>
        {
            var shown = MarkdownRecord();
            var store = new MarkdownStore();
            var window = new GuideEditorWindow([shown.Record], _ => shown, store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            Completed(window.Loading);
            Assert.True(Completed(window.OpenMarkdownAsync()));
            window.MarkdownText = window.MarkdownText.Replace("abstract-text", "[x]{p999=3}", StringComparison.Ordinal);

            Assert.True(Completed(window.SaveMarkdownAsync()));
            Assert.Single(store.Batches);
            Assert.Contains("not a known", window.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void InvalidIdentityAndAStaleFieldKeepTheWholeMarkdownWithoutStaging()
    {
        _avalonia.Invoke(() =>
        {
            var shown = MarkdownRecord();
            var store = new MarkdownStore();
            var window = new GuideEditorWindow([shown.Record], _ => shown, store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(shown.Record.Id);
            Completed(window.Loading);
            Assert.True(Completed(window.OpenMarkdownAsync()));
            var original = window.MarkdownText;
            window.MarkdownText = original.Replace("GDR-1", "GDR-9", StringComparison.Ordinal);
            Assert.False(Completed(window.SaveMarkdownAsync()));
            Assert.Contains("read-only", window.StatusText, StringComparison.Ordinal);
            Assert.Empty(store.Batches);
            window.MarkdownText = original.Replace("title-text", "new-title", StringComparison.Ordinal);
            store.Refusal = "title changed in FieldWorks";
            Assert.False(Completed(window.SaveMarkdownAsync()));
            Assert.Contains("title", window.StatusText, StringComparison.Ordinal);
            Assert.True(window.IsMarkdownOpen);
            Assert.Empty(store.Batches);
            window.DiscardMarkdown();
        });
    }
    [Theory]
    [InlineData("GDR-1", "GDR-9")]
    [InlineData("guid: ", "guid: changed-")]
    [InlineData("parent: ", "parent: changed-")]
    [InlineData("position: 1", "position: 2")]
    public void ChangingAnyReadOnlyRecordIdentityIsAFinding(string before, string after)
    {
        var document = new GdrMarkdownDocument(MarkdownRecord(), null);
        Assert.True(document.Read(document.Text.Replace(before, after, StringComparison.Ordinal)).HasErrors);
    }

    [Fact]
    public void ADiscussionEditRetainsLinksStylesAndWritingSystems()
    {
        var shown = MarkdownRecord();
        var links = WithLinks(shown.Record);
        shown = shown with { Discussion = links.Discussion };
        var document = new GdrMarkdownDocument(shown, ProjectName);
        var read = document.Read(document.Text.Replace("See ", "Look ", StringComparison.Ordinal));
        Assert.False(read.HasErrors, string.Join("; ", read.Errors));
        var changed = Assert.Single(read.Changes);
        Assert.Equal("discussion", changed.Field);
        Assert.Equal(GdrReader.LinkData(ProjectName, Linked.Id), changed.Paragraphs[0].Runs.Single(run => run.Text == "GDR-9").ObjData);
        Assert.Equal("Normal", changed.Paragraphs[0].Style);
        Assert.Equal("ar", changed.Paragraphs[1].Runs[0].Ws);
    }
}
