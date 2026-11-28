using System.Text.Json;
using System.Text;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>What staging a new Guide item decided: the number it took and whether it also creates the list.</summary>
public sealed record GdrSectionStaged(string Id, string Abbreviation, string? ParentId, bool ListCreated,
    IReadOnlyList<string> Fields, int OperationCount, string DraftName, IReadOnlyList<string> Warnings,
    IReadOnlyList<string>? StylesCreated = null);

public static partial class ProposalCommands
{
    /// <summary>
    /// Stages a new Guide item in one Draft save: the item with the next permanent number and its title, then, when
    /// <paramref name="markdown"/> is given, its Discussion read against the item as it will be, so a body that cannot be
    /// read stages nothing. The list is created by the item's operation when the project has none.
    /// </summary>
    public static CommandOutcome<GdrSectionStaged> StageGdrSection(string projectPath, string title, string? parent, string? after,
        string? markdown, string? writingSystem, string? namedDraft)
    {
        var warnings = new List<string>();
        string? number = null;
        string? parentId = null;
        var listCreated = false;
        var stylesCreated = new List<string>();
        var entity = CanonicalId.Mint();
        var outcome = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = namedDraft ?? PendingChanges.DraftName;
            var marks = new GuideNumberMarkRepository(database);
            var floor = 0L;
            try
            {
                floor = marks.Floor();
                var steps = new List<ComposeStep>
                {
                    new(null, "AddGdrSection", (cache, _) =>
                    {
                        var list = GdrSectionSupport.FindList(cache, GdrSectionOperationKinds.Create);
                        listCreated = list is null;
                        number = GdrSectionSupport.NextNumber(cache, list, floor);
                        var ws = string.IsNullOrWhiteSpace(writingSystem)
                            ? cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs) : writingSystem.Trim();
                        if (cache.WritingSystemFactory.GetWsFromStr(ws) == 0)
                            throw new GdrSectionRefusal(RefusalCodes.GdrTitleInvalid, $"The writing system '{ws}' is not in this project.");
                        string? Resolve(string? text, string role)
                        {
                            if (text is null) return null;
                            if (list is null || GdrSectionSupport.Find(cache, list, text.Trim()) is not { } found)
                                throw new GdrSectionRefusal(RefusalCodes.GdrNotFound, $"No Grammar Guide section is named '{text}', so it cannot be the {role}.");
                            return CanonicalId.FromGuid(found.Guid).Value;
                        }
                        var parentGuid = Resolve(parent, "parent");
                        var afterGuid = Resolve(after, "section to follow");
                        parentId = parentGuid;
                        var payload = new Dictionary<string, string> { ["id"] = number, ["title"] = title.Trim(), ["ws"] = ws };
                        if (parentGuid is not null) payload["parent"] = parentGuid;
                        if (afterGuid is not null) payload["after"] = afterGuid;
                        var languageProject = CanonicalId.FromGuid(cache.LangProject.Guid);
                        var input = JsonSerializer.SerializeToElement(new { target = entity.Value, number, title = title.Trim(), ws });
                        return (input, new[]
                        {
                            new OperationEnvelope(CanonicalId.Mint(), GdrSectionOperationKinds.Create, entityId: entity, target: languageProject,
                                after: JsonSerializer.SerializeToElement(payload)),
                        });
                    }),
                };
                if (!string.IsNullOrWhiteSpace(markdown))
                    steps.Add(EnsureStandardStylesStep(cache => GdrBodyStyles(cache, entity.Value, projectPath, markdown), stylesCreated));
                if (!string.IsNullOrWhiteSpace(markdown))
                    steps.Add(new ComposeStep(null, SetTextParagraphsComposer.Name, (cache, _) =>
                    {
                        var shown = GdrReader.Show(cache, entity.Value, Path.GetFileNameWithoutExtension(projectPath))
                            ?? throw new InvalidOperationException("The new section is not in the scratch project.");
                        var read = GdrMarkdownImport.Read(shown, Path.GetFileNameWithoutExtension(projectPath), markdown);
                        if (read.HasErrors)
                            throw new GdrSectionRefusal(RefusalCodes.GdrMarkdownInvalid,
                                "The Markdown cannot be read, so nothing was staged: " + string.Join(" ", read.Errors));
                        warnings.AddRange(read.Warnings);
                        if (read.Edits!.Paragraphs is not { } paragraphs)
                            return (JsonSerializer.SerializeToElement(new { }), Array.Empty<OperationEnvelope>());
                        var intent = new SetTextParagraphsIntent(CanonicalId.Parse(entity.Value), paragraphs);
                        return (SetTextParagraphsComposer.Authored(intent), SetTextParagraphsComposer.Build(cache, intent));
                    }));
                steps.Add(new ComposeStep(null, "Validate", (_, _) =>
                    (JsonSerializer.SerializeToElement(new { }), Array.Empty<OperationEnvelope>())));
                if (namedDraft is null) draftName = PendingChanges.EnsurePendingDraft(database, new ProposalRepository(database));
                var composed = ComposeStepsIntoDraft(database, project, draftName, steps);
                if (composed.Succeeded && number is not null && long.TryParse(number.AsSpan(4), out var given)) marks.Raise(given);
                return composed;
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<ComposedOperationsResponse[]>.Refused(ProjectStoreCommand.BusyBeforeSave(projectPath));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<ComposedOperationsResponse[]>.Refused(DraftInvalid(exception.Message, ("draftName", draftName)));
            }
        });
        if (!outcome.Succeeded) return CommandOutcome<GdrSectionStaged>.Refused(outcome.Refusal!);
        var all = outcome.Value!;
        return CommandOutcome<GdrSectionStaged>.Success(new GdrSectionStaged(entity.Value, number ?? "", parentId, listCreated,
            all.Sum(item => item.Operations.Count) > 1 ? ["discussion"] : [], all.Sum(item => item.Operations.Count),
            all.LastOrDefault()?.DraftName ?? namedDraft ?? PendingChanges.DraftName, warnings, stylesCreated.ToArray()));
    }

    private sealed class GdrSectionRefusal(string code, string message, FailureReason reason = FailureReason.InvalidArgument,
        IReadOnlyDictionary<string, string>? facts = null)
        : ProposalRefusalException(code, reason, message, facts ?? new Dictionary<string, string>());
}
