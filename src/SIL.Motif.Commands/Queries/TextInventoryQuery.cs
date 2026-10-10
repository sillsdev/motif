using SIL.Motif.Host;
using System;
using System.Linq;
using SIL.Motif.Contract.Commands;
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
    public double InterlinearizationPercent { get; init; }
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

/// <summary>Lists captured Text identities, titles and counts for the Selection editor.</summary>
public static class TextInventoryQuery
{
    public static CommandOutcome<TextInventoryResponse> Query(TextInventoryRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var current = new BaselineRepository(database).GetCurrentEvidence(ProjectWorkspaceKey.Compute(project));
            if (current is null)
                return CommandOutcome<TextInventoryResponse>.Success(
                    new TextInventoryResponse(Array.Empty<TextChoiceSummary>(), HasBaseline: false));
            var texts = current.Summary.Texts.Select(text => new TextChoiceSummary(text.TextId, text.Title,
                text.WordCount, text.InterlinearizedWordCount, text.OccurrenceCount, text.InterlinearizedOccurrenceCount)
            {
                InterlinearizationPercent = text.WordCount == 0 ? 0 : 100d * text.InterlinearizedWordCount / text.WordCount,
            }).OrderByDescending(choice => choice.WordCoveragePercent)
                .ThenBy(choice => choice.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            return CommandOutcome<TextInventoryResponse>.Success(new TextInventoryResponse(texts, HasBaseline: true));
        });
}
