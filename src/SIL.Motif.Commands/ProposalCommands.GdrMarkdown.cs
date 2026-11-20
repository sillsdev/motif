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

/// <summary>One whole-record edit, addressed through the existing rich-text field operations.</summary>
public sealed record GdrMarkdownEdits(string Target, IReadOnlyList<TextRichStringPayload> RichStrings,
    TextParagraphsPayload? Paragraphs, GdrMarkdownIdentity? Identity = null);

/// <summary>The record identity and list location observed when Markdown was opened.</summary>
public sealed record GdrMarkdownIdentity(string Abbreviation, string? ParentId, int Position);

public static partial class ProposalCommands
{
    /// <summary>
    /// Stages every changed field in one Draft save, or leaves its operations unchanged on refusal. With no
    /// <paramref name="namedDraft"/> the edit joins the pending changes; a named Draft must already exist.
    /// </summary>
    public static CommandOutcome<ComposedOperationsResponse[]> StageGdrMarkdown(string projectPath, GdrMarkdownEdits edits,
        string? namedDraft = null) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = namedDraft ?? PendingChanges.DraftName;
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
                            throw new GdrMarkdownRefusal(RefusalCodes.GdrRecordMoved,
                                "The record ID, parent or list position changed. Reload the Markdown.");
                        return (JsonSerializer.SerializeToElement(new { }), Array.Empty<OperationEnvelope>());
                    }));
                foreach (var payload in edits.RichStrings)
                {
                    var intent = new SetTextRichStringIntent(target, payload);
                    var authored = SetTextRichStringComposer.Authored(intent);
                    if (payload.Field == "CmPossibility.Name")
                    {
                        steps.Add(new ComposeStep(null, "SetGdrTitle", (cache, _) =>
                        {
                            if (string.IsNullOrEmpty(payload.Ws) || payload.Runs.Any(run =>
                                !string.Equals(run.Ws, payload.Ws, StringComparison.OrdinalIgnoreCase) ||
                                run.CharacterStyle is not null || run.ObjData is not null ||
                                run.Props is { } props && (props.Ints.Count != 0 || props.Strings.Count != 0)))
                                throw new InvalidOperationException("The title must be plain text in its writing system.");
                            var live = GdrReader.Show(cache, target.Value);
                            var alternative = live?.TitleAlternatives.FirstOrDefault(item =>
                                string.Equals(item.WritingSystem, payload.Ws, StringComparison.OrdinalIgnoreCase));
                            if (alternative is null) throw new InvalidOperationException("The target is not a Guide section with that writing system.");
                            if (alternative.Text.Normalize(NormalizationForm.FormD) != payload.Expected.Text.Normalize(NormalizationForm.FormD) ||
                                alternative.Digest != payload.Expected.Runs)
                                throw new GdrMarkdownRefusal(RefusalCodes.TextFieldStale, "The CmPossibility.Name title changed since editing began.");
                            var text = payload.Text.Normalize(NormalizationForm.FormD);
                            if (text == alternative.Text.Normalize(NormalizationForm.FormD))
                                return (authored, Array.Empty<OperationEnvelope>());
                            var after = text.Length == 0 ? JsonSerializer.SerializeToElement(new { ws = alternative.WritingSystem })
                                : JsonSerializer.SerializeToElement(new { ws = alternative.WritingSystem, text });
                            var kind = text.Length == 0 ? CmPossibilityNameOperationKinds.ClearName : CmPossibilityNameOperationKinds.SetName;
                            return (authored, new[] { new OperationEnvelope(CanonicalId.Mint(), kind, target: target, after: after) });
                        }, ReplacePrevious: (_, operation) => IsGdrTitleEdit(operation, target, payload.Ws),
                            Authored: authored, Independent: true));
                        continue;
                    }
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
                if (namedDraft is null) draftName = PendingChanges.EnsurePendingDraft(database, new ProposalRepository(database));
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

    /// <summary>Reads the pending title and the original text carried by its whole-record edit.</summary>
    public static CommandOutcome<PendingTextRichStringResponse> PendingGdrTitle(TextRichStringFieldRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion ?? MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new ProposalRepository(database);
            if (repository.FindPendingChangesDraft() is not { DraftName: { } name })
                return CommandOutcome<PendingTextRichStringResponse>.Success(new(null));
            if (!TryLoadDraft(repository, name, out var draft, out string _))
                return CommandOutcome<PendingTextRichStringResponse>.Refused(DraftNotFound(name));
            var target = CanonicalId.Parse(request.Target);
            var operation = ProposalJsonParser.Parse(BuildProposalJson(draft)).Operations.LastOrDefault(item => IsGdrTitleEdit(item, target, request.Ws));
            if (operation is null) return CommandOutcome<PendingTextRichStringResponse>.Success(new(null));
            var provenance = draft.ComposerProvenance.LastOrDefault(item =>
                (ProvenanceOperationIds(item) ?? []).Contains(operation.OperationId.Value));
            if (provenance.ValueKind != JsonValueKind.Object || !provenance.TryGetProperty("input", out var input) ||
                !input.TryGetProperty("expected", out var expected))
                return CommandOutcome<PendingTextRichStringResponse>.Refused(DraftInvalid("The pending title has no starting value."));
            var after = OperationSlotRegistry.After(operation);
            var text = operation.Kind == CmPossibilityNameOperationKinds.SetName ? after.GetProperty("text").GetString()! : "";
            var ws = after.GetProperty("ws").GetString()!;
            return CommandOutcome<PendingTextRichStringResponse>.Success(new(new TextRichStringPayload("CmPossibility.Name", ws,
                text.Length == 0 ? [] : [new TextRun(text, ws)],
                new(expected.GetProperty("text").GetString()!, expected.GetProperty("runs").GetString()!))));
        });

    private static bool IsGdrTitleEdit(OperationEnvelope operation, CanonicalId target, string? ws) =>
        operation.Kind is CmPossibilityNameOperationKinds.SetName or CmPossibilityNameOperationKinds.ClearName &&
        operation.Target is { } actual && CanonicalId.ByGuid.Equals(actual, target) && operation.After is { } after &&
        string.Equals(after.GetProperty("ws").GetString(), ws, StringComparison.OrdinalIgnoreCase);

    internal static Refusal? CheckGdrTitleExpectations(DraftDocument draft, LcmCache cache)
    {
        var operations = ProposalJsonParser.Parse(BuildProposalJson(draft)).Operations;
        foreach (var entry in draft.ComposerProvenance)
        {
            if (!entry.TryGetProperty("composer", out var composer) || composer.GetString() != "SetGdrTitle" ||
                !entry.TryGetProperty("input", out var input) || !input.TryGetProperty("expected", out var expected)) continue;
            var target = CanonicalId.Parse(input.GetProperty("target").GetString()!);
            var ws = input.GetProperty("ws").GetString()!;
            if (!operations.Any(operation => IsGdrTitleEdit(operation, target, ws) &&
                (ProvenanceOperationIds(entry) ?? []).Contains(operation.OperationId.Value))) continue;
            var title = GdrReader.Show(cache, target.Value)?.TitleAlternatives.FirstOrDefault(item =>
                string.Equals(item.WritingSystem, ws, StringComparison.OrdinalIgnoreCase));
            if (title is null || title.Text.Normalize(NormalizationForm.FormD) !=
                expected.GetProperty("text").GetString()!.Normalize(NormalizationForm.FormD) ||
                title.Digest != expected.GetProperty("runs").GetString())
                return new Refusal(RefusalCodes.TextFieldStale, FailureReason.Refused,
                    "The CmPossibility.Name title changed since editing began; nothing was applied.",
                    new Dictionary<string, string> { ["owner"] = target.Value, ["field"] = "CmPossibility.Name", ["ws"] = ws });
        }
        return null;
    }

    private sealed class GdrMarkdownRefusal(string code, string message)
        : ProposalRefusalException(code, FailureReason.Refused, message, new Dictionary<string, string>());
}
