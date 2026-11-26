using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Commands.Queries;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Issue 484: a Guide body on a project with no styles (one generated and never opened in FieldWorks) stages the FieldWorks
/// standard styles it uses in the same Draft, instead of refusing with <c>text.style-unknown</c>.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrStandardStylesTests(PristineProjectFixture pristine)
{
    private const string Body = "### How it works\n\nA paragraph.\n\n- one\n- two";

    [Fact]
    public async Task AddWithAHeadingAndParagraphOnAProjectWithNoStylesStagesTheStylesAndDryRunFinalizeAndApplyLeaveThemPresent()
    {
        var path = Project(styles: false);
        Assert.Empty(StyleNames(path));

        var added = GdrAddCommands.Add(new GdrAddRequest(path, "Verbs", Markdown: Body, WritingSystem: "en"));

        Assert.True(added.Succeeded, added.Refusal?.Code + ": " + added.Refusal?.Message);
        Assert.Equal(["Normal", "Heading 3", "Bulleted List"], added.Value!.StylesCreated);
        Assert.Empty(StyleNames(path));

        var version = MotifProductVersion.CurrentText;
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, version, PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(path, version, proposalId));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(RootFor(path)));
        try
        {
            runner.Start(path);
            await runner.WhenIdleAsync();
            var bound = JobCommands.WaitForDryRun(new WaitForDryRunRequest(path, version, proposalId, queued.Value!.JobId, TimeSpan.FromMinutes(2)));
            Assert.True(bound.Succeeded, bound.Refusal?.Message);
            var applied = PendingChangesWorkflow.Apply(new ApplyRequest(path, version, proposalId));
            Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
        }
        finally
        {
            await runner.DisposeAsync();
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

        Assert.Equal(["Bulleted List", "Heading 3", "Normal"], StyleNames(path).Order(StringComparer.Ordinal));
        AssertFieldWorksDefaults(path);
        var shown = GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value!;
        Assert.Contains(shown.Discussion, paragraph => paragraph.Style == "Heading 3");
    }

    [Fact]
    public void ABodyOnAProjectThatHasTheStylesStagesNoStyleOperation()
    {
        var path = Project(styles: true);
        var before = StyleNames(path);

        var added = GdrAddCommands.Add(new GdrAddRequest(path, "Verbs", Markdown: Body, WritingSystem: "en"));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Empty(added.Value!.StylesCreated);
        // The section and its Discussion: nothing for the styles.
        Assert.Equal(2, added.Value.OperationCount);
        Assert.Equal(before.Order(StringComparer.Ordinal), StyleNames(path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ABodyThatUsesOnlyAMissingStyleStagesOnlyThatStyleAndItsBase()
    {
        var path = Project(styles: true, keep: ["Normal"]);

        var added = GdrAddCommands.Add(new GdrAddRequest(path, "Verbs", Markdown: "### How it works\n\ntext", WritingSystem: "en"));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Equal(["Heading 3"], added.Value!.StylesCreated);
        Assert.Equal(3, added.Value.OperationCount);
    }

    [Fact]
    public void AStyleThatIsNotAStandardStyleStillRefusesAndStagesNoStyle()
    {
        var path = Project(styles: false);

        var added = GdrAddCommands.Add(new GdrAddRequest(path, "Verbs", Markdown: "A [styled]{custom-style=Invented} word.", WritingSystem: "en"));

        Assert.False(added.Succeeded);
        Assert.Contains("Invented", added.Refusal!.Message, StringComparison.Ordinal);
        Assert.Empty(StyleNames(path));
    }

    [Fact]
    public void EditOnAProjectWithNoStylesStagesTheStylesAndAppliesThem()
    {
        var path = Project(styles: true);
        Assert.True(GdrAddCommands.Add(new GdrAddRequest(path, "Verbs", WritingSystem: "en")).Succeeded);
        Applied(path);
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => cache.LangProject.StylesOC.Clear());
            new FwDataProjectLoader().Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        Assert.Empty(StyleNames(path));

        var edited = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", Markdown: Body));

        Assert.True(edited.Succeeded, edited.Refusal?.Code + ": " + edited.Refusal?.Message);
        Assert.Equal(["Normal", "Heading 3", "Bulleted List"], edited.Value!.StylesCreated);
        Applied(path);
        Assert.Equal(["Bulleted List", "Heading 3", "Normal"], StyleNames(path).Order(StringComparer.Ordinal));
    }

    // FlexStyles.xml: a heading is based on and followed by Normal; Normal and a list follow themselves.
    private static void AssertFieldWorksDefaults(string path)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        IStStyle Style(string name) => cache.LangProject.StylesOC.Single(style => style.Name == name);
        var normal = Style("Normal");
        var heading = Style("Heading 3");
        var bullets = Style("Bulleted List");
        Assert.Equal(StyleType.kstParagraph, heading.Type);
        Assert.Equal(ContextValues.General, heading.Context);
        Assert.Equal(StructureValues.Undefined, heading.Structure);
        Assert.Equal(FunctionValues.Prose, heading.Function);
        Assert.True(heading.IsBuiltIn);
        Assert.False(heading.IsModified);
        Assert.Equal(new Guid("366E9470-26F7-4BDF-86E4-F829784C3113"), heading.Guid);
        Assert.Same(normal, heading.BasedOnRA);
        Assert.Same(normal, heading.NextRA);
        Assert.Null(normal.BasedOnRA);
        Assert.Same(normal, normal.NextRA);
        Assert.Same(bullets, bullets.NextRA);
        Assert.Equal(10000, normal.Rules.GetIntPropValues((int)FwTextPropType.ktptFontSize, out _));
        Assert.Equal(6000, normal.Rules.GetIntPropValues((int)FwTextPropType.ktptSpaceAfter, out _));
        Assert.All(cache.LangProject.StylesOC, style => Assert.Same(cache.LangProject, style.Owner));
    }

    private static void Applied(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(RootFor(path)));
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
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
    }

    private static string[] StyleNames(string path)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        return cache.LangProject.StylesOC.Select(style => style.Name).ToArray();
    }

    // A generated project: no styles, or the three the body uses, or only those named in keep.
    private string Project(bool styles, string[]? keep = null)
    {
        using var cache = pristine.NewScratch();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.StylesOC.Clear();
            if (!styles) return;
            foreach (var name in keep ?? ["Normal", "Heading 3", "Bulleted List"])
            {
                var style = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
                cache.LangProject.StylesOC.Add(style);
                style.Name = name;
                style.Type = StyleType.kstParagraph;
            }
        });
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "gdr-styles-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
