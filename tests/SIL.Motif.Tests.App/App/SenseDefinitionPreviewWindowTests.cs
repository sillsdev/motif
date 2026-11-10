using Avalonia.Headless;
using Avalonia.Input;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SenseDefinitionPreviewWindowTests(AvaloniaHeadlessFixture avalonia)
{
    private const string SenseId = "sense-1";

    private static GdrTextProperties Props(int? italic = null, string? style = null) => new(
        italic is { } value ? new Dictionary<int, GdrIntProperty> { [TextPropertyNumbers.Italic] = new(0, value) } : new Dictionary<int, GdrIntProperty>(),
        style is null ? new Dictionary<int, string>() : new Dictionary<int, string> { [TextPropertyNumbers.NamedStyle] = style });

    private static SenseDefinitionsResponse Canned(string english = "x ") => new(
    [
        new SenseDefinitions(SenseId, "motifa  gloss",
        [
            new RichStringAlternative("en", [new GdrRun(english, "en", null, Props()), new GdrRun("y", "en", "Emphasis", Props(style: "Emphasis"))]),
            new RichStringAlternative("ar", []),
        ]),
        new SenseDefinitions("sense-2", "motifb  other", [new RichStringAlternative("en", []), new RichStringAlternative("ar", [])]),
    ],
    [new GdrStyle("Emphasis", GdrStyleKind.Character, new Dictionary<string, GdrTextProperties> { ["en"] = Props(italic: 1), ["ar"] = Props(italic: 1) })],
    [new GdrWritingSystem("ar", "rtl", "Segoe UI"), new GdrWritingSystem("en", "ltr", "Segoe UI")]);

    private static TextRichStringExpected Start(SenseDefinitionsResponse senses, string ws)
    {
        var alternative = senses.Senses[0].Definition.Single(item => item.WritingSystem == ws);
        return new(alternative.Text, alternative.Digest);
    }

    private sealed class FakeStore(GdrApplyRefusal? applyRefusal = null) : ISenseDefinitionStore
    {
        public List<(string SenseId, TextRichStringPayload RichString)> Staged { get; } = [];
        public List<(string SenseId, string Ws)> Discards { get; } = [];
        public Dictionary<string, TextRichStringPayload> Pending { get; } = [];
        public string? StageRefusal { get; set; }
        public string? PendingRefusal { get; set; }
        public TaskCompletionSource? PendingGate { get; set; }
        public TaskCompletionSource? ApplyGate { get; set; }
        public GdrApplyRefusal? ApplyRefusal { get; set; } = applyRefusal;
        public int Applies { get; private set; }

        public Task<string?> StageAsync(string senseId, TextRichStringPayload richString)
        {
            if (StageRefusal is { } refusal) return Task.FromResult<string?>(refusal);
            Staged.Add((senseId, richString));
            return Task.FromResult<string?>(null);
        }

        public Task<string?> DiscardAsync(string senseId, string ws)
        {
            Discards.Add((senseId, ws));
            return Task.FromResult<string?>(null);
        }

        public async Task<RichStringPendingRead> PendingAsync(string senseId, string ws)
        {
            if (PendingGate is { } gate) await gate.Task;
            return new RichStringPendingRead(PendingRefusal is null ? Pending.GetValueOrDefault(ws) : null, PendingRefusal);
        }

        public async Task<GdrApplyRefusal?> ApplyAsync()
        {
            Applies++;
            if (ApplyGate is { } gate) await gate.Task;
            return ApplyRefusal;
        }
    }

    private static SenseDefinitionPreviewWindow Opened(FakeStore? store, out IDisposable closer, Func<SenseDefinitionsResponse>? read = null,
        Func<Task<bool>>? confirm = null)
    {
        read ??= () => Canned();
        var window = new SenseDefinitionPreviewWindow(read(), () => read(), store, confirm);
        closer = window.CloseOnExit();
        window.Show();
        window.Show(SenseId);
        Completed(window.Loading);
        return window;
    }

    private static void Completed(Task task)
    {
        while (!task.IsCompleted) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        task.GetAwaiter().GetResult();
    }

    private static T Completed<T>(Task<T> task)
    {
        while (!task.IsCompleted) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return task.GetAwaiter().GetResult();
    }

    private static void Type(SenseDefinitionPreviewWindow window, string ws, string text)
    {
        window.FieldFor(ws)!.Focus();
        window.KeyTextInput(text);
        window.SessionFor(ws)!.EndSession();
    }

    [Fact]
    public void ASenseShowsOneFieldPerWritingSystemWithItsDirectionAndCharacterStyle()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(null, out var closer);
            using var _ = closer;

            var english = window.FieldFor("en")!;
            Assert.False(english.IsReadOnly);
            Assert.Equal("x y", english.Editor.Document.Paragraphs.Single().Text);
            Assert.Equal("Emphasis", english.Editor.Document.Paragraphs[0].Runs[1].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            Assert.Equal(1, english.StyleResolver!.Resolve("Emphasis", "en")!.IntProps[TextPropertyNumbers.Italic].Value);
            var arabic = window.FieldFor("ar")!;
            Assert.True(arabic.Editor.Document.Paragraphs.Single().Rtl);
            Assert.True(arabic.WritingSystems.Get("ar").Rtl);
            Assert.Equal("motifa  gloss", window.HeadingText);
        });
    }

    [Fact]
    public void LeavingAFieldStagesItsWritingSystemsEditWithTheRunsAndTheTextItBeganFrom()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Opened(store, out var closer);
            using var _ = closer;

            Type(window, "en", "z");
            Type(window, "ar", "ب");

            Assert.Equal(2, store.Staged.Count);
            var (senseId, english) = store.Staged[0];
            Assert.Equal((SenseId, SenseDefinitionStore.Field, "en"), (senseId, english.Field, english.Ws));
            Assert.Equal([("zx ", (string?)null), ("y", "Emphasis")], english.Runs.Select(run => (run.Text, run.CharacterStyle)));
            Assert.Equal(Start(Canned(), "en"), english.Expected);
            var arabic = store.Staged[1].RichString;
            Assert.Equal(("ar", "ب", new TextRichStringExpected("", TextRunsDigest.OfRuns([]))), (arabic.Ws, arabic.Text, arabic.Expected));
        });
    }

    [Fact]
    public void AFormattingOnlyChangeIsStaged()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Opened(store, out var closer);
            using var _ = closer;
            var field = window.FieldFor("en")!;
            var paragraph = field.Editor.Document.Paragraphs[0];
            var bolded = paragraph.Clone();
            bolded.Runs[0].Props.SetInt(TextPropertyNumbers.Bold, 1, 0);
            field.Editor.Apply(new EditDelta { Start = 0, Before = [paragraph], After = [bolded] });

            window.SessionFor("en")!.EndSession();

            var staged = Assert.Single(store.Staged).RichString;
            Assert.Equal("x y", staged.Text);
            Assert.Equal((0, 1), staged.Runs[0].Props!.Ints[TextPropertyNumbers.Bold]);
        });
    }

    [Fact]
    public void EditingBackToTheStartTakesTheEditOutOfTheDraft()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Opened(store, out var closer);
            using var _ = closer;
            Type(window, "en", "z");

            Assert.True(window.SessionFor("en")!.Undo());
            window.SessionFor("en")!.EndSession();

            Assert.Equal([(SenseId, "en")], store.Discards);
        });
    }

    [Fact]
    public void ADraftEditIsShownAndALaterSessionKeepsTheFirstStart()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var began = Start(Canned(), "en");
            store.Pending["en"] = new TextRichStringPayload(SenseDefinitionStore.Field, "en", [new TextRun("kept", "en", "Emphasis")], began);
            var window = Opened(store, out var closer);
            using var _ = closer;

            Assert.Equal("kept", window.FieldFor("en")!.Editor.Document.Paragraphs[0].Text);
            Assert.True(window.IsUnappliedStripShown);
            window.FieldFor("en")!.Editor.SetCaret(new TextPosition(0, 4));
            Type(window, "en", "!");

            var staged = Assert.Single(store.Staged).RichString;
            Assert.Equal(("kept!", began), (staged.Text, staged.Expected));
            Assert.Equal("Emphasis", staged.Runs[0].CharacterStyle);
        });
    }

    [Fact]
    public void TypingBeforeTheDraftHasBeenReadIsHeldBack()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore { PendingGate = new TaskCompletionSource() };
            var window = new SenseDefinitionPreviewWindow(Canned(), () => Canned(), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(SenseId);
            window.FieldFor("en")!.Focus();

            window.KeyTextInput("lost");
            Assert.True(window.FieldFor("en")!.IsReadOnly);
            store.PendingGate.SetResult();
            Completed(window.Loading);

            Assert.False(window.FieldFor("en")!.IsReadOnly);
            Assert.Equal("x y", window.FieldFor("en")!.Editor.Document.Paragraphs[0].Text);
        });
    }

    [Fact]
    public void AFieldWhoseDraftEditCannotBeReadStaysReadOnlyAndSaysWhy()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(new FakeStore { PendingRefusal = "The project store is busy." }, out var closer);
            using var _ = closer;

            Assert.True(window.FieldFor("en")!.IsReadOnly);
            Assert.Contains("The project store is busy.", window.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ADraftEditBegunFromTextFieldWorksHasSinceChangedIsStaleUntilStartedAgain()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            store.Pending["en"] = new TextRichStringPayload(SenseDefinitionStore.Field, "en", [new TextRun("mine", "en")], Start(Canned(), "en"));
            var asked = 0;
            var window = Opened(store, out var closer, () => Canned("fw "), () => Task.FromResult(++asked > 0));
            using var _ = closer;

            Assert.True(window.IsStaleStripShown);
            Assert.True(window.FieldFor("en")!.IsReadOnly);
            Assert.Equal("fw y", window.FieldFor("en")!.Editor.Document.Paragraphs[0].Text);
            Assert.False(window.FieldFor("ar")!.IsReadOnly);

            store.Pending.Clear();
            Assert.True(Completed(window.StartAgainFromFieldWorksAsync()));

            Assert.Equal(1, asked);
            Assert.Equal([(SenseId, "en")], store.Discards);
            Assert.False(window.IsStaleStripShown);
            Assert.False(window.FieldFor("en")!.IsReadOnly);
        });
    }

    [Fact]
    public void AnApplyHeldForDriftKeepsTheTypingAndShowsTheStrip()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore(new GdrApplyRefusal("changed", SenseId, "en"));
            var window = Opened(store, out var closer);
            using var _ = closer;
            Type(window, "en", "z");

            Assert.False(Completed(window.ApplyAsync()));

            Assert.True(window.IsStaleStripShown);
            Assert.Equal("zx y", window.FieldFor("en")!.Editor.Document.Paragraphs[0].Text);
            Assert.Equal("Nothing was applied.", window.StatusText);
        });
    }

    [Fact]
    public void EveryFieldIsReadOnlyWhileApplyRunsAndOneOpenedThenStaysSo()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore { ApplyGate = new TaskCompletionSource() };
            var window = Opened(store, out var closer);
            using var _ = closer;
            Type(window, "en", "a");

            var applying = window.ApplyAsync();
            while (store.Applies == 0) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(window.FieldFor("en")!.IsReadOnly);
            window.Show("sense-2");
            Completed(window.Loading);
            Assert.True(window.FieldFor("en")!.IsReadOnly);

            store.ApplyGate.SetResult();
            Assert.True(Completed(applying));
            Assert.False(window.FieldFor("en")!.IsReadOnly);
            Assert.Equal("Applied.", window.StatusText);
        });
    }

    [Fact]
    public void ApplyRetriesARefusedStagingAndWaitsWhileItIsStillRefused()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore { StageRefusal = "The pending changes are busy." };
            var window = Opened(store, out var closer);
            using var _ = closer;
            Type(window, "en", "a");

            Assert.False(Completed(window.ApplyAsync()));
            Assert.Equal(0, store.Applies);
            Assert.Contains("busy", window.StatusText, StringComparison.Ordinal);

            store.StageRefusal = null;
            Assert.True(Completed(window.ApplyAsync()));
            Assert.Equal("ax y", Assert.Single(store.Staged).RichString.Text);
        });
    }

    [Fact]
    public void ASecondParagraphIsNotStagedAndHoldsApply()
    {
        avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Opened(store, out var closer);
            using var _ = closer;
            window.FieldFor("en")!.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.SessionFor("en")!.EndSession();

            Assert.Empty(store.Staged);
            Assert.False(Completed(window.ApplyAsync()));
            Assert.Contains(SenseDefinitionPreviewWindow.OneParagraphMessage, window.StatusText, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("1", true, true)]
    [InlineData("1", false, false)]
    [InlineData(null, true, false)]
    public void PreviewIsRequestedOnlyByTheDeveloperFlagAndItsArgument(string? flag, bool argument, bool expected)
    {
        var args = argument ? new[] { SenseDefinitionPreviewWindow.StartupArgument } : [];
        Assert.Equal(expected, SenseDefinitionPreviewWindow.IsRequested(flag, args));
    }
}
