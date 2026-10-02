using System.Xml.Linq;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class InspectorIdentityBoundaryTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(null)]
    [InlineData("structural")]
    [InlineData("grammar-local")]
    [InlineData("authored")]
    public async Task TraceMorphLaunchPreservesIdentityLimitsAndCapturedDetails(string? quality)
    {
        using var project = new WalkthroughProject(pristine);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var morph = new TraceMorph(null, "recorded form", null, null, null, null, null, null, null, null)
        {
            FormId = project.Seed.FirstLexemeFormId.ToString("B").ToUpperInvariant(),
            MsaId = project.FirstMsaId.ToString("N"), IdentityQuality = quality, IdentityScope = "recorded grammar",
        };
        var chip = new TraceMorphViewModel(morph, allowLiveLink: false);
        var (context, inspector) = InspectorFor(project.FwDataPath);
        context.OpenInspector(chip.InspectSubject!, trace: new InspectorTrace("recorded word", captured.Value!.Token.BundleDigest),
            captured: [new InspectorDetail("Form", "recorded form")]);
        await inspector.Loading;
        Assert.Equal(quality ?? "unknown", context.Inspector!.Subject.IdentityQuality);
        Assert.Contains(inspector.TraceDetails, detail => detail.Value == "recorded form");
        Assert.Equal(quality == "authored", inspector.Facts.Count > 0);
        Assert.Equal(quality == "authored", inspector.Facts.Any(row => row.Link is not null));
        if (quality != "authored") Assert.Contains("without a FieldWorks identity", inspector.FactsNote);
    }

    [Fact]
    public async Task TraceOpenedThroughInspectorRetainsFactsButCannotLinkToAReplacedProject()
    {
        using var project = new WalkthroughProject(pristine);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var saved = XDocument.Load(project.FwDataPath);
        saved.Root!.Elements("rt").Single(item => (string?)item.Attribute("class") == "LangProject")
            .SetAttributeValue("guid", Guid.NewGuid());
        saved.Save(project.FwDataPath);
        var morph = new TraceMorph(null, "recorded form", null, null, null, null, null, null, null, null)
            { FormId = project.Seed.FirstLexemeFormId.ToString("D"), IdentityQuality = "authored" };
        var (context, inspector) = InspectorFor(project.FwDataPath);
        context.OpenInspector(new TraceMorphViewModel(morph, allowLiveLink: false).InspectSubject!,
            trace: new InspectorTrace("recorded word", captured.Value!.Token.BundleDigest),
            captured: [new InspectorDetail("Form", "recorded form")]);
        await inspector.Loading;
        Assert.NotEmpty(inspector.Facts);
        Assert.All(inspector.Facts, row => Assert.Null(row.Link));
        Assert.Contains(inspector.TraceDetails, detail => detail.Value == "recorded form");
    }

    private static (WorkspaceContext, InspectorViewModel) InspectorFor(string path)
    {
        var client = new FakeCommandClient();
        client.OnInspect((request, _) => Task.FromResult(InspectQuery.Query(request)));
        var selection = new SelectionViewModel(client);
        var context = new WorkspaceContext(selection, new AssessViewModel(client, selection), new ChangesViewModel(client),
            client, new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(client)) { ProjectPath = path };
        return (context, new InspectorViewModel(context, _ => "Try a Word"));
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(DragDropEffects.None);
    }
}
