using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class InspectorTests
{
    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("structural", "structural")]
    [InlineData("synthetic", "synthetic")]
    [InlineData("authored", "authored")]
    public void ATraceMorphCarriesItsRecordedIdentityQualityIntoTheInspector(string? recorded, string expected)
    {
        var morph = new TraceMorph(null, "a-", null, null, null, null, null, null, null, null)
        {
            FormId = "form-id",
            IdentityQuality = recorded,
        };

        Assert.Equal(expected, new TraceMorphViewModel(morph, allowLiveLink: false).InspectSubject!.IdentityQuality);
    }

    [Fact]
    public void AnInspectorIdentityIsUnknownUntilItsSourceSaysOtherwise()
    {
        Assert.Equal("unknown", new InspectorSubject(InspectorSubjectKind.Feature).IdentityQuality);
        Assert.Equal("unknown", InspectorSubject.Morpheme("form-id", null)!.IdentityQuality);
        Assert.Equal("unknown", InspectorSubject.Rule(new TraceTimingKey("phon_rule", "rule-id")).IdentityQuality);
    }

}
