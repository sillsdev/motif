using SIL.Motif.Commands;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class PhonologyExampleCommandsTests(PristineProjectFixture pristine)
{
    [Fact]
    public void SaveListAndShowUseStoredRowsWithoutCapturingOrRunningTheParser()
    {
        var projectPath = pristine.CopyProjectFile();
        var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath,
            "Vowel harmony", [Example("front", new CitationView("Field notes", "example 12"))]));

        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var listed = PhonologyExampleCommands.List(new ListPhonologyExamplesRequest(projectPath));
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        var listedSet = Assert.Single(listed.Value!.Items);
        Assert.Equal(saved.Value!.ExampleSet.Revision.RevisionId, listedSet.Revision.RevisionId);

        var shown = PhonologyExampleCommands.Show(new ShowPhonologyExamplesRequest(projectPath,
            listedSet.Revision.RevisionId));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        var example = Assert.Single(shown.Value!.Items);
        Assert.Equal("front", example.Id);
        Assert.Equal(new CitationView("Field notes", "example 12"), example.Citation);
    }

    [Fact]
    public void SaveRefusesStaleRevisionAndInvalidContentWithStableCodes()
    {
        var projectPath = pristine.CopyProjectFile();
        var first = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath,
            "Set", [Example("first")]));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var second = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath,
            "Set", [Example("second")], first.Value!.ExampleSet.Revision.RevisionId));
        Assert.True(second.Succeeded, second.Refusal?.Message);

        var stale = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath,
            "Set", [Example("stale")], first.Value.ExampleSet.Revision.RevisionId));
        var invalid = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(
            "/missing/project.fwdata", "Set", []));

        Assert.Equal("phonology-examples.revision-conflict", stale.Refusal?.Code);
        Assert.Equal("phonology-examples.invalid", invalid.Refusal?.Code);
    }

    private static PhonologyExample Example(string id, CitationView? citation = null) =>
        new(id, "a-ku", ["e-ki"], new PhonologyGenerationContext.HcXmlEntry("entry-17"), citation);
}
