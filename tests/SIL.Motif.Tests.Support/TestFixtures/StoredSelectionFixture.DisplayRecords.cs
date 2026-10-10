using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

public sealed partial class StoredSelectionFixture
{
    /// <summary>Seeds current captured records from explicitly authored display facts, without reading legacy stores.</summary>
    public static StoredSelectionFixture FromDisplayRecords(TextWordsResponse source, BaselineToken? baseline = null)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        Guid WordId(string form, Guid? id = null)
        {
            if (id is { } exact) return exact;
            if (!ids.TryGetValue(form, out var found)) ids.Add(form, found = Guid.NewGuid());
            return found;
        }
        var analyses = new Dictionary<string, TextWordsProjectedAnalysis>(StringComparer.Ordinal);
        TextWordsProjectedAnalysis Analysis(ProjectAnalysis sourceAnalysis)
        {
            var key = sourceAnalysis.StoredAnalysisId ?? sourceAnalysis.Key;
            if (analyses.TryGetValue(key, out var cached)) return cached;
            var id = CanonicalId.TryParse(sourceAnalysis.StoredAnalysisId, out var canonical) ? canonical.ToGuid() :
                Guid.TryParse(sourceAnalysis.StoredAnalysisId, out var guid) ? guid : Guid.NewGuid();
            var identity = sourceAnalysis.Identity ?? new ApprovedMorphology(sourceAnalysis.Morphs.Select(morph =>
                new ApprovedMorph(morph.AllomorphId, morph.GrammaticalInfoId, null, [morph.Form])).ToArray());
            var value = new TextWordsProjectedAnalysis(key, sourceAnalysis.Morphs.Select(morph =>
                new TextWordsProjectedMorph(morph.Form, morph.Gloss, morph.Category, morph.InflectionType,
                    morph.Guessed, null)
                {
                    FormWritingSystem = morph.FormWritingSystem,
                    GlossWritingSystem = morph.GlossWritingSystem,
                    CategoryWritingSystem = morph.CategoryWritingSystem,
                }).ToArray())
            {
                AnalysisId = id,
                Opinion = sourceAnalysis.StoredAnalysisOpinion switch
                {
                    ReadingGrade.Approved => ReadingGrade.Approved,
                    ReadingGrade.Disapproved => ReadingGrade.Disapproved,
                    _ => "unknown",
                },
                Identity = identity with { SourceAnalysisId = CanonicalId.FromGuid(id).Value },
            };
            analyses.Add(key, value);
            return value;
        }

        var wordforms = new Dictionary<Guid, TextWordsProjectedWordform>();
        void Wordform(Guid id, IEnumerable<ProjectAnalysis> readings, bool incorrect)
        {
            var stored = readings.Select(Analysis).DistinctBy(analysis => analysis.AnalysisId).ToArray();
            if (wordforms.TryGetValue(id, out var previous))
                stored = previous.Analyses.Concat(stored).DistinctBy(analysis => analysis.AnalysisId).ToArray();
            wordforms[id] = new(id, stored.Where(analysis => analysis.Opinion == ReadingGrade.Approved).ToArray(),
                stored.Where(analysis => analysis.Opinion == ReadingGrade.Disapproved).ToArray(),
                stored.Count(analysis => analysis.Opinion == "unknown"), incorrect, stored);
        }
        foreach (var word in source.Words)
        {
            var id = CanonicalId.TryParse(word.WordformGuid, out var canonical) ? canonical.ToGuid() :
                Guid.TryParse(word.WordformGuid, out var guid) ? guid : WordId(word.Form);
            ids.TryAdd(word.Form, id);
            Wordform(id, word.Analyses.Concat(word.Approved).Concat(word.Disapproved), word.IncorrectSpelling);
        }
        var texts = source.Texts.Select(text =>
        {
            var lines = text.Lines.Select(line =>
            {
                var tokens = line.Tokens.Select((token, offset) =>
                {
                    Guid? id = token.Form is null ? null : WordId(token.Form, token.WordformId);
                    var chosen = token.Analysis is { } selected ? Analysis(selected) : null;
                    if (id is { } wordId)
                        Wordform(wordId, token.StoredAnalyses.Concat(token.Analysis is { } analysis ? [analysis] : []),
                            token.IncorrectSpelling);
                    return new TextWordsProjectedToken(token.Text,
                        token.Form is null ? [] : [new WritingSystemText(token.Form, token.FormWritingSystem ?? "en")],
                        id, id is null ? null : chosen is null ? "unanalysed" : chosen.Opinion switch
                        {
                            ReadingGrade.Approved => "approved",
                            ReadingGrade.Disapproved => "disapproved",
                            _ => "unapproved",
                        }, chosen?.Key, token.WordGloss, token.Category,
                        id is { } linkId ? new FieldWorksLinkTarget("analyses", linkId) : null,
                        offset, chosen?.AnalysisId)
                    {
                        TextWritingSystem = token.TextWritingSystem,
                        WordGlossWritingSystem = token.WordGlossWritingSystem,
                        CategoryWritingSystem = token.CategoryWritingSystem,
                    };
                }).ToArray();
                return new TextWordsProjectedLine(line.Number, string.Join(" ", line.Tokens.Select(token => token.Text)),
                    tokens, line.ParagraphId == Guid.Empty ? Guid.NewGuid() : line.ParagraphId,
                    line.SegmentId == Guid.Empty ? Guid.NewGuid() : line.SegmentId, true)
                { SentenceStyle = line.SentenceStyle, SentenceWritingSystem = line.SentenceWritingSystem };
            }).ToArray();
            var selected = lines.SelectMany(line => line.Tokens).Where(token => token.AnalysisKey is not null)
                .Select(token => analyses[token.AnalysisKey!]).DistinctBy(analysis => analysis.AnalysisId).ToArray();
            return new TextWordsProjectedText(text.TextId, text.Title, lines, selected)
            { TitleWritingSystem = text.TitleWritingSystem };
        }).ToArray();
        return new StoredSelectionFixture(new TextWordsProjection(texts, wordforms.Values.ToArray()), baseline);
    }

    /// <summary>Stores Assessment words and declared measurement identities for a window fixture.</summary>
    public void RecordAssessment(AssessCommandResponse response)
    {
        var root = response.Measurements.LastOrDefault(item => item.Kind == AssessmentKinds.ParseTime)?.AssessmentId ??
            response.AssessmentIds.FirstOrDefault() ?? "fixture/assessment";
        var record = new NewAssessmentRecord(root, null, null, "test", AssessmentKinds.ParseTime, "{}",
            "sha256:fixture-selection", "whitespace", "1", JsonSerializer.Serialize(response.Baseline.Token, MotifJson.CreateOptions()),
            Selection.Create("Fixture", response.Words.Select(word => word.Word)),
            "sha256:fixture-outcome", "sha256:fixture-semantic", "sha256:fixture-grammar", "model", "pipeline", 0,
            response.Words.Select(word => new AssessedWord(word.Word, word.Outcome, [], word.ElapsedMs)
            {
                Morphology = word.Morphology, IsIncomplete = word.IsIncomplete, ProjectStanding = word.ProjectStanding,
                ReadingGrades = word.ReadingGrades, OccurrenceCount = word.OccurrenceCount,
                AnalysisComparison = word.AnalysisComparison, MissedApproved = word.MissedApproved,
            }).ToArray(), SavedUtc: DateTimeOffset.UtcNow.ToString("O"))
        {
            Invocation = new BatchInvocationEvidence(string.IsNullOrEmpty(response.InvocationId) ? root : response.InvocationId,
                "fixture", "sha256:fixture-source", "sha256:fixture-parser", "fixture-words", "sha256:fixture-words",
                "fixture-tsv", "sha256:fixture-tsv", "fixture-stderr", "sha256:fixture-stderr", null,
                StepCap.Default, 1, false),
        };
        RecordAssessment(record);
        foreach (var replacement in response.TimingOverrideAssessmentIds.Select((id, index) => (id, index)))
            RecordAssessment(record with
            {
                AssessmentId = replacement.id,
                ReplacesAssessmentId = root,
                SavedUtc = DateTimeOffset.Parse(record.SavedUtc!).AddSeconds(replacement.index + 1).ToString("O"),
                Invocation = record.Invocation! with { InvocationId = replacement.id },
            });
        foreach (var measurement in response.Measurements.Where(item => item.AssessmentId != root)
                     .DistinctBy(item => item.AssessmentId))
            RecordAssessment(record with { AssessmentId = measurement.AssessmentId, Kind = measurement.Kind, Words = [] });
    }
}
