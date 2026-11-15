using System.Text.RegularExpressions;
using Avalonia.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WorkspaceRedesignContractTests
{
    private static readonly string[] Pages =
        ["Progress", "Texts", "Words", "GrammarGuide", "Proposals", "Diagnostics", "Settings"];

    [Fact]
    public void RegistryContainsExactlyTheApprovedPagesInOrder()
    {
        Assert.Equal(Pages, Enum.GetNames<WorkspacePage>());
        Assert.Equal(Pages, PageRegistry.Entries.Select(entry => entry.Page.ToString()));
        var diagnostics = Assert.Single(PageRegistry.Entries, entry => entry.IsDiagnostic);
        Assert.Equal("Diagnostics", diagnostics.Page.ToString());
        Assert.False(PageRegistry.Entries[^1].ShowInRail);
        Assert.DoesNotContain(PageRegistry.Entries, entry => entry.Page.ToString().Contains("Claude", StringComparison.Ordinal));
        Assert.DoesNotContain(PageRegistry.Entries, entry => entry.Title.Contains("Purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EachPageCanBeConstructedFromTheContextWithoutDuplicatingItsModel()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
            Type[] views = [typeof(ProgressPage), typeof(TextsPage), typeof(WordsPage), typeof(GrammarGuidePage),
                typeof(ProposalsPage), typeof(DiagnosticsPage), typeof(SettingsPage)];
            for (var index = 0; index < views.Length; index++)
            {
                var model = PageRegistry.Entries[index].CreateModel(context);
                Assert.NotNull(model.GetType().GetConstructor([typeof(WorkspaceContext)]));
                var view = Assert.IsAssignableFrom<Control>(Activator.CreateInstance(views[index], context));
                object? expected = model is SettingsPageModel settings ? settings.Settings : model;
                Assert.Same(expected, view.DataContext);
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DiagnosticsDefaultsOffPersistsAndReturnsToProgressWhenDisabled()
    {
        Assert.True(Enum.TryParse<WorkspacePage>("Progress", out var progress));
        Assert.True(Enum.TryParse<WorkspacePage>("Diagnostics", out var diagnostics));
        var preferences = new MemoryUserPreferencesStore();
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        context.UseDiagnosticsPreferences(preferences);

        Assert.False(context.Diagnostics);
        context.Diagnostics = true;
        Assert.True(preferences.Current.Diagnostics);
        context.OpenPage(diagnostics);
        Assert.Equal(diagnostics, context.CurrentPage);
        context.Diagnostics = false;
        Assert.False(preferences.Current.Diagnostics);
        Assert.Equal(progress, context.CurrentPage);
    }

    [Fact]
    public void SunsetPageAndPanelSourcesAreGone()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var app = Path.Combine(root.FullName, "src", "SIL.Motif.App");
        string[] names =
        [
            "OverviewPage", "TryAWordPage", "TimingPage", "WarningsPage", "ParsimonyPage", "AiHandoffPage",
            "AssistantConnectionsPage", "OverviewInspector", "ReviewPanel", "ReviewInspector", "Inspector",
            "TryWordPanel", "TryWordAttemptInspector", "WarningsFilterPanel", "ProposalBarPanel", "ProposalViewSwitch",
            "CompareWordPresentationHost", "HeatCell", "OverviewShareBar", "TextsListsPanel",
        ];
        foreach (var name in names)
        {
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(app, "Views"), name + ".axaml*", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(app, "Views"), name + ".cs", SearchOption.AllDirectories));
        }
        string[] models = ["OverviewPageModel", "TryWordPageModel", "TimingPageModel", "WarningsPageModel",
            "ParsimonyPageModel", "AiHandoffPageModel", "AssistantConnectionsPageModel", "ReviewPageModel",
            "TextsListsViewModel", "OverviewSlowWord", "PageDelta", "ProposalPageNote", "ProposalViewScope"];
        foreach (var model in models)
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(app, "ViewModels"), model + "*.cs"));
        foreach (var adapter in new[] { "CompareProposalMovedFilter", "CompareProposalMoves", "CompareProposalSide", "CompareProposalWords" })
            Assert.False(File.Exists(Path.Combine(app, "ViewModels", adapter + ".cs")));
        var declarations = names.Concat(models).Select(Regex.Escape);
        var typeDeclaration = new Regex(@"\b(?:class|record|struct)\s+(?:" + string.Join("|", declarations) + @")\b");
        foreach (var source in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
            Assert.DoesNotMatch(typeDeclaration, File.ReadAllText(source));
    }
}
