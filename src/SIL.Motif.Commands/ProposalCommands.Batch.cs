using SIL.Motif.Projection;
using SIL.Motif.Host.Store;
using System.Text.Json;
using System.Globalization;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    private const string PhonologicalRuleComposerVerb = "compose-author-phonological-rule";
    private const string CreatePhonologicalRuleKind = "grammar/phPhonData/createPhonRules";

    /// <summary>
    /// Composes one intent into a Draft through the composer its verb names, through the batch append path so a
    /// citation shares its validation rules.
    /// </summary>
    /// <remarks>
    /// An unknown verb is refused as <c>draft.invalid</c>. A second edit of an earlier request's target replaces
    /// that earlier edit; only within one batch is a second edit of the same target refused.
    /// </remarks>
    public static CommandOutcome<ComposedOperationsResponse> ComposeIntent(ComposeIntentRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                if (!DraftComposers.TryGetValue(request.Verb, out var composer))
                    throw new InvalidOperationException($"'{request.Verb}' is not a composing verb.");
                using var document = JsonDocument.Parse(request.IntentJson);
                return Single(ComposeStepsIntoDraft(database, project, request.DraftName,
                    [ComposeStep.From(composer, document.RootElement.Clone(), request.Citation)]));
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(request.FwDataPath));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(
                    DraftInvalid(exception.Message, ("draftName", request.DraftName)));
            }
        });

    /// <summary>
    /// Composes many intents into one Draft from one project load, through the same composers and checks as
    /// each verb run alone, and appends all of them or none.
    /// </summary>
    /// <remarks>
    /// Entries run in file order and each sees the project as the earlier ones left it. One difference from the
    /// same verbs run one call at a time: when a composer replaces its own earlier edits, a second edit of a target
    /// an earlier entry of the same batch already edited is refused, whereas a later call would replace it. A refusal names the failing
    /// entry's index and verb.
    /// </remarks>
    public static CommandOutcome<ComposedBatchResponse> ComposeBatch(ComposeBatchRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            List<ComposeStep> steps;
            try
            {
                steps = ParseBatch(request.IntentsJson);
            }
            catch (BatchEntryException exception)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(DraftInvalid(exception.Message,
                    ("draftName", request.DraftName),
                    ("intentIndex", exception.Index.ToString(CultureInfo.InvariantCulture)),
                    ("verb", exception.Verb)));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ContractParseException)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(
                    DraftInvalid(exception.Message, ("draftName", request.DraftName)));
            }
            var outcome = ComposeStepsIntoDraft(database, project, request.DraftName, steps, describeBatchFailure: true);
            if (!outcome.Succeeded)
                return CommandOutcome<ComposedBatchResponse>.Refused(outcome.Refusal!);
            return CommandOutcome<ComposedBatchResponse>.Success(BatchResponse(request.DraftName, steps, outcome.Value!));
        });

    /// <summary>
    /// Removes operations from a Draft and composes any number of steps into it, each through the composer its
    /// verb names, all in one save: every removal and every step stands, or none does.
    /// </summary>
    /// <remarks>
    /// One step behaves as <see cref="ComposeIntent"/> does for its verb; many behave as <see cref="ComposeBatch"/>
    /// does, so a later step sees the project as the earlier ones left it and may place a rule after an earlier
    /// step's <c>as</c> label. A removal that would orphan a dependent operation is refused, never forced. A refusal
    /// names the failing step's index and verb.
    /// </remarks>
    public static CommandOutcome<ComposedBatchResponse> Compose(ComposeRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            List<ComposeStep> steps;
            try
            {
                if (request.Steps.Count == 0)
                    throw new InvalidOperationException("A compose call needs at least one step.");
                var labels = new HashSet<string>(StringComparer.Ordinal);
                steps = request.Steps.Select((step, index) => BuildStep(index, step.Verb, step.Intent,
                    step.As is null ? null : JsonSerializer.SerializeToElement(step.As), labels,
                    () => step.Cite)).ToList();
            }
            catch (BatchEntryException exception)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(DraftInvalid(exception.Message,
                    ("draftName", request.Proposal),
                    ("intentIndex", exception.Index.ToString(CultureInfo.InvariantCulture)),
                    ("verb", exception.Verb)));
            }
            catch (InvalidOperationException exception)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(
                    DraftInvalid(exception.Message, ("draftName", request.Proposal)));
            }
            var outcome = ComposeStepsIntoDraft(database, project, request.Proposal, steps,
                describeBatchFailure: true, remove: request.Remove);
            return outcome.Succeeded
                ? CommandOutcome<ComposedBatchResponse>.Success(BatchResponse(request.Proposal, steps, outcome.Value!))
                : CommandOutcome<ComposedBatchResponse>.Refused(outcome.Refusal!);
        });

    private static ComposedBatchResponse BatchResponse(string draftName, IReadOnlyList<ComposeStep> steps,
        ComposedOperationsResponse[] results) => new(draftName,
        results.Select((result, index) => new ComposedIntentResult(index, steps[index].Verb!,
            result.ComposerName, result.Operations, result.RelatedObjects)
        {
            CreatedObjects = result.CreatedObjects,
        }).ToArray(),
        results.Length == 0 ? 0 : results[^1].OperationCount);

    private static List<ComposeStep> ParseBatch(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("A batch is an object with one property, 'intents', an array.");
        var rootProperties = root.EnumerateObject().ToArray();
        if (rootProperties.Length != 1 || rootProperties[0].Name != "intents" ||
            !root.TryGetProperty("intents", out var intents) || intents.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("A batch is an object with one property, 'intents', an array.");
        if (intents.GetArrayLength() == 0)
            throw new InvalidOperationException("A batch needs at least one intent.");
        var steps = new List<ComposeStep>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in intents.EnumerateArray())
        {
            var index = steps.Count;
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"Intent {index}: each entry has 'verb' (a composing verb) and 'intent', with optional 'citation'.");
            var properties = entry.EnumerateObject().ToArray();
            var verbValue = entry.TryGetProperty("verb", out var suppliedVerb) ? suppliedVerb : default;
            var verb = verbValue.ValueKind == JsonValueKind.String ? verbValue.GetString()! : "<unknown>";
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (properties.Any(property => !names.Add(property.Name)))
                throw new BatchEntryException(index, verb, $"Intent {index} ({verb}): an entry repeats a property.");
            if (properties.Any(property => property.Name is not ("verb" or "intent" or "citation" or "as")))
                throw new BatchEntryException(index, verb,
                    $"Intent {index} ({verb}): each entry has 'verb' and 'intent', with optional 'citation' and 'as'.");
            if (verbValue.ValueKind != JsonValueKind.String || !entry.TryGetProperty("intent", out var intent))
                throw new BatchEntryException(index, verb,
                    $"Intent {index} ({verb}): each entry has 'verb' (a composing verb) and 'intent'.");
            steps.Add(BuildStep(index, verb, intent.Clone(), entry.TryGetProperty("as", out var labelValue) ? labelValue : null,
                labels, () => entry.TryGetProperty("citation", out var citationValue)
                    ? OperationCitation.Parse(citationValue)
                    : null));
        }
        return steps;
    }

    private static ComposeStep BuildStep(int index, string verb, JsonElement intent, JsonElement? labelValue,
        HashSet<string> labels, Func<OperationCitation?> readCitation)
    {
        if (!DraftComposers.TryGetValue(verb, out var composer))
            throw new BatchEntryException(index, verb,
                $"Intent {index} ({verb}): this is not a verb a batch can run. Batchable verbs: " +
                string.Join(", ", DraftComposers.Keys.Order(StringComparer.Ordinal)) + ".");
        string? label = null;
        if (labelValue is { } supplied)
        {
            if (verb != PhonologicalRuleComposerVerb)
                throw new BatchEntryException(index, verb,
                    $"Intent {index} ({verb}): 'as' labels are supported only for {PhonologicalRuleComposerVerb}.");
            if (supplied.ValueKind != JsonValueKind.String || !IsRulePlacementLabel(supplied.GetString()))
                throw new BatchEntryException(index, verb,
                    $"Intent {index} ({verb}): 'as' must be a label matching [A-Za-z][A-Za-z0-9_-]{{0,20}}.");
            label = supplied.GetString();
            if (!labels.Add(label!))
                throw new BatchEntryException(index, verb,
                    $"Intent {index} ({verb}): label '{label}' is already declared in this batch.");
        }
        OperationCitation? citation;
        try
        {
            citation = readCitation();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ContractParseException)
        {
            throw new BatchEntryException(index, verb, $"Intent {index} ({verb}): {exception.Message}");
        }
        return ComposeStep.From(composer, intent, citation, label, resolvePlacementLabels: true);
    }

    private sealed class BatchEntryException(int index, string verb, string message) : InvalidOperationException(message)
    {
        internal int Index { get; } = index;
        internal string Verb { get; } = verb;
    }

    private static bool IsRulePlacementLabel(string? value)
    {
        if (value is null || value.Length is < 1 or > 21 ||
            !((value[0] is >= 'A' and <= 'Z') || (value[0] is >= 'a' and <= 'z')))
            return false;
        return value.Skip(1).All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    }

    private static JsonElement ResolveRulePlacementLabels(JsonElement authored,
        IReadOnlyDictionary<string, CanonicalId> available, IReadOnlyDictionary<string, int> declared,
        int intentIndex)
    {
        if (authored.ValueKind != JsonValueKind.Object) return authored;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in authored.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name == "placement" && property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (var anchor in property.Value.EnumerateObject())
                    {
                        writer.WritePropertyName(anchor.Name);
                        if (anchor.Name is ("after" or "before") && anchor.Value.ValueKind == JsonValueKind.String &&
                            anchor.Value.GetString() is { } value && value.StartsWith('@'))
                        {
                            if (CanonicalId.TryParse(value, out _))
                            {
                                writer.WriteStringValue(value);
                                continue;
                            }
                            var label = value[1..];
                            if (!IsRulePlacementLabel(label))
                                throw new InvalidOperationException(
                                    $"Placement {anchor.Name} label '{value}' is malformed.");
                            if (!available.TryGetValue(label, out var entityId))
                            {
                                var reason = declared.TryGetValue(label, out var declaredAt)
                                    ? declaredAt >= intentIndex ? "is a forward reference" : "was not created by an earlier intent"
                                    : "is not declared in this batch";
                                throw new InvalidOperationException(
                                    $"Placement {anchor.Name} label '@{label}' {reason}; use an earlier " +
                                    $"{PhonologicalRuleComposerVerb} entry with 'as'.");
                            }
                            writer.WriteStringValue(entityId.Value);
                        }
                        else
                        {
                            anchor.Value.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            writer.Flush();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}
