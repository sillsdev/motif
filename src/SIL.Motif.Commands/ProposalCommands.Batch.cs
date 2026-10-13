using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
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
            catch (Exception exception)
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
                    result.ComposerName, result.Operations, result.RelatedObjects)).ToArray(),
                results.Length == 0 ? 0 : results[^1].OperationCount));
        });

    private static List<ComposeStep> ParseBatch(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property => property.Name != "intents") ||
            !root.TryGetProperty("intents", out var intents) || intents.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("A batch is an object with one property, 'intents', an array.");
        if (intents.GetArrayLength() == 0)
            throw new InvalidOperationException("A batch needs at least one intent.");
        var steps = new List<ComposeStep>();
        foreach (var entry in intents.EnumerateArray())
        {
            var index = steps.Count;
            if (entry.ValueKind != JsonValueKind.Object ||
                entry.EnumerateObject().Any(property => property.Name is not ("verb" or "intent" or "citation")) ||
                !entry.TryGetProperty("verb", out var verbValue) || verbValue.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("intent", out var intent))
                throw new InvalidOperationException(
                    $"Intent {index}: each entry has 'verb' (a composing verb) and 'intent', with optional 'citation'.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (entry.EnumerateObject().Any(property => !names.Add(property.Name)))
                throw new InvalidOperationException($"Intent {index}: an entry repeats a property.");
            var verb = verbValue.GetString()!;
            if (!DraftComposers.TryGetValue(verb, out var composer))
                throw new InvalidOperationException(
                    $"Intent {index}: '{verb}' is not a verb a batch can run. Batchable verbs: " +
                    string.Join(", ", DraftComposers.Keys.Order(StringComparer.Ordinal)) + ".");
            var citation = entry.TryGetProperty("citation", out var citationValue)
                ? OperationCitation.Parse(citationValue)
                : null;
            steps.Add(ComposeStep.From(composer, intent.Clone(), citation));
        }
        return steps;
    }
}
