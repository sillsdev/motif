using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>One whole-record edit, addressed through the existing rich-text field operations.</summary>
public sealed record GdrMarkdownEdits(string Target, IReadOnlyList<TextRichStringPayload> RichStrings,
    TextParagraphsPayload? Paragraphs, GdrMarkdownIdentity? Identity = null);

/// <summary>The record identity and list location observed when Markdown was opened.</summary>
public sealed record GdrMarkdownIdentity(string Abbreviation, string? ParentId, int Position);

public static partial class ProposalCommands
{
    /// <summary>Stages every changed field in one Draft save, or leaves its operations unchanged on refusal.</summary>
    public static CommandOutcome<ComposedOperationsResponse[]> StageGdrMarkdown(string projectPath, GdrMarkdownEdits edits) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = PendingChanges.DraftName;
            try
            {
                var target = CanonicalId.Parse(edits.Target);
                var steps = new List<ComposeStep>();
                if (edits.Identity is { } identity)
                    steps.Add(new ComposeStep(null, "Check record", (cache, _) =>
                    {
                        var live = GdrReader.Show(cache, target.Value);
                        if (live is null || live.Record.Abbreviation != identity.Abbreviation ||
                            live.Record.ParentId != identity.ParentId || live.Position != identity.Position)
                            throw new InvalidOperationException("The record ID, parent or list position changed. Reload the Markdown.");
                        return (JsonSerializer.SerializeToElement(new { }), Array.Empty<OperationEnvelope>());
                    }));
                foreach (var payload in edits.RichStrings)
                {
                    var intent = new SetTextRichStringIntent(target, payload);
                    var authored = SetTextRichStringComposer.Authored(intent);
                    steps.Add(new ComposeStep(null, SetTextRichStringComposer.Name,
                        (cache, _) => (authored, SetTextRichStringComposer.Build(cache, intent)),
                        ReplacePrevious: (_, operation) => IsRichStringEdit(operation, target, payload.Field, payload.Ws),
                        Authored: authored, Independent: true));
                }
                if (edits.Paragraphs is { } paragraphs)
                {
                    var intent = new SetTextParagraphsIntent(target, paragraphs);
                    var authored = SetTextParagraphsComposer.Authored(intent);
                    steps.Add(new ComposeStep(null, SetTextParagraphsComposer.Name,
                        (cache, _) => (authored, SetTextParagraphsComposer.Build(cache, intent)),
                        ReplacePrevious: (_, operation) => IsFieldEdit(operation, target, paragraphs.Field),
                        Authored: authored, Independent: true));
                }
                if (edits.RichStrings.Count == 0 && edits.Paragraphs is null)
                    return CommandOutcome<ComposedOperationsResponse[]>.Success([]);
                // The empty step makes the batch validate its last operation against the scratch project.
                steps.Add(new ComposeStep(null, "Validate", (_, _) =>
                    (JsonSerializer.SerializeToElement(new { }), Array.Empty<OperationEnvelope>())));
                draftName = PendingChanges.EnsurePendingDraft(database, new ProposalRepository(database));
                return ComposeStepsIntoDraft(database, project, draftName, steps);
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
}
