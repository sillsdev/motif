using System.Xml.Linq;
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
    public void RecordedMorphIdentityOnlyResolvesAuthoredFieldWorksFacts(string? quality)
    {
        using var project = new WalkthroughProject(pristine);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var morph = new TraceMorph(null, "recorded form", null, null, null, null, null, null, null, null)
        {
            FormId = project.Seed.FirstLexemeFormId.ToString("B").ToUpperInvariant(),
            MsaId = project.FirstMsaId.ToString("N"), IdentityQuality = quality, IdentityScope = "recorded grammar",
        };
        var recorded = new TraceMorphViewModel(morph, allowLiveLink: false);
        var result = InspectQuery.Query(new InspectRequest(project.FwDataPath, recorded.InspectSubject!));

        Assert.Null(recorded.Link);
        Assert.Equal("recorded form", recorded.Form);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(quality == "authored", result.Value!.Facts.Value is not null);
        if (quality == "authored") Assert.NotNull(result.Value.Facts.Value!.Entry!.FieldWorks);
        else Assert.Equal(InspectorResolution.NotAuthored, result.Value.Resolution);
    }

    [Fact]
    public void ReplacedProjectKeepsRecordedFactsButSuppliesNoLiveTarget()
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
        var recorded = new TraceMorphViewModel(morph, allowLiveLink: false);
        var result = InspectQuery.Query(new InspectRequest(project.FwDataPath, recorded.InspectSubject!));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("recorded form", recorded.Form);
        Assert.Null(recorded.Link);
        Assert.NotNull(result.Value!.Facts.Value);
        Assert.Null(result.Value.Facts.Value!.Entry!.FieldWorks);
        Assert.All(result.Value.Facts.Value.Senses, sense => Assert.Null(sense.FieldWorks));
    }
}
