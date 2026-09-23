using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads the chosen Texts' words from a project's current Baseline scratch copy — never the live project,
/// the same separation <see cref="TextInventoryQuery"/> and <see cref="SIL.Motif.Commands.Assess.SelectionComposer"/> rely on.
/// </summary>
/// <remarks>
/// <b><see cref="TextWord.Form"/> is read exactly the way <see cref="SIL.Motif.Commands.Assess.SelectionComposer"/> reads it</b>
/// for the same Texts: each writing system populated on the occurrence's <c>WfiWordform.Form</c>, in
/// ascending writing-system order, trimmed and NFD-normalized. A word form spelled in more than one
/// writing system therefore contributes more than one distinct <see cref="TextWord"/> from the same
/// occurrence — the same flattening the composer applies when it builds a Selection from these Texts, so
/// the two never disagree about which strings the parser was actually asked about.
/// </remarks>
public static class TextWordsQuery
{
    public static CommandOutcome<TextWordsResponse> Query(TextWordsRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is null)
                return CommandOutcome<TextWordsResponse>.Success(
                    new TextWordsResponse(Array.Empty<TextWord>(), Array.Empty<TextLines>(), HasBaseline: false));

            using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);
            var repository = cache.ServiceLocator.GetInstance<ITextRepository>();

            var order = new List<string>();
            var accumulators = new Dictionary<string, WordAccumulator>(StringComparer.Ordinal);
            var texts = new List<TextLines>();

            foreach (var textId in request.TextIds)
            {
                if (!repository.TryGetObject(textId, out var text)) continue;
                texts.Add(ReadText(cache, projectName, textId, text, order, accumulators));
            }

            var words = order.Select(form =>
            {
                var accumulator = accumulators[form];
                var (approved, disapproved, candidates, incorrect) = ReadProjectAnalyses(cache, projectName, accumulator.WordformGuid);
                return new TextWord(form, accumulator.WordformGuid?.ToString("D"), accumulator.Occurrences, approved, disapproved,
                    candidates, incorrect);
            }).ToList();

            return CommandOutcome<TextWordsResponse>.Success(new TextWordsResponse(words, texts, HasBaseline: true));
        });

    private static TextLines ReadText(
        LcmCache cache, string projectName, Guid textId, IText text,
        List<string> order, Dictionary<string, WordAccumulator> accumulators)
    {
        var title = ReadTitle(text);
        var lines = new List<TextLine>();
        var lineNumber = 0;

        foreach (var paragraph in text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? Enumerable.Empty<IStTxtPara>())
        {
            var occurrencesBySegment = SegmentServices.GetAnalysisOccurrences(paragraph).ToLookup(o => o.Segment);
            foreach (var segment in paragraph.SegmentsOS)
            {
                lineNumber++;
                var sentence = segment.BaselineText?.Text ?? string.Empty;
                var tokens = new List<TextToken>();
                foreach (var occurrence in occurrencesBySegment[segment])
                {
                    tokens.Add(ReadToken(
                        cache, projectName, textId, title, lineNumber, sentence, occurrence.Analysis, order, accumulators));
                }
                lines.Add(new TextLine(lineNumber, tokens));
            }
        }

        return new TextLines(textId, title, lines);
    }

    private static TextToken ReadToken(
        LcmCache cache, string projectName, Guid textId, string title, int line, string sentence,
        IAnalysis analysis, List<string> order, Dictionary<string, WordAccumulator> accumulators)
    {
        if (analysis is IPunctuationForm punctuation)
            return new TextToken(punctuation.Form?.Text ?? string.Empty, Form: null, Gloss: null, Status: null);

        var (wordform, wfiAnalysis) = analysis switch
        {
            IWfiGloss wordGloss => ((IWfiWordform)wordGloss.Owner.Owner, (IWfiAnalysis)wordGloss.Owner),
            IWfiAnalysis wordAnalysis => ((IWfiWordform)wordAnalysis.Owner, wordAnalysis),
            IWfiWordform bare => (bare, (IWfiAnalysis?)null),
            _ => throw new NotSupportedException($"Unrecognized analysis kind: {analysis.GetType()}"),
        };

        var forms = TxtForms(wordform.Form).ToArray();
        string status;
        ProjectAnalysis? projectAnalysis;
        if (wfiAnalysis is { } chosen)
        {
            status = wordform.HumanApprovedAnalyses.Contains(chosen)
                ? InterlinearAnalysisStatus.Approved : InterlinearAnalysisStatus.Unapproved;
            projectAnalysis = BuildProjectAnalysis(cache, projectName, chosen);
        }
        else
        {
            status = InterlinearAnalysisStatus.Unanalysed;
            projectAnalysis = null;
        }

        foreach (var raw in forms)
        {
            var form = Canonicalize(raw);
            if (form.Length == 0) continue;
            if (!accumulators.TryGetValue(form, out var accumulator))
            {
                accumulator = new WordAccumulator(wordform.Guid);
                accumulators[form] = accumulator;
                order.Add(form);
            }
            accumulator.Occurrences.Add(new WordOccurrence(textId, title, line, sentence, status, projectAnalysis));
        }

        var primary = forms.Length > 0 ? Canonicalize(forms[0]) : string.Empty;
        var tokenText = forms.Length > 0 ? forms[0] : string.Empty;
        var gloss = projectAnalysis is null ? null
            : string.Join(" ", projectAnalysis.Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
        var chosenWordGloss = analysis is IWfiGloss chosenGloss ? BestText(chosenGloss.Form) : null;
        var category = wfiAnalysis?.CategoryRA is { } pos ? BestText(pos.Abbreviation) ?? BestText(pos.Name) : null;
        return new TextToken(tokenText, primary.Length == 0 ? null : primary, gloss, status)
        {
            Analysis = projectAnalysis,
            WordGloss = chosenWordGloss,
            Category = category,
            WordLink = tokenText.Length == 0 ? null : FieldWorksLinks.ForWordform(cache, projectName, tokenText),
        };
    }

    private static ProjectAnalysis BuildProjectAnalysis(LcmCache cache, string projectName, IWfiAnalysis analysis)
    {
        var bundles = analysis.MorphBundlesOS
            .Select(bundle => new MorphBundleContent(
                bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D")))
            .ToList();
        var morphs = analysis.MorphBundlesOS
            .Select(bundle => new ParseMorph(
                bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D"),
                GuessedString: null))
            .ToArray();
        return new ProjectAnalysis(AnalysisContent.ComputeDigest(bundles), ParserReadingReader.ReadMorphs(cache, projectName, morphs));
    }

    private static (IReadOnlyList<ProjectAnalysis> Approved, IReadOnlyList<ProjectAnalysis> Disapproved, int Candidates, bool Incorrect)
        ReadProjectAnalyses(LcmCache cache, string projectName, Guid? wordformGuid)
    {
        if (wordformGuid is not { } guid) return (Array.Empty<ProjectAnalysis>(), Array.Empty<ProjectAnalysis>(), 0, false);
        if (!cache.ServiceLocator.GetInstance<IWfiWordformRepository>().TryGetObject(guid, out var wordform))
            return (Array.Empty<ProjectAnalysis>(), Array.Empty<ProjectAnalysis>(), 0, false);

        var humanApproved = wordform.HumanApprovedAnalyses.ToList();
        var humanDisapproved = wordform.HumanDisapprovedParses.ToList();
        var approved = humanApproved.Select(a => BuildProjectAnalysis(cache, projectName, a)).ToArray();
        var disapproved = humanDisapproved.Select(a => BuildProjectAnalysis(cache, projectName, a)).ToArray();
        var withOpinion = humanApproved.Concat(humanDisapproved).ToHashSet();
        var candidates = wordform.AnalysesOC.Count(a => !withOpinion.Contains(a));
        return (approved, disapproved, candidates, wordform.SpellingStatus == IncorrectSpellingStatus);
    }

    // SpellingStatus's Incorrect member (0 = Undecided, 1 = Correct, 2 = Incorrect).
    private const int IncorrectSpellingStatus = 2;

    // The first non-empty alternative, analysis writing systems in id order; display text only.
    private static string? BestText(IMultiAccessorBase accessor) => accessor.AvailableWritingSystemIds.OrderBy(ws => ws)
        .Select(ws => accessor.get_String(ws)?.Text).FirstOrDefault(text => !string.IsNullOrEmpty(text));

    // Mirrors SelectionComposer's own Contribute step: trim, then NFD-normalize, so the two always agree.
    private static string Canonicalize(string raw) => raw.Trim().Normalize(System.Text.NormalizationForm.FormD);

    private static IEnumerable<string> TxtForms<TAccessor>(TAccessor accessor) where TAccessor : IMultiAccessorBase, ITsMultiString
    {
        foreach (var ws in accessor.AvailableWritingSystemIds.OrderBy(w => w))
        {
            var text = accessor.get_String(ws)?.Text;
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }

    // Mirrors InterlinearTextReader's own title choice: the first populated writing system, ws id ascending.
    private static string ReadTitle(IText text)
    {
        foreach (var ws in text.Name.AvailableWritingSystemIds.OrderBy(w => w))
        {
            var value = text.Name.get_String(ws)?.Text;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return string.Empty;
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;

    private sealed class WordAccumulator(Guid wordformGuid)
    {
        public Guid? WordformGuid { get; } = wordformGuid;
        public List<WordOccurrence> Occurrences { get; } = [];
    }
}
