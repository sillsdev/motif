using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Projection;

/// <summary>Text Coverage counts the vernacular words of a Text, once per occurrence, and counts the rest apart.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class TextCoverageWordsTests(PristineProjectFixture pristine)
{
    [Fact]
    public void EnglishRunsAndNumbersAreCountedApartAndEachVernacularOccurrenceCountsOnce()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        IText text = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(cache.DefaultAnalWs, "coverage");
            text.ContentsOA = services.GetInstance<IStTextFactory>().Create();
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            text.ContentsOA.ParagraphsOS.Add(paragraph);
            var segment = services.GetInstance<ISegmentFactory>().Create();
            paragraph.SegmentsOS.Add(segment);
            var both = Wordform(cache, "motifb", vernacular: true);
            var secondVernacular = services.WritingSystems.VernacularWritingSystems
                .First(ws => ws.Handle != cache.DefaultVernWs);
            both.Form.set_String(secondVernacular.Handle, "motifb-alternate");
            both.Form.set_String(cache.DefaultAnalWs, "alternative");
            foreach (var wordform in new[]
                     {
                         Wordform(cache, "motifa", vernacular: true), both, Wordform(cache, "1990", vernacular: true),
                         Wordform(cache, "explanation", vernacular: false), Wordform(cache, "mo@tifc", vernacular: true),
                         both,
                     })
                segment.AnalysesRS.Add(wordform);
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            both.AnalysesOC.Add(analysis);
            var gloss = services.GetInstance<IWfiGlossFactory>().Create();
            analysis.MeaningsOC.Add(gloss);
            segment.AnalysesRS.Add(analysis);
            segment.AnalysesRS.Add(gloss);
        });

        var counts = TextOccurrenceReader.Read(cache, [text.Guid, text.Guid]);

        Assert.Equal(6, counts.TotalOccurrences);
        Assert.Equal(1, counts.DigitOccurrences);
        Assert.Equal(1, counts.OtherWritingSystemOccurrences);
        Assert.Equal(["mo@tifc", "motifa", "motifb", "motifb-alternate"],
            counts.OccurrencesByWord.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(4, counts.OccurrencesByWord["motifb"]);
        Assert.Equal(0, counts.OccurrencesByWord["motifb-alternate"]);
        Assert.Equal(counts.TotalOccurrences, counts.OccurrencesByWord.Values.Sum());
        Assert.Equal(2, counts.InterlinearizedOccurrences);
        Assert.Equal(2, counts.InterlinearizedOccurrencesByWord["motifb"]);
        Assert.Equal(0, counts.InterlinearizedOccurrencesByWord["motifb-alternate"]);
        Assert.Equal(counts.InterlinearizedOccurrences, counts.InterlinearizedOccurrencesByWord.Values.Sum());

        var summary = Assert.Single(ProjectSummaryReader.Read(cache).Texts, value => value.TextId == text.Guid);

        Assert.Equal(4, summary.WordCount);
        Assert.Equal(6, summary.OccurrenceCount);
        Assert.Equal(2, summary.InterlinearizedWordCount);
        Assert.Equal(2, summary.InterlinearizedOccurrenceCount);
        Assert.Equal(1, summary.DigitOccurrenceCount);
        Assert.Equal(1, summary.OtherWritingSystemOccurrenceCount);
        Assert.Equal(counts.OccurrencesByWord.OrderBy(value => value.Key),
            summary.OccurrencesByWord.OrderBy(value => value.Key));

        var project = new ProjectLocator(cache.ProjectId.Path, cache.ProjectId.Name);
        using var database = MotifDatabase.OpenOwned(
            Path.Combine(Path.GetDirectoryName(cache.ProjectId.Path)!, "coverage.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var selection = SelectionComposer.Compose(cache, new SelectionRequest(false, [text.Guid], [], false, null),
            new AssessmentRepository(database));

        Assert.True(selection.Succeeded);
        Assert.Equal(counts.OccurrencesByWord.Keys.Order(StringComparer.Ordinal), selection.Value!.Selection.Words);
    }

    [Fact]
    public void ProjectSummaryRequiresTheTextCoverageCategoryCounts()
    {
        var summary = new ProjectSummarySnapshot(0, 0, 0, 0, 0, [],
            [new ProjectTextSummary(Guid.NewGuid(), "Text", 0, 0, new Dictionary<string, int>())
            {
                InterlinearizedWordCount = 0,
                InterlinearizedOccurrenceCount = 0,
                DigitOccurrenceCount = 0,
                OtherWritingSystemOccurrenceCount = 0,
            }])
        {
            WordWritingSystems = new Dictionary<string, string?>(),
            WritingSystems = [],
        };

        foreach (var countName in new[] { "DigitOccurrenceCount", "OtherWritingSystemOccurrenceCount" })
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(summary))!.AsObject();
            json["Texts"]!.AsArray()[0]!.AsObject().Remove(countName);

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectSummarySnapshot>(json.ToJsonString()));
        }
    }

    private static IWfiWordform Wordform(LcmCache cache, string form, bool vernacular) =>
        cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
            TsStringUtils.MakeString(form, vernacular ? cache.DefaultVernWs : cache.DefaultAnalWs));
}
