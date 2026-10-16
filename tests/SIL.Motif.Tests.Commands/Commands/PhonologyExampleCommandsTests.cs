using System.Xml.Linq;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
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

    [Fact]
    public void SaveRefusesFieldWorksEntriesTheCurrentBaselineDoesNotHaveAndNamesThem()
    {
        var projectPath = pristine.CopyProjectFile();
        WithBaseline(projectPath, () =>
        {
            var ghostEntry = Guid.NewGuid().ToString();
            var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath, "Ghosts",
                [FieldWorksExample("ghost", ghostEntry, Guid.NewGuid().ToString())]));

            Assert.False(saved.Succeeded);
            Assert.Equal("phonology-examples.entry-not-found", saved.Refusal!.Code);
            Assert.Equal(FailureReason.NotFound, saved.Refusal.Reason);
            Assert.Contains("ghost", saved.Refusal.Message, StringComparison.Ordinal);
            Assert.Contains(ghostEntry, saved.Refusal.Message, StringComparison.Ordinal);
            Assert.Empty(PhonologyExampleCommands.List(new ListPhonologyExamplesRequest(projectPath)).Value!.Items);
        });
    }

    [Fact]
    public void SaveAcceptsARealEntryAndStemMsaFromTheBaselineAndSaysSo()
    {
        var projectPath = pristine.CopyProjectFile();
        WithBaseline(projectPath, () =>
        {
            var (entry, msa) = FirstStemMsa(projectPath);
            var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath, "Real",
                [FieldWorksExample("real", entry, msa), Example("hc")]));

            Assert.True(saved.Succeeded, saved.Refusal?.Message);
            Assert.Equal("verified", saved.Value!.EntryVerification);
        });
    }

    [Fact]
    public void WithoutABaselineSaveStillWorksAndReportsTheEntriesWereNotVerified()
    {
        var projectPath = pristine.CopyProjectFile();

        var fieldWorks = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath, "Early",
            [FieldWorksExample("early", Guid.NewGuid().ToString(), Guid.NewGuid().ToString())]));
        var hcOnly = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath, "Hc", [Example("hc")]));

        Assert.True(fieldWorks.Succeeded, fieldWorks.Refusal?.Message);
        Assert.Equal("not-verified-no-baseline", fieldWorks.Value!.EntryVerification);
        Assert.Equal("not-applicable", hcOnly.Value!.EntryVerification);
    }

    [Fact]
    public void AnUnreadableBaselineFileDoesNotStopSaveAndReportsTheEntriesWereNotVerified()
    {
        var projectPath = pristine.CopyProjectFile();
        var managedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.PhonologySaveVerify", Guid.NewGuid().ToString("N"));
        try
        {
            var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            foreach (var file in Directory.GetFiles(managedRoot, "*.fwdata", SearchOption.AllDirectories))
                File.WriteAllText(file, "<not-xml");

            var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath, "Broken",
                [FieldWorksExample("broken", Guid.NewGuid().ToString(), Guid.NewGuid().ToString())]));

            Assert.True(saved.Succeeded, saved.Refusal?.Message);
            Assert.Equal("not-verified-baseline-file-missing", saved.Value!.EntryVerification);
        }
        finally
        {
            try { Directory.Delete(managedRoot, recursive: true); } catch (IOException) { }
        }
    }

    private static void WithBaseline(string projectPath, Action body)
    {
        var managedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.PhonologySaveVerify", Guid.NewGuid().ToString("N"));
        try
        {
            var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            body();
        }
        finally
        {
            try { Directory.Delete(managedRoot, recursive: true); } catch (IOException) { }
        }
    }

    private static (string Entry, string Msa) FirstStemMsa(string fwdata)
    {
        var msa = XDocument.Load(fwdata).Descendants("rt").First(rt =>
            (string?)rt.Attribute("class") == "MoStemMsa" && rt.Attribute("ownerguid") is not null);
        return ((string)msa.Attribute("ownerguid")!, (string)msa.Attribute("guid")!);
    }

    private static PhonologyExample FieldWorksExample(string id, string entry, string msa) =>
        new(id, "a-ku", ["e-ki"], new PhonologyGenerationContext.FwdataEntry(entry, msa, PhonologyMsaClass.MoStemMsa));

    private static PhonologyExample Example(string id, CitationView? citation = null) =>
        new(id, "a-ku", ["e-ki"], new PhonologyGenerationContext.HcXmlEntry("entry-17"), citation);
}
