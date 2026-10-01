using System.IO;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class EvidenceQueryBaselineTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.EvidenceQueryBaselineTests",
        Guid.NewGuid().ToString("N"));

    public EvidenceQueryBaselineTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void UsesAndStoredReadingsRemainReadableWhileFieldWorksHoldsTheSource()
    {
        var project = Capture();
        var selected = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [project.TextId], []));
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        RecordAssessment(project, [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]);
        var stamp = File.GetLastWriteTimeUtc(project.Path);
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(project.Path,
            new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }));
        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);

        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(uses.Value!.Uses!.Words).Row.Word);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var reading = Assert.Single(evidence.Value!.Assessment!.Words
            .Single(word => word.Word == SeededProject.AnalysedWordForm).Readings!);
        Assert.Equal(SeededProject.FirstGloss, reading.Morphs[0].Gloss);
        Assert.Contains(Path.GetFileNameWithoutExtension(project.Path), reading.Morphs[0].FieldWorksLink!);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(project.Path));
    }

    private CapturedProject Capture()
    {
        string path;
        Guid textId;
        ParseWordEvidence morphology;
        using (var cache = _pristine.NewScratch())
        {
            textId = SeededProject.SeedText(cache, _pristine.Seed).TextId;
            var approved = Assert.Single(ApprovedMorphologyReader.Read(cache)[SeededProject.AnalysedWordForm]);
            morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, SeededProject.AnalysedWordForm, 1,
                false, false, false, [new ParseAnalysis(approved.Morphs.Select(morph =>
                    new ParseMorph(morph.Form, morph.Msa, morph.InflType, null)).ToArray())], []);
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return new CapturedProject(path, textId, captured.Value!, morphology);
    }

    private static void RecordAssessment(CapturedProject project, IReadOnlyList<string> words)
    {
        using var database = ProjectMotifDatabase.Open(project.Path);
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            "assessment", null, null, "pangloss", AssessmentKinds.ParseTime, "{}", "sha256:scope",
            "whitespace", "1", JsonSerializer.Serialize(project.Baseline.Token, MotifJson.CreateOptions()),
            Selection.Create("Default", words), "sha256:outcome", "sha256:semantic", "sha256:grammar",
            "fingerprint", "pipeline", 0, words.Select(word => new AssessedWord(word, "analysed", [], 1)
            {
                ProjectStanding = ProjectStanding.Approved,
                Morphology = word == SeededProject.AnalysedWordForm ? project.Morphology : null,
            }).ToArray()));
    }

    private sealed record CapturedProject(string Path, Guid TextId, BaselineCaptureResponse Baseline,
        ParseWordEvidence Morphology);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
