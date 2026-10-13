using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
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
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
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
                entry.EnumerateObject().Any(property => property.Name is not ("verb" or "intent")) ||
                !entry.TryGetProperty("verb", out var verbValue) || verbValue.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("intent", out var intent))
                throw new InvalidOperationException(
                    $"Intent {index}: each entry has exactly 'verb' (a composing verb) and 'intent'.");
            var verb = verbValue.GetString()!;
            if (!DraftComposers.TryGetValue(verb, out var composer))
                throw new InvalidOperationException(
                    $"Intent {index}: '{verb}' is not a verb a batch can run. Batchable verbs: " +
                    string.Join(", ", DraftComposers.Keys.Order(StringComparer.Ordinal)) + ".");
            steps.Add(ComposeStep.From(composer, intent.Clone()));
        }
        return steps;
    }
}
