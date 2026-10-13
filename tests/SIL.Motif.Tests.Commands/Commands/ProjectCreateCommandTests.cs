using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ProjectCreateCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-project-create-" + Guid.NewGuid().ToString("N"));

    public ProjectCreateCommandTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// A created project reopens with the named writing systems, keeps an apostrophe inside a word, and holds an empty
    /// phoneme set with only FieldWorks' reserved boundary markers: no phonemes, entries or texts.
    /// </summary>
    [Fact]
    public void CreatesABlankProjectWithTheNamedWritingSystemsAndOnlyTheReservedMarkers()
    {
        var outcome = ProjectCreateCommand.Create(new ProjectCreateRequest(
            _root, "Kiswahili", "swh", "Kiswahili", "'", null));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var created = outcome.Value!;
        var expectedPath = Path.Combine(_root, "Kiswahili", "Kiswahili.fwdata");
        Assert.Equal(Path.GetFullPath(expectedPath), created.FwDataPath);
        Assert.Equal("swh", created.Vernacular);
        Assert.Equal("Kiswahili", created.VernacularName);
        Assert.Equal(["'"], created.WordFormingCharacters);
        Assert.Equal("en", created.Analysis);

        using var cache = new FwDataProjectLoader().LoadScratchCache(created.FwDataPath);
        var vernacular = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
        Assert.Equal("swh", vernacular.LanguageTag);
        Assert.Equal("en", cache.ServiceLocator.WritingSystems.DefaultAnalysisWritingSystem.LanguageTag);
        Assert.Single(cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems);
        Assert.Equal(["kwa'heri", "rafiki"], new WritingSystemWordTokeniser(vernacular).Tokenise("kwa'heri, rafiki."));

        var set = Assert.Single(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS);
        Assert.Empty(set.PhonemesOC);
        Assert.Equal(
            new[] { LangProjectTags.kguidPhRuleMorphBdry, LangProjectTags.kguidPhRuleWordBdry }.Order(),
            set.BoundaryMarkersOC.Select(marker => marker.Guid).Order());
        Assert.Equal(["#", "+"], set.BoundaryMarkersOC
            .Select(marker => marker.CodesOS.Single().Representation.get_String(cache.DefaultVernWs).Text).Order());
        Assert.Empty(cache.LangProject.LexDbOA.Entries);
        Assert.Empty(cache.LangProject.Texts);
    }

    [Fact]
    public void DefaultsTheAnalysisWritingSystemToEnglishAndDeclaresNoExtraWordFormingCharacters()
    {
        var outcome = ProjectCreateCommand.Create(new ProjectCreateRequest(_root, "Plain", "swh"));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("en", outcome.Value!.Analysis);
        Assert.Empty(outcome.Value.WordFormingCharacters);
    }

    [Fact]
    public void NeverOverwritesAnExistingProjectFolder()
    {
        var existing = Path.Combine(_root, "Taken");
        Directory.CreateDirectory(existing);
        var marker = Path.Combine(existing, "keep.txt");
        File.WriteAllText(marker, "mine");

        var outcome = ProjectCreateCommand.Create(new ProjectCreateRequest(_root, "Taken", "swh"));

        Assert.False(outcome.Succeeded);
        Assert.Equal("project-create.exists", outcome.Refusal!.Code);
        Assert.Equal(["keep.txt"], Directory.GetFileSystemEntries(existing).Select(Path.GetFileName));
        Assert.Equal("mine", File.ReadAllText(marker));
    }

    [Theory]
    [InlineData("", "swh", null, null, "project-create.invalid-name")]
    [InlineData("a/b", "swh", null, null, "project-create.invalid-name")]
    [InlineData("CON", "swh", null, null, "project-create.invalid-name")]
    [InlineData("ends.", "swh", null, null, "project-create.invalid-name")]
    [InlineData("Ok", "not a tag", null, null, "project-create.invalid-writing-system")]
    [InlineData("Ok", "swh", null, "not a tag", "project-create.invalid-writing-system")]
    [InlineData("Ok", "en", null, null, "project-create.invalid-writing-system")]
    [InlineData("Ok", "swh", "' ", null, "project-create.invalid-word-forming")]
    public void RefusesAnInvalidRequestWithoutCreatingAnything(
        string name, string vernacular, string? wordForming, string? analysis, string code)
    {
        var outcome = ProjectCreateCommand.Create(new ProjectCreateRequest(_root, name, vernacular, null, wordForming, analysis));

        Assert.False(outcome.Succeeded);
        Assert.Equal(code, outcome.Refusal!.Code);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void RefusesAMissingParentFolder()
    {
        var outcome = ProjectCreateCommand.Create(new ProjectCreateRequest(
            Path.Combine(_root, "missing"), "Ok", "swh"));

        Assert.False(outcome.Succeeded);
        Assert.Equal("project-create.folder-not-found", outcome.Refusal!.Code);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("'’", new[] { "'", "’" })]
    [InlineData("''", new[] { "'" })]
    [InlineData("", new string[0])]
    public void ReadsWordFormingCharactersOnePerCharacterWithoutDuplicates(string raw, string[] expected)
    {
        Assert.True(ProjectCreateCommand.TryReadWordForming(raw, out var characters, out _));
        Assert.Equal(expected, characters);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
