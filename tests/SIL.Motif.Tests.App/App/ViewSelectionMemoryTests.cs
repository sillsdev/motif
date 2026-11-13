using System.Linq;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the remembered selected row of a list view: its stable identity, its place in the preferences file, and its
/// return after leaving the view and after reopening the project.
/// Identities: Assess words use the word form; Warnings use the finding kind code; Review uses the change id.
/// </summary>
public sealed class ViewSelectionMemoryTests
{
    private const string Project = "/projects/one.fwdata";

    private static AssessmentWordResult Word(string word, string? writingSystem = null) =>
        new(word, "analysed", false, "Complete", 3, null) { WordWritingSystem = writingSystem };

    private static GrammarWarning Warning(string code) => new(
        GrammarDiagnosticLevel.Warning, "Entry", [new("entry", GrammarWarningPartRole.Text)],
        [new("is odd", GrammarWarningPartRole.Text)], "warning: " + code) { Group = code, Code = code };

    private static ProjectViewMemory Open(IUserPreferencesStore store)
    {
        var memory = new ProjectViewMemory(store);
        memory.OpenProject(Project);
        return memory;
    }

    private static AssessWordsViewModel Words(ProjectViewMemory memory)
    {
        var words = new AssessWordsViewModel();
        words.UseViewMemory(memory);
        words.SetProjectPath(Project);
        return words;
    }

    private static GrammarWarningsViewModel Warnings(ProjectViewMemory memory)
    {
        var warnings = new GrammarWarningsViewModel();
        warnings.UseViewMemory(memory);
        warnings.SetProjectPath(Project);
        return warnings;
    }

    [Fact]
    public void AssessWordsReturnToTheSelectedWordAfterReloadingAndAfterReopeningTheProject()
    {
        var store = new MemoryUserPreferencesStore();
        var words = Words(Open(store));
        words.Load([Word("kitabu"), Word("mtoto"), Word("rafiki")]);
        words.SelectedRow = words.Rows.Single(row => row.Word == "rafiki");

        words.Load([Word("kitabu"), Word("mtoto"), Word("rafiki")]);
        Assert.Equal("rafiki", words.SelectedRow?.Word);

        var reopened = Words(Open(store));
        reopened.Load([Word("kitabu"), Word("mtoto"), Word("rafiki")]);
        Assert.Equal("rafiki", reopened.SelectedRow?.Word);
    }

    [Fact]
    public void AssessWordsWithTheSameFormInTwoWritingSystemsReturnToTheOneSelected()
    {
        var store = new MemoryUserPreferencesStore();
        var words = Words(Open(store));
        words.Load([Word("kitabu", "swh"), Word("kitabu", "swh-fonipa"), Word("mtoto", "swh")]);
        words.SelectedRow = words.Rows[1];

        var reopened = Words(Open(store));
        reopened.Load([Word("kitabu", "swh"), Word("kitabu", "swh-fonipa"), Word("mtoto", "swh")]);
        Assert.Equal("swh-fonipa", reopened.SelectedRow?.Source.WordWritingSystem);
    }

    [Fact]
    public void AFormOnlyKeySavedEarlierStillSelectsTheWord()
    {
        var store = new MemoryUserPreferencesStore();
        store.SetViewState(ProjectPreferenceKey.For(null, Project), "AssessWords", new ViewState(null, false, null, "mtoto"));
        var words = Words(Open(store));
        words.Load([Word("kitabu", "swh"), Word("mtoto", "swh")]);
        Assert.Equal("mtoto", words.SelectedRow?.Word);
    }

    [Fact]
    public void ChangingTheFilterDoesNotReplaceTheRememberedAssessWord()
    {
        var store = new MemoryUserPreferencesStore();
        var words = Words(Open(store));
        words.Load([Word("kitabu"), Word("mtoto")]);
        words.SelectedRow = words.Rows[1];
        words.WordFilter = "kitabu";
        Assert.Equal("kitabu", words.SelectedRow?.Word);
        words.WordFilter = "";

        var reopened = Words(Open(store));
        reopened.Load([Word("kitabu"), Word("mtoto")]);
        Assert.Equal("mtoto", reopened.SelectedRow?.Word);
    }

    [Fact]
    public void AnAssessWordThatIsGoneSelectsNothingAndRaisesNothing()
    {
        var store = new MemoryUserPreferencesStore();
        var words = Words(Open(store));
        words.Load([Word("kitabu"), Word("mtoto")]);
        words.SelectedRow = words.Rows.Single(row => row.Word == "mtoto");

        var reopened = Words(Open(store));
        Assert.Null(Record.Exception(() => reopened.Load([Word("kitabu"), Word("rafiki")])));
        Assert.Null(reopened.SelectedRow);
        Assert.Equal(2, reopened.Rows.Count);
    }

    [Fact]
    public void WithNothingRememberedTheFirstAssessWordStaysTheDefault()
    {
        var words = Words(Open(new MemoryUserPreferencesStore()));
        words.Load([Word("kitabu"), Word("mtoto")]);
        Assert.Equal("kitabu", words.SelectedRow?.Word);
    }

    [Fact]
    public void WarningsReturnToTheSelectedFindingAfterReloadingAndAfterReopeningTheProject()
    {
        var store = new MemoryUserPreferencesStore();
        var warnings = Warnings(Open(store));
        warnings.Load([Warning("a.one"), Warning("b.two")]);
        warnings.SelectedTableEntry = warnings.TableEntries.OfType<GrammarWarningRowViewModel>()
            .Single(row => row.FindingIdentity == "b.two");

        var reopened = Warnings(Open(store));
        reopened.Load([Warning("a.one"), Warning("b.two")]);
        var selected = Assert.IsType<GrammarWarningRowViewModel>(reopened.SelectedTableEntry);
        Assert.Equal("b.two", selected.FindingIdentity);
    }

    [Fact]
    public void AFindingThatIsNoLongerReportedSelectsNothing()
    {
        var store = new MemoryUserPreferencesStore();
        var warnings = Warnings(Open(store));
        warnings.Load([Warning("a.one"), Warning("b.two")]);
        warnings.SelectedTableEntry = warnings.TableEntries.OfType<GrammarWarningRowViewModel>()
            .Single(row => row.FindingIdentity == "b.two");

        var reopened = Warnings(Open(store));
        Assert.Null(Record.Exception(() => reopened.Load([Warning("a.one")])));
        Assert.Null(reopened.SelectedTableEntry);
    }

    [Fact]
    public void ChangingTheSortKeepsTheRememberedSelectionAndTheSelectionKeepsTheSort()
    {
        var store = new MemoryUserPreferencesStore();
        var warnings = Warnings(Open(store));
        warnings.Load([Warning("a.one"), Warning("b.two")]);
        warnings.SelectedTableEntry = warnings.TableEntries.OfType<GrammarWarningRowViewModel>().First();
        warnings.MostYourWordsFirst = false;

        var key = ProjectPreferenceKey.For(null, Project);
        var state = store.GetViewState(key, "Warnings")!;
        Assert.Equal("ReportOrder", state.SortColumn);
        Assert.Equal("a.one", state.SelectedKey);
    }

    private const string OldFormatTwoFile =
        "{\"FormatVersion\":2,\"ZoomPercent\":100,\"Theme\":\"System\",\"LastSettingsGroup\":\"Display\",\"Window\":null," +
        "\"Trace\":\"Plain\",\"HasSeenTechDemoNotice\":false,\"ExpandedSections\":{}," +
        "\"ProjectViews\":{\"id:a\":{\"AssessWords\":{\"SortColumn\":null,\"SortDescending\":false,\"Filter\":\"Missed\"}}}}";

    [Fact]
    public void AFormatTwoFileWithNoSelectionStillLoadsAndIsWrittenAsTheLatestFormat()
    {
        var folder = Directory.CreateTempSubdirectory("motif-selection-").FullName;
        try
        {
            var path = Path.Combine(folder, "user-preferences.json");
            File.WriteAllText(path, OldFormatTwoFile);
            var store = new FileUserPreferencesStore(path);

            Assert.Equal(PreferenceState.Ready, store.State);
            Assert.Equal(new ViewState(null, false, "Missed"), store.GetViewState("id:a", "AssessWords"));
            Assert.Null(store.GetViewState("id:a", "AssessWords")!.SelectedKey);

            store.SetViewState("id:a", "AssessWords", new ViewState(null, false, "Missed", "mtoto"));
            var reread = new FileUserPreferencesStore(path);
            Assert.Equal(PreferenceState.Ready, reread.State);
            Assert.Equal(FileUserPreferencesStore.LatestFormatVersion, reread.Current.FormatVersion);
            Assert.Equal(new ViewState(null, false, "Missed", "mtoto"), reread.GetViewState("id:a", "AssessWords"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private const string FormatThreeFile =
        "{\"FormatVersion\":3,\"ZoomPercent\":125,\"Theme\":\"Dark\",\"LastSettingsGroup\":\"Display\",\"Window\":null," +
        "\"Trace\":\"Plain\",\"HasSeenTechDemoNotice\":true,\"ExpandedSections\":{}," +
        "\"ProjectViews\":{\"id:a\":{\"AssessWords\":{\"SortColumn\":null,\"SortDescending\":false,\"Filter\":\"Missed\",\"SelectedKey\":\"mtoto\"}}}}";

    [Fact]
    public void AFormatThreeFileReadsAsPolishAndIsRewrittenAsFormatFour()
    {
        var folder = Directory.CreateTempSubdirectory("motif-stage-").FullName;
        try
        {
            var path = Path.Combine(folder, "user-preferences.json");
            File.WriteAllText(path, FormatThreeFile);
            var store = new FileUserPreferencesStore(path);

            Assert.Equal(PreferenceState.Ready, store.State);
            Assert.Equal(MotifStage.Polish, store.Current.Stage);
            Assert.Equal(125, store.Current.ZoomPercent);
            Assert.Equal(FormatThreeFile, File.ReadAllText(path));

            store.Update(p => p with { Stage = MotifStage.Grow });

            using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                Assert.Equal(4, document.RootElement.GetProperty("FormatVersion").GetInt32());
            var reread = new FileUserPreferencesStore(path);
            Assert.Equal(PreferenceState.Ready, reread.State);
            Assert.Equal(MotifStage.Grow, reread.Current.Stage);
            Assert.Equal(125, reread.Current.ZoomPercent);
            Assert.Equal(new ViewState(null, false, "Missed", "mtoto"), reread.GetViewState("id:a", "AssessWords"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
