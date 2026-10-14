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

    /// <summary>Runs one CLI composer through the batch append path so citations share its validation rules.</summary>
    /// <param name="fwDataPath">The saved project this Draft belongs to.</param>
    /// <param name="productVersion">The Motif product version used to open the paired store.</param>
    /// <param name="draftName">The Draft that receives the composed operations.</param>
    /// <param name="verb">The CLI composing verb that selects the composer.</param>
    /// <param name="intentJson">The closed semantic intent accepted by that composer.</param>
    /// <param name="citation">The optional source copied to every operation the composer creates.</param>
    internal static CommandOutcome<ComposedOperationsResponse> ComposeIntentForCli(
        string fwDataPath, string productVersion, string draftName, string verb, string intentJson,
        OperationCitation? citation) =>
        ProjectStoreCommand.Run(fwDataPath, productVersion, (database, project) =>
        {
            try
            {
                if (!DraftComposers.TryGetValue(verb, out var composer))
                    throw new InvalidOperationException($"'{verb}' is not a composing verb.");
                using var document = JsonDocument.Parse(intentJson);
                return Single(ComposeStepsIntoDraft(database, project, draftName,
                    [ComposeStep.From(composer, document.RootElement.Clone(), citation)]));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException &&
                !SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(
                    DraftInvalid(exception.Message, ("draftName", draftName)));
            }
        });

    /// <summary>
    /// Composes many intents into one Draft from one project load, through the same composers and checks as
    /// each verb run alone, and appends all of them or none.
    /// </summary>
    /// <remarks>
    /// Entries run in file order and each sees the project as the earlier ones left it, so the Draft ends as it
    /// would after the same verbs one call at a time. A refusal names the failing entry's index and verb.
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
            var results = outcome.Value!;
            return CommandOutcome<ComposedBatchResponse>.Success(new ComposedBatchResponse(request.DraftName,
                results.Select((result, index) => new ComposedIntentResult(index, steps[index].Verb!,
                    result.ComposerName, result.Operations, result.RelatedObjects)
                {
                    CreatedObjects = result.CreatedObjects,
                }).ToArray(),
                results.Length == 0 ? 0 : results[^1].OperationCount));
        });

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
            if (!DraftComposers.TryGetValue(verb, out var composer))
                throw new BatchEntryException(index, verb,
                    $"Intent {index} ({verb}): this is not a verb a batch can run. Batchable verbs: " +
                    string.Join(", ", DraftComposers.Keys.Order(StringComparer.Ordinal)) + ".");
            string? label = null;
            if (entry.TryGetProperty("as", out var labelValue))
            {
                if (verb != PhonologicalRuleComposerVerb)
                    throw new BatchEntryException(index, verb,
                        $"Intent {index} ({verb}): 'as' labels are supported only for {PhonologicalRuleComposerVerb}.");
                if (labelValue.ValueKind != JsonValueKind.String || !IsRulePlacementLabel(labelValue.GetString()))
                    throw new BatchEntryException(index, verb,
                        $"Intent {index} ({verb}): 'as' must be a label matching [A-Za-z][A-Za-z0-9_-]{{0,20}}.");
                label = labelValue.GetString();
                if (!labels.Add(label!))
                    throw new BatchEntryException(index, verb,
                        $"Intent {index} ({verb}): label '{label}' is already declared in this batch.");
            }
            OperationCitation? citation;
            try
            {
                citation = entry.TryGetProperty("citation", out var citationValue)
                    ? OperationCitation.Parse(citationValue)
                    : null;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ContractParseException)
            {
                throw new BatchEntryException(index, verb, $"Intent {index} ({verb}): {exception.Message}");
            }
            steps.Add(ComposeStep.From(composer, intent.Clone(), citation, label, resolvePlacementLabels: true));
        }
        return steps;
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
