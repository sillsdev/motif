using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>The one field a Text inventory read needs: which project to ask about.</summary>
public sealed record TextInventoryRequest(string ProjectPath);

/// <summary>
/// One Text available to add to a Selection. <see cref="Id"/> is the Text's own GUID and the only thing a
/// caller may match it by (AGENTS.md rule 12); <see cref="Title"/> is display text only.
/// </summary>
public sealed record TextChoiceSummary(
    Guid Id,
    string Title,
    int WordCount = 0,
    int InterlinearizedWordCount = 0,
    int OccurrenceCount = 0,
    int InterlinearizedOccurrenceCount = 0)
{
    public double WordCoveragePercent => WordCount == 0 ? 0 : 100d * InterlinearizedWordCount / WordCount;
    public double OccurrenceCoveragePercent => OccurrenceCount == 0 ? 0 : 100d * InterlinearizedOccurrenceCount / OccurrenceCount;
}

/// <summary>The current Baseline's Texts, and whether there was a Baseline to read them from.</summary>
/// <param name="HasBaseline">
/// False when no Baseline has been captured yet. An empty <paramref name="Texts"/> means two different
/// things — nothing captured, or a Baseline holding no Texts — and a caller that cannot tell them apart
/// can only show an empty list with no way for a reader to know what to do about it.
/// </param>
public sealed record TextInventoryResponse(IReadOnlyList<TextChoiceSummary> Texts, bool HasBaseline);

/// <summary>
/// Lists the Texts held in a project's current Baseline scratch copy, for the Selection editor's Text
/// picker. Read-only and outside the command catalog: it changes nothing and has no CLI verb.
/// </summary>
/// <remarks>
/// Reads only the published Baseline bundle's own scratch copy — never the live project — the same
/// separation <see cref="SIL.Motif.Commands.Assess.SelectionComposer"/> relies on. A project with no
/// current Baseline yet has no scratch copy to read, so it reports an empty inventory rather than opening
/// the live project to make one up.
/// </remarks>
public static class TextInventoryQuery
{
    public static CommandOutcome<TextInventoryResponse> Query(TextInventoryRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is null)
                return CommandOutcome<TextInventoryResponse>.Success(
                    new TextInventoryResponse(Array.Empty<TextChoiceSummary>(), HasBaseline: false));

            using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
            var texts = repository.AllInstances()
                .Select(text => ReadChoice(cache, text))
                .OrderByDescending(choice => choice.WordCoveragePercent)
                .ThenBy(choice => choice.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return CommandOutcome<TextInventoryResponse>.Success(new TextInventoryResponse(texts, HasBaseline: true));
        });

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

    private static TextChoiceSummary ReadChoice(LcmCache cache, IText text)
    {
        var projection = InterlinearTextReader.Read(cache, text);
        var words = projection.Paragraphs.SelectMany(paragraph => paragraph.Phrases)
            .SelectMany(phrase => phrase.Words).Where(word => word.WordformGuid is not null).ToArray();
        var interlinearized = words.Where(word => word.AnalysisStatus != InterlinearAnalysisStatus.Unanalysed).ToArray();
        return new TextChoiceSummary(text.Guid, ReadTitle(text),
            words.Select(word => word.WordformGuid!.Value).Distinct().Count(),
            interlinearized.Select(word => word.WordformGuid!.Value).Distinct().Count(),
            words.Length, interlinearized.Length);
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;
}
