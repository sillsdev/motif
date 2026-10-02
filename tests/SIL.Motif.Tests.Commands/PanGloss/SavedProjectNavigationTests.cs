using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

public sealed class SavedProjectNavigationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.NavigationTests", Guid.NewGuid().ToString("N"));
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EntryId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("<languageproject><rt class='LangProject' guid='{0}'/><rt class='LexEntry' guid='{1}'/>", "unknown")]
    [InlineData("<languageproject><rt class='LexEntry' guid='{1}'/></languageproject>", "unknown")]
    [InlineData("<languageproject><rt class='LangProject' guid='{0}'/><rt class='LangProject' guid='{1}'/></languageproject>", "unknown")]
    [InlineData("<languageproject><rt class='LangProject' guid='{0}'/><rt class='LexEntry' guid='{1}'/><rt class='LexEntry' guid='{1}'/></languageproject>", "unknown")]
    [InlineData("<languageproject><rt class='LangProject' guid='{1}'/><rt class='LexEntry' guid='{0}'/></languageproject>", "mismatch")]
    public void IncompleteOrAmbiguousSavedIdentityCannotAuthorizeLinks(string xml, string status)
    {
        var navigation = Read(string.Format(xml, ProjectId, EntryId));
        Assert.Equal(status, navigation.ProjectIdentityStatus);
        Assert.Null(navigation.LinkFor(new("lexiconEdit", EntryId)));
    }

    [Fact]
    public void AVerifiedDestinationUsesTheCurrentFilenameAndCanonicalGuid()
    {
        var navigation = Read($"<languageproject><rt class='LangProject' guid='{ProjectId:B}'/><rt class='LexEntry' guid='{EntryId:N}'/></languageproject>");
        Assert.True(navigation.ProjectMatches);
        var earlier = FieldWorksLinks.ForTarget("earlier filename", new("lexiconEdit", EntryId));
        var verified = navigation.VerifyLink(earlier);
        Assert.Equal(FieldWorksLinks.ForTarget("renamed", new("lexiconEdit", EntryId)), verified);
        Assert.Null(navigation.VerifyLink("https://example.invalid/"));
        Assert.Null(navigation.LinkFor(new("lexiconEdit", Guid.NewGuid())));
        var captured = new ParserReading([new ParserReadingMorph("captured", "gloss", "category", null, false, earlier)]);
        Assert.Equal(verified, navigation.Verify(captured).Morphs[0].FieldWorksLink);
        Assert.Equal("captured", navigation.Verify(captured).Morphs[0].Form);
    }

    [Fact]
    public void WarningNavigationLeavesProducerAdviceAndReportedStateIntact()
    {
        var navigation = Read($"<languageproject><rt class='LangProject' guid='{ProjectId}'/></languageproject>");
        var part = new GrammarWarningPart("producer label", GrammarWarningPartRole.Object,
            FieldWorksLink: FieldWorksLinks.ForTarget("earlier", new("lexiconEdit", EntryId)))
        {
            FieldWorksGuid = EntryId.ToString("B"), FieldWorksTool = "lexiconEdit", LinkStatus = FieldWorksLinkStatus.Available,
        };
        var check = new GrammarCheckResponse([new GrammarWarning(GrammarDiagnosticLevel.Warning, "producer code", [part], [part], "producer text")
            { Description = "producer description", Guidance = "producer advice" }], true);
        var verified = navigation.Verify(check).Findings[0];
        Assert.Equal(check.Findings[0].Description, verified.Description);
        Assert.Equal(check.Findings[0].Guidance, verified.Guidance);
        Assert.Equal(part.Text, verified.Subject[0].Text);
        Assert.Equal(part.LinkStatus, verified.Subject[0].LinkStatus);
        Assert.Null(verified.Subject[0].FieldWorksLink);
        Assert.Null(verified.Problem[0].FieldWorksLink);
    }

    private SavedProjectNavigation Read(string xml)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "renamed.fwdata");
        File.WriteAllText(path, xml);
        return SavedProjectNavigation.Read(path, ProjectId.ToString("N"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
