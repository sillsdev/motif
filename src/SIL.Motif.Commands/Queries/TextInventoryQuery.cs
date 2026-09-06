using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>The one field a Text inventory read needs: which project to ask about.</summary>
public sealed record TextInventoryRequest(string ProjectPath);

/// <summary>
/// One Text available to add to a Selection. <see cref="Id"/> is the Text's own GUID and the only thing a
/// caller may match it by (AGENTS.md rule 12); <see cref="Title"/> is display text only.
/// </summary>
public sealed record TextChoiceSummary(Guid Id, string Title);

/// <summary>The current Baseline's Texts, empty when no Baseline has been captured for this project yet.</summary>
public sealed record TextInventoryResponse(IReadOnlyList<TextChoiceSummary> Texts);

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
                    new TextInventoryResponse(Array.Empty<TextChoiceSummary>()));

            using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
            var texts = repository.AllInstances()
                .Select(text => new TextChoiceSummary(text.Guid, ReadTitle(text)))
                .OrderBy(choice => choice.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return CommandOutcome<TextInventoryResponse>.Success(new TextInventoryResponse(texts));
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

    private static string ResolveProductVersion() =>
        typeof(TextInventoryQuery).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
