using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GdrPreviewWindowTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public GdrPreviewWindowTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    private static GdrTextProperties Props(int? italic = null, string? style = null) => new(
        italic is { } value ? new Dictionary<int, GdrIntProperty> { [TextPropertyNumbers.Italic] = new(0, value) } : new Dictionary<int, GdrIntProperty>(),
        style is null ? new Dictionary<int, string>() : new Dictionary<int, string> { [TextPropertyNumbers.NamedStyle] = style });

    private static readonly GdrSummary First = new("id-1", "GDR-1", "Word boundaries", "Where one word ends.");
    private static readonly GdrSummary Second = new("id-2", "GDR-2", "Spelling variants", "Which spelling wins.");

    private static GdrShowResponse Canned(GdrSummary record) => new(record,
    [
        new GdrParagraph("Normal", false, "en", Props(style: "Normal"),
        [
            new GdrRun("Words end at ", "en", null, Props()),
            new GdrRun("spaces", "en", "Emphasis", Props(style: "Emphasis")),
        ], "para-1"),
        new GdrParagraph("Normal", true, "ar", Props(style: "Normal"), [new GdrRun("كتاب", "ar", null, Props())], "para-2"),
    ])
    {
        DefaultVernacular = "ar",
        DefaultAnalysis = "en",
        Styles =
        [
            new GdrStyle("Emphasis", GdrStyleKind.Character,
                new Dictionary<string, GdrTextProperties> { ["en"] = Props(italic: 1), ["ar"] = Props(italic: 1) }),
            new GdrStyle("Normal", GdrStyleKind.Paragraph,
                new Dictionary<string, GdrTextProperties> { ["en"] = Props(), ["ar"] = Props() }),
        ],
        WritingSystems = [new GdrWritingSystem("ar", "rtl", "Segoe UI"), new GdrWritingSystem("en", "ltr", "Segoe UI")],
    };

    [Fact]
    public void SelectingARecordShowsItsDiscussionEditableWithTheProjectsStylesAndWritingSystems()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second));
            using var closer = window.CloseOnExit();
            window.Show();
            Assert.Null(window.Field);

            window.Show(First.Id);

            var field = window.Field!;
            Assert.False(field.IsReadOnly);
            Assert.Equal(["para-1", "para-2"], field.Editor.Document.Paragraphs.Select(paragraph => paragraph.Key));
            Assert.StartsWith("GDR-1  Word boundaries", window.HeadingText, StringComparison.Ordinal);
            var paragraphs = field.Editor.Document.Paragraphs;
            Assert.Equal(2, paragraphs.Count);
            Assert.False(paragraphs[0].Rtl);
            Assert.True(paragraphs[1].Rtl);
            Assert.Equal("ar", paragraphs[1].Runs[0].Ws);
            Assert.Equal("Emphasis", paragraphs[0].Runs[1].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            Assert.True(field.WritingSystems.Get("ar").Rtl);
            Assert.False(field.WritingSystems.Get("en").Rtl);
            var resolved = field.StyleResolver!.Resolve("Emphasis", "en");
            Assert.NotNull(resolved);
            Assert.Equal(1, resolved.IntProps[TextPropertyNumbers.Italic].Value);
            Assert.Null(field.StyleResolver.Resolve("Absent", "en"));
        });
    }

    private static GdrPreviewWindow Opened(out IDisposable closer)
    {
        var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second));
        closer = window.CloseOnExit();
        window.Show();
        window.Show(First.Id);
        window.Field!.Focus();
        return window;
    }

    private static void Leave(GdrPreviewWindow window) =>
        Assert.True(window.GetLogicalDescendants().OfType<ListBox>().Single().ContainerFromIndex(1)!.Focus());

    private static void Press(GdrPreviewWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    [Fact]
    public void TypingThenLeavingTheFieldMakesOnePendingEditThatRemembersItsStartingText()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            var raised = new List<GdrDiscussionEdit?>();
            window.PendingEditChanged += (_, edit) => raised.Add(edit);

            window.KeyTextInput("xyz");
            Assert.Empty(window.PendingEdits);
            Leave(window);

            var edit = Assert.Single(window.PendingEdits).Value;
            Assert.Equal(First.Id, edit.RecordId);
            Assert.Equal(["para-1", "para-2"], edit.Order);
            var changed = Assert.Single(edit.Changed);
            Assert.Equal((0, "para-1", "xyzWords end at spaces"), (changed.Index, changed.Key, changed.Text));
            Assert.Equal("Normal", changed.Content.Style);
            Assert.Empty(edit.Removed);
            Assert.Equal("Words end at spaces", Assert.Single(edit.Starting).Value);
            Assert.Same(edit, Assert.Single(raised));
        });
    }

    [Fact]
    public void EnterMakesANewParagraphAndBackspaceJoinsOneAway()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            var editor = window.Field!.Editor;

            editor.SetCaret(new TextPosition(0, "Words end".Length));
            Press(window, Key.Enter);
            window.KeyTextInput("new");
            editor.SetCaret(new TextPosition(2, 0));
            Press(window, Key.Back);
            Leave(window);

            var edit = Assert.Single(window.PendingEdits).Value;
            Assert.Equal(["para-1", null], edit.Order);
            Assert.Equal(["Words end", "new at spacesكتاب"], editor.Document.Paragraphs.Select(paragraph => paragraph.Text));
            Assert.Collection(edit.Changed,
                kept => Assert.Equal((0, "para-1", "Words end"), (kept.Index, kept.Key, kept.Text)),
                made => Assert.Equal((1, (string?)null, "new at spacesكتاب"), (made.Index, made.Key, made.Text)));
            Assert.Equal(["para-2"], edit.Removed);
            Assert.Equal("كتاب", edit.Starting["para-2"]);
        });
    }

    [Fact]
    public void CtrlZUndoesATypingStepAndCtrlYRedoesIt()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            var paragraphs = window.Field!.Editor.Document.Paragraphs;

            window.KeyTextInput("xyz");
            Press(window, Key.Z, RawInputModifiers.Control);

            Assert.Equal("Words end at spaces", paragraphs[0].Text);
            Assert.Equal((0, 1), (window.Session!.UndoSteps, window.Session.RedoSteps));

            Press(window, Key.Y, RawInputModifiers.Control);

            Assert.Equal("xyzWords end at spaces", paragraphs[0].Text);
            Assert.Equal((1, 0), (window.Session.UndoSteps, window.Session.RedoSteps));
        });
    }

    [Fact]
    public void UndoingBackToTheStartingTextLeavesNothingPending()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            var raised = new List<GdrDiscussionEdit?>();
            window.PendingEditChanged += (_, edit) => raised.Add(edit);
            window.KeyTextInput("xyz");
            Leave(window);
            window.Field!.Focus();

            Press(window, Key.Z, RawInputModifiers.Control);
            Leave(window);

            Assert.Empty(window.PendingEdits);
            Assert.Equal(2, raised.Count);
            Assert.Null(raised[1]);
        });
    }

    [Fact]
    public void ALaterSessionUpdatesThePendingEditAndKeepsTheFirstStartingText()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            window.KeyTextInput("a");
            Leave(window);

            window.Field!.Focus();
            window.Field.Editor.SetCaret(new TextPosition(0, 1));
            window.KeyTextInput("b");
            Leave(window);

            var edit = Assert.Single(window.PendingEdits).Value;
            Assert.Equal("abWords end at spaces", Assert.Single(edit.Changed).Text);
            Assert.Equal("Words end at spaces", edit.Starting["para-1"]);
        });
    }

    [Fact]
    public void AnEditStaysWithItsRecordWhenAnotherIsShownAndBack()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            window.KeyTextInput("a");
            window.Show(Second.Id);
            window.Show(First.Id);

            Assert.Equal("aWords end at spaces", window.Field!.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(First.Id, Assert.Single(window.PendingEdits).Key);
        });
    }

    [Fact]
    public void AfterApplyTheFieldsUndoIsEmptyAndTheAppliedTextIsTheNewStart()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            window.KeyTextInput("xyz");
            Leave(window);

            window.Session!.Applied();
            window.Field!.Focus();
            Press(window, Key.Z, RawInputModifiers.Control);

            Assert.Equal("xyzWords end at spaces", window.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(0, window.Session.UndoSteps);
            Assert.Empty(window.PendingEdits);
            window.KeyTextInput("q");
            Leave(window);
            Assert.Equal("xyzWords end at spaces", Assert.Single(window.PendingEdits).Value.Starting["para-1"]);
        });
    }

    [Fact]
    public void APauseInTheFieldEndsTheSession()
    {
        _avalonia.Invoke(() =>
        {
            var window = Opened(out var closer);
            using var _ = closer;
            var session = new GdrDiscussionSession(window.Field!, First.Id, TimeSpan.FromMilliseconds(1));

            window.KeyTextInput("a");
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (session.Pending is null && waited.Elapsed < TimeSpan.FromSeconds(10))
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.NotNull(session.Pending);
            Assert.True(window.Field!.IsFocused);
        });
    }

    [Fact]
    public void ChoosingAnotherRecordReplacesTheDiscussion()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second));
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Show(Second.Id);

            Assert.StartsWith("GDR-2  Spelling variants", window.HeadingText, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("1", true, true)]
    [InlineData("1", false, false)]
    [InlineData(null, true, false)]
    [InlineData("0", true, false)]
    public void PreviewIsRequestedOnlyByTheDeveloperFlagAndItsArgument(string? flag, bool argument, bool expected)
    {
        var args = argument ? new[] { GdrPreviewWindow.StartupArgument } : [];
        Assert.Equal(expected, GdrPreviewWindow.IsRequested(flag, args));
    }

    [Fact]
    public void PreviewWindowIsNotOpenedWithoutTheGate()
    {
        _avalonia.Invoke(() => Assert.Null(GdrPreviewWindow.ShowIfRequested([])));
    }

    private sealed class FakeStore(GdrApplyRefusal? applyRefusal = null) : IGdrEditStore
    {
        public List<(string RecordId, TextParagraphsPayload Paragraphs)> Staged { get; } = [];

        public List<string> Discards { get; } = [];

        public Dictionary<string, TextParagraphsPayload> Pending { get; } = [];

        public string? StageRefusal { get; set; }

        public TaskCompletionSource? ApplyGate { get; set; }

        public TaskCompletionSource? PendingGate { get; set; }

        public string? PendingRefusal { get; set; }

        public int Applies { get; private set; }

        public Task<string?> StageAsync(string recordId, TextParagraphsPayload paragraphs)
        {
            if (StageRefusal is { } refusal) return Task.FromResult<string?>(refusal);
            Staged.Add((recordId, paragraphs));
            return Task.FromResult<string?>(null);
        }

        public Task<string?> DiscardAsync(string recordId)
        {
            Discards.Add(recordId);
            return Task.FromResult<string?>(null);
        }

        public async Task<GdrPendingRead> PendingAsync(string recordId)
        {
            if (PendingGate is { } gate) await gate.Task;
            return new GdrPendingRead(PendingRefusal is null ? Pending.GetValueOrDefault(recordId) : null, PendingRefusal);
        }

        public async Task<GdrApplyRefusal?> ApplyAsync()
        {
            Applies++;
            if (ApplyGate is { } gate) await gate.Task;
            return applyRefusal;
        }
    }

    private static void Completed(Task task)
    {
        while (!task.IsCompleted) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        task.GetAwaiter().GetResult();
    }

    private static GdrShowResponse Split(GdrSummary record)
    {
        var canned = Canned(record);
        return canned with
        {
            Discussion = [canned.Discussion[0] with { Runs = [new GdrRun("Words end", "en", null, Props())] },
                canned.Discussion[0] with { Runs = [new GdrRun(" at spaces", "en", null, Props())], Key = "para-new" },
                canned.Discussion[1]],
        };
    }

    private static T Completed<T>(Task<T> task)
    {
        while (!task.IsCompleted) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return task.GetAwaiter().GetResult();
    }

    [Fact]
    public void LeavingTheFieldStagesTheEditAsOneParagraphsPayloadOfTheDiscussion()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();

            window.KeyTextInput("xyz");
            Leave(window);

            var (recordId, paragraphs) = Assert.Single(store.Staged);
            Assert.Equal(First.Id, recordId);
            Assert.Equal(GdrDiscussionPayload.Field, paragraphs.Field);
            Assert.Equal(["para-1", "para-2"], paragraphs.Order);
            var changed = Assert.Single(paragraphs.Changed);
            Assert.Equal((0, "para-1", "Normal"), (changed.Index, changed.Key, changed.Style));
            Assert.Equal([("xyzWords end at ", "en", (string?)null), ("spaces", "en", "Emphasis")],
                changed.Runs.Select(run => (run.Text, run.Ws, run.CharacterStyle)));
            Assert.Equal(new TextParagraphExpected("Words end at spaces", "Normal", TextRunsDigest.Of(Canned(First).Discussion[0])),
                paragraphs.Expected["para-1"]);
        });
    }

    [Fact]
    public void EditingBackToTheStartTakesTheEditOutOfTheDraft()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            window.KeyTextInput("a");
            Leave(window);
            window.Field.Focus();

            Press(window, Key.Z, RawInputModifiers.Control);
            Leave(window);

            Assert.Single(store.Staged);
            Assert.Equal([First.Id], store.Discards);
        });
    }

    [Fact]
    public void ApplyGivesANewParagraphItsProjectKeyAndStartsTheRecordAgainFromTheAppliedText()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var applied = false;
            var window = new GdrPreviewWindow([First, Second],
                id => applied ? Split(First) : Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            window.Field.Editor.SetCaret(new TextPosition(0, "Words end".Length));
            Press(window, Key.Enter);

            applied = true;
            Assert.True(Completed(window.ApplyAsync()));

            Assert.Equal(1, store.Applies);
            Assert.Equal(["para-1", null, "para-2"], Assert.Single(store.Staged).Paragraphs.Order);
            Assert.Equal(["para-1", "para-new", "para-2"], window.Field.Editor.Document.Paragraphs.Select(paragraph => paragraph.Key));
            Assert.Equal(0, window.Session!.UndoSteps);
            Assert.Empty(window.PendingEdits);
            Assert.Equal("Applied.", window.StatusText);

            window.Field.Focus();
            window.Field.Editor.SetCaret(new TextPosition(1, 0));
            window.KeyTextInput("q");
            Leave(window);
            var next = store.Staged[^1].Paragraphs;
            Assert.Equal(["para-1", "para-new", "para-2"], next.Order);
            Assert.Equal(" at spaces", next.Expected["para-new"].Text);
        });
    }

    [Fact]
    public void ARefusedApplyShowsWhyAndKeepsTheEditPending()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore(new GdrApplyRefusal("The project is busy."));
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            window.KeyTextInput("a");

            Assert.False(Completed(window.ApplyAsync()));

            Assert.Equal("The project is busy.", window.StatusText);
            Assert.False(window.IsStaleStripShown);
            Assert.Single(window.PendingEdits);
            Assert.Equal(1, window.Session!.UndoSteps);
        });
    }

    private static GdrShowResponse ChangedInFieldWorks(GdrSummary record)
    {
        var canned = Canned(record);
        return canned with
        {
            Discussion = [canned.Discussion[0] with { Runs = [new GdrRun("Changed in FieldWorks", "en", null, Props())] },
                canned.Discussion[1]],
        };
    }

    [Fact]
    public void AnApplyRefusedForDriftKeepsTheTypingAndShowsTheStripOnThatRecordOnly()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore(new GdrApplyRefusal("stale", First.Id));
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            Assert.False(window.IsStaleStripShown);
            window.Field!.Focus();
            window.KeyTextInput("a");

            Assert.False(Completed(window.ApplyAsync()));

            Assert.True(window.IsStaleStripShown);
            Assert.Equal("Nothing was applied.", window.StatusText);
            Assert.StartsWith("aWords", window.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Single(window.PendingEdits);
            window.Show(Second.Id);
            Assert.False(window.IsStaleStripShown);
            window.Show(First.Id);
            Assert.True(window.IsStaleStripShown);
        });
    }

    [Fact]
    public void StartingAgainFromFieldWorksAsksFirstThenDropsTheEditAndShowsTheFieldWorksText()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore(new GdrApplyRefusal("stale", First.Id));
            var confirm = false;
            var refused = false;
            var window = new GdrPreviewWindow([First, Second],
                id => refused && id == First.Id ? ChangedInFieldWorks(First) : Canned(id == First.Id ? First : Second), store,
                () => Task.FromResult(confirm));
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            window.KeyTextInput("a");
            Assert.False(Completed(window.ApplyAsync()));
            refused = true;

            Assert.False(Completed(window.StartAgainFromFieldWorksAsync()));
            Assert.True(window.IsStaleStripShown);
            Assert.Single(window.PendingEdits);

            confirm = true;
            Assert.True(Completed(window.StartAgainFromFieldWorksAsync()));

            Assert.Equal([First.Id], store.Discards);
            Assert.False(window.IsStaleStripShown);
            Assert.Empty(window.PendingEdits);
            Assert.Equal("Changed in FieldWorks", window.Field!.Editor.Document.Paragraphs[0].Text);

            window.Field.Focus();
            window.KeyTextInput("b");
            Leave(window);
            Assert.Equal("Changed in FieldWorks", store.Staged[^1].Paragraphs.Expected["para-1"].Text);
        });
    }

    private static GdrShowResponse WithLinks() => Canned(First) with
    {
        Discussion =
        [
            new GdrParagraph("Normal", false, "en", Props(style: "Normal"),
            [
                new GdrRun("see ", "en", null, Props()),
                new GdrRun("entry-1", "en", null, Props(italic: 1), "link-1", new GdrLinkTarget(GdrLinkState.Resolved, "Lexical entry", "entry-1", "gloss-1")),
                new GdrRun(" and ", "en", null, Props()),
                new GdrRun("entry-2", "en", null, Props(), "link-2", new GdrLinkTarget(GdrLinkState.Broken, null, null, "")),
                new GdrRun(" and ", "en", null, Props()),
                new GdrRun("site", "en", null, Props(), "link-3", new GdrLinkTarget(GdrLinkState.External, null, "https://example.invalid/x", "")),
            ]),
        ],
    };

    private static ContextInfo At(GdrPreviewWindow window, int offset) =>
        window.Field!.Editor.DescribeContext(new TextPosition(0, offset), false);

    [Fact]
    public void ABrokenLinkShowsAsBrokenInTheTextAndInTheFieldsAccessibleValue()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First], _ => WithLinks());
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);

            var runs = window.Field!.Editor.Document.Paragraphs[0].Runs;
            var broken = runs.Single(run => run.Text == "entry-2");
            Assert.True(broken.Props.TryGetInt(TextPropertyNumbers.ForeColor, out _));
            Assert.Contains(runs, run => run.Text == GdrDiscussion.BrokenMarker);
            var value = ((IValueProvider)ControlAutomationPeer.CreatePeerForElement(window.Field)).Value;
            Assert.Contains("entry-2" + GdrDiscussion.BrokenMarker, value, StringComparison.Ordinal);
            Assert.DoesNotContain("entry-1" + GdrDiscussion.BrokenMarker, value, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ActivatingALinkShowsWhatItPointsTo()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First], _ => WithLinks());
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);

            Assert.True(window.ShowTarget(At(window, 5)));
            Assert.Equal("Lexical entry: entry-1 (gloss-1)", window.TargetText);

            Assert.True(window.ShowTarget(At(window, 17)));
            Assert.Equal("'entry-2' is a broken link: its target is not in this project.", window.TargetText);

            Assert.True(window.ShowTarget(At(window, window.Field!.Editor.Document.Paragraphs[0].Text.Length - 1)));
            Assert.Equal("https://example.invalid/x", window.TargetText);
        });
    }

    [Fact]
    public void ActivatingPlainTextShowsNothingAndChoosingARecordClearsTheTarget()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First], _ => WithLinks());
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            Assert.True(window.ShowTarget(At(window, 5)));

            Assert.False(window.ShowTarget(At(window, 1)));
            Assert.NotEqual("", window.TargetText);
            window.Show(First.Id);

            Assert.Equal("", window.TargetText);
        });
    }

    [Fact]
    public void EditingAParagraphWithABrokenLinkStagesTheProjectTextWithoutTheBrokenMarker()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var linked = WithLinks();
            var keyed = linked with { Discussion = [linked.Discussion[0] with { Key = "para-1", RightToLeft = true }] };
            var window = new GdrPreviewWindow([First, Second], _ => keyed, store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();

            window.KeyTextInput("x");
            Leave(window);

            var paragraphs = Assert.Single(store.Staged).Paragraphs;
            Assert.Equal(new TextParagraphExpected("see entry-1 and entry-2 and site", "Normal", TextRunsDigest.Of(keyed.Discussion[0])),
                paragraphs.Expected["para-1"]);
            var changed = Assert.Single(paragraphs.Changed);
            Assert.Equal("xsee entry-1 and entry-2 and site", string.Concat(changed.Runs.Select(run => run.Text)));
            Assert.Equal((0, 1), changed.Props!.Ints[128]);
            var link = changed.Runs.Single(run => run.Text == "entry-1");
            Assert.Equal("link-1", link.ObjData);
            Assert.Equal([TextPropertyNumbers.Italic], link.Props!.Ints.Keys);
            var broken = changed.Runs.Single(run => run.Text == "entry-2");
            Assert.Equal("link-2", broken.ObjData);
            Assert.Empty(broken.Props!.Ints);
            Assert.Equal(["link-1", "link-2", "link-3"], changed.Runs.Where(run => run.ObjData is not null).Select(run => run.ObjData));
        });
    }

    [Fact]
    public void StartingAgainAfterAnEnterAddedAParagraphTakesTheEditOutOfTheDraft()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore(new GdrApplyRefusal("stale", First.Id));
            var refused = false;
            var window = new GdrPreviewWindow([First, Second],
                id => refused && id == First.Id ? ChangedInFieldWorks(First) : Canned(id == First.Id ? First : Second), store,
                () => Task.FromResult(true));
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            window.Field.Editor.SetCaret(new TextPosition(0, "Words end".Length));
            Press(window, Key.Enter);
            Assert.False(Completed(window.ApplyAsync()));
            Assert.Equal(["para-1", null, "para-2"], store.Staged[^1].Paragraphs.Order);
            refused = true;

            Assert.True(Completed(window.StartAgainFromFieldWorksAsync()));

            Assert.Equal([First.Id], store.Discards);
            Assert.Equal(["Changed in FieldWorks", "كتاب"], window.Field!.Editor.Document.Paragraphs.Select(paragraph => paragraph.Text));
            Assert.Empty(window.PendingEdits);
        });
    }

    [Fact]
    public void EveryFieldIsReadOnlyWhileApplyRuns()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore { ApplyGate = new TaskCompletionSource() };
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            window.KeyTextInput("a");

            var applying = window.ApplyAsync();
            while (store.Applies == 0) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(window.Field.IsReadOnly);
            window.KeyTextInput("b");
            Assert.StartsWith("aWords", window.Field.Editor.Document.Paragraphs[0].Text);
            store.ApplyGate.SetResult();
            Assert.True(Completed(applying));
            Assert.False(window.Field.IsReadOnly);
        });
    }

    [Fact]
    public void OpeningARecordShowsTheEditTheDraftHoldsAndALaterSessionKeepsWhatItBeganFrom()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var began = new TextParagraphExpected("Words end at spaces", "Normal", "runs-when-the-edit-began");
            store.Pending[First.Id] = new TextParagraphsPayload(GdrDiscussionPayload.Field, ["para-1", null, "para-2"],
            [
                new TextParagraphChange(0, "para-1", "Normal", [new TextRun("Words end", "en")]),
                new TextParagraphChange(1, null, "Normal", [new TextRun("New", "en")]),
            ], [], new Dictionary<string, TextParagraphExpected> { ["para-1"] = began });
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            Completed(window.Loading);

            Assert.Equal(["Words end", "New", "كتاب"], window.Field!.Editor.Document.Paragraphs.Select(paragraph => paragraph.Text));
            Assert.True(window.IsUnappliedStripShown);
            Assert.Single(window.PendingEdits);

            window.Field.Focus();
            window.Field.Editor.SetCaret(new TextPosition(1, 3));
            window.KeyTextInput("er");
            Leave(window);

            var staged = store.Staged[^1].Paragraphs;
            Assert.Equal(["para-1", null, "para-2"], staged.Order);
            Assert.Equal(began, staged.Expected["para-1"]);
            Assert.Equal("Newer", string.Concat(staged.Changed.Single(change => change.Key is null).Runs.Select(run => run.Text)));
        });
    }

    [Fact]
    public void ApplyWaitsWhileARecordsLatestEditCouldNotBeStaged()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore { StageRefusal = "The pending changes are busy." };
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            window.KeyTextInput("a");
            Leave(window);

            Assert.False(Completed(window.ApplyAsync()));

            Assert.Equal(0, store.Applies);
            Assert.Contains("The pending changes are busy.", window.StatusText, StringComparison.Ordinal);

            store.StageRefusal = null;
            Assert.True(Completed(window.ApplyAsync()));
            Assert.Equal(1, store.Applies);
            Assert.StartsWith("aWords", Assert.Single(store.Staged).Paragraphs.Changed[0].Runs[0].Text);
        });
    }

    [Fact]
    public void TypingBeforeTheDraftHasBeenReadIsHeldBackAndTheDraftsEditIsShown()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore { PendingGate = new TaskCompletionSource() };
            store.Pending[First.Id] = new TextParagraphsPayload(GdrDiscussionPayload.Field, ["para-1", "para-2"],
                [new TextParagraphChange(0, "para-1", "Normal", [new TextRun("Kept", "en")])], [],
                new Dictionary<string, TextParagraphExpected> { ["para-1"] = new("Words end at spaces", "Normal", "digest") });
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();

            window.KeyTextInput("lost");
            Assert.True(window.Field.IsReadOnly);
            Assert.Equal("Words end at spaces", window.Field.Editor.Document.Paragraphs[0].Text);
            store.PendingGate.SetResult();
            Completed(window.Loading);

            Assert.False(window.Field!.IsReadOnly);
            Assert.Equal("Kept", window.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Empty(store.Staged);
        });
    }

    [Fact]
    public void ARecordWhoseDraftEditCannotBeReadStaysReadOnlyAndSaysWhy()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore { PendingRefusal = "The project store is busy." };
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second), store);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            Completed(window.Loading);

            Assert.True(window.Field!.IsReadOnly);
            Assert.Contains("The project store is busy.", window.StatusText, StringComparison.Ordinal);
        });
    }
}
