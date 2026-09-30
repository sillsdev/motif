using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads Text words from the current Baseline's stored projection without opening LibLCM.</summary>
/// <remarks>
/// <para>
/// The projection is written with the Baseline by both capture and the runner's refresh, so it is exactly as
/// current as the Baseline itself. Reopened Assessments use its stored identities for comparison;
/// pending-change fit still uses the project model rather than this projection.
/// </para>
/// <para>
/// <b><see cref="TextWord.Form"/> is read exactly the way <see cref="SIL.Motif.Commands.Assess.SelectionComposer"/>
/// reads it</b> for the same Texts: each writing system populated on the occurrence's wordform, in ascending
/// writing-system order, trimmed and NFD-normalized. A word spelled in more than one writing system
/// therefore contributes more than one distinct <see cref="TextWord"/> from the same occurrence, so the two
/// never disagree about which strings the parser was asked about. FieldWorks links are built here from
/// stored targets, because they carry the requesting project's name.
/// </para>
/// </remarks>
public static class TextWordsQuery
{
    /// <summary>
    /// Returns the chosen Texts' ordered words, occurrences, analyses and lines, or a cancellation refusal once
    /// <paramref name="cancellationToken"/> is cancelled: checked before the store is opened and before each Text.
    /// </summary>
    public static CommandOutcome<TextWordsResponse> Query(
        TextWordsRequest request, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled();
        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var current = new BaselineRepository(database).GetCurrentTextWords(workspaceKey, request.TextIds);
            if (current is null)
                return CommandOutcome<TextWordsResponse>.Success(
                    new TextWordsResponse(Array.Empty<TextWord>(), Array.Empty<TextLines>(), HasBaseline: false));

            var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);
            var textsById = current.Projection.Texts.ToDictionary(text => text.TextId);
            var wordformsById = current.Projection.Wordforms.ToDictionary(wordform => wordform.WordformId);
            var order = new List<string>();
            var accumulators = new Dictionary<string, WordAccumulator>(StringComparer.Ordinal);
            var texts = new List<TextLines>();

            foreach (var textId in request.TextIds)
            {
                if (cancellationToken.IsCancellationRequested) return Cancelled();
                if (!textsById.TryGetValue(textId, out var text)) continue;
                var analyses = text.Analyses.ToDictionary(
                    stored => stored.Key, stored => ReadAnalysis(stored, projectName), StringComparer.Ordinal);
                var lines = new List<TextLine>();
                foreach (var line in text.Lines)
                {
                    var tokens = new List<TextToken>();
                    foreach (var token in line.Tokens)
                    {
                        var analysis = token.AnalysisKey is { } key ? analyses[key] : null;
                        var storedAnalyses = token.WordformId is { } storedWordformId &&
                            wordformsById.TryGetValue(storedWordformId, out var storedWordform)
                                ? storedWordform.Analyses.Select(item => ReadAnalysis(item, projectName)).ToArray()
                                : Array.Empty<ProjectAnalysis>();
                        var primary = token.Forms.Count == 0 ? string.Empty : Canonicalize(token.Forms[0]);
                        tokens.Add(new TextToken(token.Text, primary.Length == 0 ? null : primary,
                            GlossOf(analysis), token.Status)
                        {
                            Analysis = analysis,
                            StoredAnalyses = storedAnalyses,
                            WordGloss = token.WordGloss,
                            Category = token.Category,
                            WordformId = token.WordformId,
                            OccurrenceIndex = token.OccurrenceIndex,
                            StoredAnalysisId = token.AnalysisId is { } analysisId
                                ? CanonicalId.FromGuid(analysisId).Value : null,
                            IncorrectSpelling = token.WordformId is { } markedWordformId &&
                                wordformsById.TryGetValue(markedWordformId, out var markedWordform) &&
                                markedWordform.IncorrectSpelling,
                            WordLink = token.Text.Length == 0
                                ? null : FieldWorksLinks.ForTarget(projectName, token.WordLinkTarget),
                        });

                        if (token.Status is null) continue;
                        foreach (var raw in token.Forms)
                        {
                            var form = Canonicalize(raw);
                            if (form.Length == 0) continue;
                            if (!accumulators.TryGetValue(form, out var accumulator))
                            {
                                accumulator = new WordAccumulator(token.WordformId);
                                accumulators.Add(form, accumulator);
                                order.Add(form);
                            }
                            accumulator.Occurrences.Add(new WordOccurrence(
                                text.TextId, text.Title, line.Number, line.Sentence, token.Status, analysis));
                        }
                    }
                    lines.Add(new TextLine(line.Number, tokens)
                    {
                        ParagraphId = line.ParagraphId,
                        SegmentId = line.SegmentId,
                        ParseIsCurrent = line.ParseIsCurrent,
                    });
                }
                texts.Add(new TextLines(text.TextId, text.Title, lines));
            }

            var words = order.Select(form =>
            {
                var accumulator = accumulators[form];
                var wordform = accumulator.WordformId is { } id && wordformsById.TryGetValue(id, out var found)
                    ? found : null;
                var approved = wordform?.Approved.Select(analysis => ReadAnalysis(analysis, projectName)).ToArray()
                    ?? Array.Empty<ProjectAnalysis>();
                var disapproved = wordform?.Disapproved.Select(analysis => ReadAnalysis(analysis, projectName)).ToArray()
                    ?? Array.Empty<ProjectAnalysis>();
                var all = wordform?.Analyses.Select(analysis => ReadAnalysis(analysis, projectName)).ToArray()
                    ?? Array.Empty<ProjectAnalysis>();
                return new TextWord(form, accumulator.WordformId?.ToString("D"), accumulator.Occurrences,
                    approved, disapproved, wordform?.CandidateCount ?? 0, wordform?.IncorrectSpelling ?? false)
                {
                    Analyses = all,
                };
            }).ToList();

            return CommandOutcome<TextWordsResponse>.Success(new TextWordsResponse(words, texts, HasBaseline: true,
                OccurrenceCount: words.Sum(word => word.Occurrences.Count)));
        });
    }

    private static CommandOutcome<TextWordsResponse> Cancelled() => CommandOutcome<TextWordsResponse>.Refused(
        new Refusal("texts.words-cancelled", FailureReason.Cancelled, "Reading the chosen Texts' words was cancelled."));

    private static string? GlossOf(ProjectAnalysis? analysis) => analysis is null ? null
        : string.Join(" ", analysis.Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));

    private static ProjectAnalysis ReadAnalysis(TextWordsProjectedAnalysis analysis, string projectName)
    {
        var morphs = analysis.Morphs.Select(morph => new ParserReadingMorph(
            morph.Form, morph.Gloss, morph.Category, morph.InflectionType, morph.Guessed,
            FieldWorksLinks.ForTarget(projectName, morph.LinkTarget)) { Entry = morph.Entry }).ToArray();
        return new ProjectAnalysis(analysis.Key, morphs)
        {
            StoredAnalysisId = CanonicalId.FromGuid(analysis.AnalysisId).Value,
            StoredAnalysisOpinion = analysis.Opinion switch
            {
                "approved" => ReadingGrade.Approved,
                "disapproved" => ReadingGrade.Disapproved,
                "unknown" => ReadingGrade.Candidate,
                _ => ReadingGrade.NoOpinion,
            },
            Identity = analysis.Identity,
        };
    }

    private static string Canonicalize(string raw) => raw.Trim().Normalize(NormalizationForm.FormD);

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;

    private sealed class WordAccumulator(Guid? wordformId)
    {
        public Guid? WordformId { get; } = wordformId;
        public List<WordOccurrence> Occurrences { get; } = [];
    }
}
