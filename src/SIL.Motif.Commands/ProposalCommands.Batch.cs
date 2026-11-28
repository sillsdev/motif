using SIL.Motif.Projection;
using SIL.Motif.Host.Store;
using System.Text.Json;
using System.Globalization;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Worker.Store;
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
        DiscardEmptyDraftAfterRefusal(request.FwDataPath, request.ProductVersion, request.DraftName,
            ComposeBatchInto(request));

    private static CommandOutcome<ComposedBatchResponse> ComposeBatchInto(ComposeBatchRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            List<ComposeStep> steps;
            try
            {
                var problems = new List<BatchProblem>();
                var intentsJson = request.IntentsJson;
                var expanded = new List<ExpandedEntry>();
                if (intentsJson.Contains("forEach", StringComparison.Ordinal) ||
                    intentsJson.Contains("{target", StringComparison.Ordinal))
                {
                    using var expandScope = ComposeProjectScope.Ensure(database, project);
                    (intentsJson, expanded) = ProjectReadCache.ReadSaved(project, (_, cache) =>
                    {
                        var text = ExpandForEach(request.IntentsJson, request.BaseDirectory, cache, out var found);
                        return (text, found);
                    });
                }
                steps = ParseBatch(intentsJson, problems, out var entries);
                foreach (var (item, position) in expanded.Select((item, position) => (item, position)))
                    if (item.Code is not null && position < entries.Count)
                        problems.Add(new(position, entries[position].Verb, item.Field!, item.Message!, item.Code));
                if (problems.Count == 0 && steps.Count == expanded.Count && expanded.Any(item => item.From is not null))
                    steps = steps.Select((step, position) => step with
                    {
                        ExpandedFrom = expanded[position].From,
                        Target = expanded[position].Target,
                    }).ToList();
                PreflightBatch(project, database, request.DraftName, steps, entries, problems);
                if (problems.Count > 0) throw new BatchPreflightException(problems);
            }
            catch (BatchPreflightException exception)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(PreflightRefusal(exception, request.DraftName));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ContractParseException)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(
                    DraftInvalid(exception.Message, ("draftName", request.DraftName)));
            }
            var advisories = IntentAdvisories(steps);
            var outcome = ComposeStepsIntoDraft(database, project, request.DraftName, steps, describeBatchFailure: true,
                advisories: advisories);
            if (!outcome.Succeeded)
                return CommandOutcome<ComposedBatchResponse>.Refused(outcome.Refusal!);
            return CommandOutcome<ComposedBatchResponse>.Success(
                BatchResponse(request.DraftName, steps, outcome.Value!, advisories));
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
                var problems = new List<BatchProblem>();
                steps = [];
                for (var index = 0; index < request.Steps.Count; index++)
                {
                    var step = request.Steps[index];
                    if (TryBuildStep(problems, index, step.Verb, step.Intent,
                            step.As is null ? null : JsonSerializer.SerializeToElement(step.As), labels,
                            () => step.Cite) is { } built)
                        steps.Add(built);
                }
                if (problems.Count > 0) throw new BatchPreflightException(problems);
            }
            catch (BatchPreflightException exception)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(PreflightRefusal(exception, request.Proposal));
            }
            catch (InvalidOperationException exception)
            {
                return CommandOutcome<ComposedBatchResponse>.Refused(
                    DraftInvalid(exception.Message, ("draftName", request.Proposal)));
            }
            var advisories = IntentAdvisories(steps);
            var outcome = ComposeStepsIntoDraft(database, project, request.Proposal, steps,
                describeBatchFailure: true, remove: request.Remove, advisories: advisories);
            return outcome.Succeeded
                ? CommandOutcome<ComposedBatchResponse>.Success(
                    BatchResponse(request.Proposal, steps, outcome.Value!, advisories))
                : CommandOutcome<ComposedBatchResponse>.Refused(outcome.Refusal!);
        });

    private static ComposedBatchResponse BatchResponse(string draftName, IReadOnlyList<ComposeStep> steps,
        ComposedOperationsResponse[] results, IReadOnlyList<ComposeAdvisory> advisories) => new(draftName,
        results.Select((result, index) => new ComposedIntentResult(index, steps[index].Verb!,
            result.ComposerName, result.Operations, result.RelatedObjects)
        {
            CreatedObjects = result.CreatedObjects,
            NewlyMetRules = result.NewlyMetRules,
            UncheckedRules = result.UncheckedRules,
        }).ToArray(),
        results.Length == 0 ? 0 : results[^1].OperationCount)
    {
        Advisories = advisories.Count == 0 ? null : advisories,
    };

    private const int MaximumLabelLength = 21;
    private const string MetathesisRuleComposerVerb = "compose-author-metathesis-rule";

    private sealed record BatchProblem(int Index, string Verb, string Field, string Message, string? Code = null);

    private sealed record BatchEntry(int Index, string Verb, string? Label, JsonElement? Intent);

    private static List<ComposeStep> ParseBatch(string json, List<BatchProblem> problems, out List<BatchEntry> entries)
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
        entries = [];
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var index = -1;
        foreach (var entry in intents.EnumerateArray())
        {
            index++;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                problems.Add(new(index, "<unknown>", "entry",
                    $"Intent {index}: each entry has 'verb' (a composing verb) and 'intent', with optional 'citation'."));
                entries.Add(new(index, "<unknown>", null, null));
                continue;
            }
            var properties = entry.EnumerateObject().ToArray();
            var verbValue = entry.TryGetProperty("verb", out var suppliedVerb) ? suppliedVerb : default;
            var verb = verbValue.ValueKind == JsonValueKind.String ? verbValue.GetString()! : "<unknown>";
            var label = entry.TryGetProperty("as", out var shapedLabel) && shapedLabel.ValueKind == JsonValueKind.String &&
                IsRulePlacementLabel(shapedLabel.GetString()) ? shapedLabel.GetString() : null;
            var hasIntent = entry.TryGetProperty("intent", out var intent);
            entries.Add(new(index, verb, label, hasIntent ? intent.Clone() : null));
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (properties.Any(property => !names.Add(property.Name)))
            {
                problems.Add(new(index, verb, "entry", $"Intent {index} ({verb}): an entry repeats a property."));
                continue;
            }
            if (properties.Any(property => property.Name is not ("verb" or "intent" or "citation" or "as")))
            {
                problems.Add(new(index, verb, "entry",
                    $"Intent {index} ({verb}): each entry has 'verb' and 'intent', with optional 'citation' and 'as'."));
                continue;
            }
            if (verbValue.ValueKind != JsonValueKind.String || !hasIntent)
            {
                problems.Add(new(index, verb, verbValue.ValueKind != JsonValueKind.String ? "verb" : "intent",
                    $"Intent {index} ({verb}): each entry has 'verb' (a composing verb) and 'intent'."));
                continue;
            }
            if (TryBuildStep(problems, index, verb, intent.Clone(),
                    entry.TryGetProperty("as", out var labelValue) ? labelValue : null,
                    labels, () => entry.TryGetProperty("citation", out var citationValue)
                        ? OperationCitation.Parse(citationValue)
                        : null) is { } step)
                steps.Add(step);
        }
        return steps;
    }

    private static ComposeStep? TryBuildStep(List<BatchProblem> problems, int index, string verb, JsonElement intent,
        JsonElement? labelValue, HashSet<string> labels, Func<OperationCitation?> readCitation)
    {
        try
        {
            return BuildStep(index, verb, intent, labelValue, labels, readCitation);
        }
        catch (BatchEntryException exception)
        {
            problems.Add(new(exception.Index, exception.Verb, exception.Field, exception.Message));
            return null;
        }
    }

    private static ComposeStep BuildStep(int index, string verb, JsonElement intent, JsonElement? labelValue,
        HashSet<string> labels, Func<OperationCitation?> readCitation)
    {
        if (!DraftComposers.TryGetValue(verb, out var composer))
            throw new BatchEntryException(index, verb, "verb",
                $"Intent {index} ({verb}): this is not a verb a batch can run. Batchable verbs: " +
                string.Join(", ", DraftComposers.Keys.Order(StringComparer.Ordinal)) + ".");
        string? label = null;
        if (labelValue is { } supplied)
        {
            if (supplied.ValueKind != JsonValueKind.String || !IsRulePlacementLabel(supplied.GetString()))
                throw new BatchEntryException(index, verb, "as", supplied.ValueKind == JsonValueKind.String
                    ? $"Intent {index} ({verb}): 'as' must be a label matching [A-Za-z][A-Za-z0-9_-]{{0,20}}, " +
                      $"1 to {MaximumLabelLength} characters; '{supplied.GetString()}' has {supplied.GetString()!.Length}."
                    : $"Intent {index} ({verb}): 'as' must be a label matching [A-Za-z][A-Za-z0-9_-]{{0,20}}.");
            label = supplied.GetString();
            if (!labels.Add(label!))
                throw new BatchEntryException(index, verb, "as",
                    $"Intent {index} ({verb}): label '{label}' is already declared in this batch.");
        }
        OperationCitation? citation;
        try
        {
            citation = readCitation();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ContractParseException)
        {
            throw new BatchEntryException(index, verb, "citation", $"Intent {index} ({verb}): {exception.Message}");
        }
        return ComposeStep.From(composer, intent, citation, label, resolvePlacementLabels: true);
    }

    private sealed class BatchEntryException(int index, string verb, string field, string message) : InvalidOperationException(message)
    {
        internal int Index { get; } = index;
        internal string Verb { get; } = verb;
        internal string Field { get; } = field;
    }

    private sealed class BatchPreflightException(IReadOnlyList<BatchProblem> problems)
        : InvalidOperationException(DescribeProblems(problems))
    {
        internal IReadOnlyList<BatchProblem> Problems { get; } = problems;
    }

    private static string DescribeProblems(IReadOnlyList<BatchProblem> problems)
    {
        var ordered = problems.OrderBy(problem => problem.Index).ThenBy(problem => problem.Field, StringComparer.Ordinal).ToArray();
        return ordered.Length == 1
            ? ordered[0].Message
            : $"{ordered.Length} problems found before any intent ran:" +
              string.Concat(ordered.Select(problem => $"{Environment.NewLine}  {problem.Message} [field: {problem.Field}]"));
    }

    private static Refusal PreflightRefusal(BatchPreflightException exception, string draftName)
    {
        var first = exception.Problems.OrderBy(problem => problem.Index).First();
        var refusal = DraftInvalid(exception.Message, ("draftName", draftName),
            ("intentIndex", first.Index.ToString(CultureInfo.InvariantCulture)),
            ("verb", first.Verb), ("field", first.Field),
            ("problemCount", exception.Problems.Count.ToString(CultureInfo.InvariantCulture)),
            ("problemIntents", string.Join(",", exception.Problems.Select(problem => problem.Index).Distinct().Order())));
        return first.Code is null ? refusal : new Refusal(first.Code, refusal.Reason, refusal.Message, refusal.Facts);
    }

    // Read-only: collects every static and placement problem so one refusal lists them all before any stage runs.
    private static void PreflightBatch(ProjectLocator project, MotifDatabase database, string draftName,
        IReadOnlyList<ComposeStep> steps, IReadOnlyList<BatchEntry> entries, List<BatchProblem> problems)
    {
        var declared = entries.Where(entry => entry.Label is not null)
            .GroupBy(entry => entry.Label!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var ruleVerbs = new[] { PhonologicalRuleComposerVerb, MetathesisRuleComposerVerb };
        var ruleSteps = entries.Where(entry => entry.Intent is not null && ruleVerbs.Contains(entry.Verb)).ToArray();
        HashSet<Guid>? known = null;
        var priorRules = 0;
        if (ruleSteps.Length > 0)
        {
            using var composeScope = ComposeProjectScope.Ensure(database, project);
            var repository = new ProposalRepository(database);
            var draftRules = TryLoadDraft(repository, draftName, out var draft, out _)
                ? draft.Operations.Where(operation => operation.Kind == CreatePhonologicalRuleKind && operation.EntityId is not null)
                    .Select(operation => CanonicalId.Parse(operation.EntityId!).ToGuid()).ToArray()
                : [];
            var projectRules = ProjectReadCache.ReadSaved(project, (_, cache) =>
                cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid).ToArray());
            known = projectRules.Concat(draftRules).ToHashSet();
            priorRules = known.Count;
        }
        var rulesSoFar = priorRules;
        foreach (var entry in entries)
        {
            if (entry.Intent is not { } intent || intent.ValueKind != JsonValueKind.Object) continue;
            var isRule = ruleVerbs.Contains(entry.Verb);
            if (isRule)
            {
                if (!intent.TryGetProperty("placement", out var placement) || placement.ValueKind == JsonValueKind.Null)
                {
                    if (rulesSoFar > 0)
                        problems.Add(new(entry.Index, entry.Verb, "placement",
                            $"Intent {entry.Index} ({entry.Verb}): a nonempty rule sequence requires declared placement beside existing rules " +
                            $"({(priorRules > 0 ? "the grammar has rules" : "an earlier rule in this batch creates one")}); name 'after' or 'before' " +
                            "(a rule id or an '@label' of an earlier rule entry with 'as')."));
                }
                else if (placement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var anchor in placement.EnumerateObject().Where(item => item.Name is "after" or "before"))
                        CheckAnchor(entry, anchor.Name, anchor.Value, declared, known, problems);
                }
                rulesSoFar++;
            }
            CheckBatchReferences(entry, intent, declared, problems);
        }
    }

    private static void CheckAnchor(BatchEntry entry, string side, JsonElement value,
        IReadOnlyDictionary<string, BatchEntry> declared, HashSet<Guid>? known, List<BatchProblem> problems)
    {
        var field = $"placement.{side}";
        if (value.ValueKind != JsonValueKind.String) return;
        var text = value.GetString()!;
        if (!text.StartsWith('@'))
        {
            if (known is not null && CanonicalId.TryParse(text, out var id) && !known.Contains(id.ToGuid()))
                problems.Add(new(entry.Index, entry.Verb, field,
                    $"Intent {entry.Index} ({entry.Verb}): Placement {side} '{text}' is not a rule in the grammar or this Draft; rule placement anchors are stale or are not adjacent in the current order."));
            return;
        }
        if (CanonicalId.TryParse(text, out _)) return;
        var label = text[1..];
        if (!IsRulePlacementLabel(label))
        {
            problems.Add(new(entry.Index, entry.Verb, field,
                $"Intent {entry.Index} ({entry.Verb}): Placement {side} label '{text}' is malformed; a label is " +
                $"1 to {MaximumLabelLength} characters matching [A-Za-z][A-Za-z0-9_-]{{0,20}}."));
            return;
        }
        var reason = !declared.TryGetValue(label, out var target) ? "is not declared in this batch"
            : target.Index >= entry.Index ? "is a forward reference"
            : target.Verb != PhonologicalRuleComposerVerb ? "was not created by an earlier rule intent"
            : null;
        if (reason is not null)
            problems.Add(new(entry.Index, entry.Verb, field,
                $"Intent {entry.Index} ({entry.Verb}): Placement {side} label '@{label}' {reason}; use an earlier " +
                $"{PhonologicalRuleComposerVerb} entry with 'as'."));
    }

    private static void CheckBatchReferences(BatchEntry entry, JsonElement intent,
        IReadOnlyDictionary<string, BatchEntry> declared, List<BatchProblem> problems)
    {
        Walk(intent, "intent", false);

        void Walk(JsonElement current, string path, bool textual)
        {
            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in current.EnumerateObject())
                        Walk(property.Value, $"{path}.{property.Name}",
                            textual || LookupReferences.IsTextField(property.Name) || property.Name == "placement");
                    break;
                case JsonValueKind.Array:
                    var position = 0;
                    foreach (var item in current.EnumerateArray()) Walk(item, $"{path}[{position++}]", textual);
                    break;
                case JsonValueKind.String when !textual && current.GetString() is { } text && text.StartsWith('@') &&
                    ParseBatchReference(text) is { } reference && declared.TryGetValue(reference.Label, out var target) &&
                    target.Index >= entry.Index:
                    problems.Add(new(entry.Index, entry.Verb, path,
                        $"Intent {entry.Index} ({entry.Verb}): '{text}' is a forward reference: label '{reference.Label}' " +
                        $"is declared by intent {target.Index}, not an earlier one."));
                    break;
            }
        }
    }

    private static CommandOutcome<ComposedBatchResponse> DiscardEmptyDraftAfterRefusal(string fwDataPath,
        string productVersion, string draftName, CommandOutcome<ComposedBatchResponse> outcome)
    {
        if (outcome.Succeeded || outcome.Refusal!.Code is "draft.not-found" || string.IsNullOrWhiteSpace(draftName))
            return outcome;
        var repository = ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            var record = new ProposalRepository(database).ListDrafts()
                .FirstOrDefault(draft => draft.DraftName == draftName && !draft.IsPendingChanges);
            return CommandOutcome<string[]>.Success(
                record is not null && ProposalLifecycleCommand.IsNeverFinalizedEmptyDraft(record)
                    ? [record.ProposalId.Value, DraftRevision.Compute(record.ProposalJson!)]
                    : []);
        });
        if (!repository.Succeeded || repository.Value is not [var emptyId, var emptyRevision]) return outcome;
        var discarded = DiscardDraft(new DiscardDraftRequest(fwDataPath, productVersion, draftName, emptyRevision), CanonicalId.Parse(emptyId));
        if (!discarded.Succeeded) return outcome;
        var refusal = outcome.Refusal!;
        return CommandOutcome<ComposedBatchResponse>.Refused(new Refusal(refusal.Code, refusal.Reason,
            refusal.Message + $" Draft '{draftName}' held no operations, so it was discarded; run `new --draft {draftName}` again.",
            refusal.Facts, refusal.ParserDiagnostic));
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

    /// <summary>Replaces each declared <c>@label</c> string with an earlier intent's created id.</summary>
    private static JsonElement ResolveBatchLabelReferences(LcmCache cache, JsonElement authored,
        IReadOnlyDictionary<string, IReadOnlyList<CreatedObject>> created,
        IReadOnlyDictionary<string, CanonicalId> principals,
        IReadOnlyDictionary<string, IReadOnlyList<(string Role, string Id)>> roles,
        IReadOnlyDictionary<string, int> declared, int intentIndex)
    {
        using var buffer = new MemoryStream();
        var changed = false;
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Walk(authored, writer);
            writer.Flush();
        }
        if (!changed) return authored;
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();

        void Walk(JsonElement current, Utf8JsonWriter writer, bool textual = false)
        {
            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in current.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        Walk(property.Value, writer, textual || LookupReferences.IsTextField(property.Name));
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in current.EnumerateArray()) Walk(item, writer, textual);
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String when !textual && current.GetString() is { } text &&
                    text.StartsWith('@') && ParseBatchReference(text) is { } reference &&
                    declared.ContainsKey(reference.Label):
                    changed = true;
                    writer.WriteStringValue(ResolveBatchReference(cache, text, reference, created, principals, roles,
                        declared, intentIndex));
                    break;
                default:
                    current.WriteTo(writer);
                    break;
            }
        }
    }

    private static (string Label, string? Kind, string? Name)? ParseBatchReference(string text)
    {
        var match = BatchReference.Match(text);
        if (!match.Success) return null;
        return (match.Groups["label"].Value, match.Groups["kind"].Success ? match.Groups["kind"].Value : null,
            match.Groups["name"].Success ? match.Groups["name"].Value : null);
    }

    private static readonly System.Text.RegularExpressions.Regex BatchReference = new(
        "^@(?<label>[A-Za-z][A-Za-z0-9_-]{0,20})(?::(?<kind>[A-Za-z]+)(?::(?<name>.+))?)?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string ResolveBatchReference(LcmCache cache, string text, (string Label, string? Kind, string? Name) reference,
        IReadOnlyDictionary<string, IReadOnlyList<CreatedObject>> created,
        IReadOnlyDictionary<string, CanonicalId> principals,
        IReadOnlyDictionary<string, IReadOnlyList<(string Role, string Id)>> roles,
        IReadOnlyDictionary<string, int> declared, int intentIndex)
    {
        var label = reference.Label;
        if (declared[label] >= intentIndex)
            throw new InvalidOperationException(
                $"'{text}' is a forward reference: label '{label}' is declared by intent {declared[label]}, not an earlier one.");
        if (!created.TryGetValue(label, out var objects) || objects.Count == 0)
            throw new InvalidOperationException($"'{text}' names label '{label}', whose intent created no object.");
        if (reference.Kind is null)
        {
            if (principals.TryGetValue(label, out var principal)) return principal.Value;
            var distinct = objects.DistinctBy(item => item.Id).ToArray();
            if (distinct.Length == 1) return distinct[0].Id;
            throw new InvalidOperationException(
                $"'{text}' is ambiguous: intent {declared[label]} created {distinct.Length} objects ({DescribeCreated(distinct)}). " +
                $"Name one with '@{label}:<kind>'.");
        }
        // A role such as prefix picks one of several objects of one kind.
        if (reference.Name is null && roles.TryGetValue(label, out var named) &&
            named.Where(item => string.Equals(item.Role, reference.Kind, StringComparison.OrdinalIgnoreCase)).ToArray() is { Length: 1 } byRole)
            return byRole[0].Id;
        var matches = objects.DistinctBy(item => item.Id)
            .Where(item => CreatedKindMatches(item.Kind, reference.Kind)).ToArray();
        if (reference.Name is { } name)
        {
            if (!string.Equals(reference.Kind, "values", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{text}': a named selector requires ':values:<value name>'.");
            matches = matches.Where(item =>
                cache.ServiceLocator.ObjectRepository.GetObject(CanonicalId.Parse(item.Id).ToGuid()) is IFsSymFeatVal value &&
                value.Name.get_String(cache.DefaultAnalWs)?.Text?.Normalize(NormalizationForm.FormD) ==
                    name.Normalize(NormalizationForm.FormD)).ToArray();
        }
        if (matches.Length == 1) return matches[0].Id;
        throw new InvalidOperationException(matches.Length == 0
            ? $"'{text}' matches nothing: intent {declared[label]} created {DescribeCreated(objects)}."
            : $"'{text}' matches {matches.Length} objects ({DescribeCreated(matches)}), so it is ambiguous; " +
              $"name a feature value with '@{label}:values:<value name>', or use a separate labelled entry.");
    }

    private static string CreatedKindName(string kind)
    {
        var name = kind[(kind.LastIndexOf('/') + 1)..];
        return name.StartsWith("create", StringComparison.Ordinal) ? name["create".Length..] : name;
    }

    private static bool CreatedKindMatches(string kind, string wanted) =>
        string.Equals(CreatedKindName(kind), wanted, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(wanted, "msa", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(CreatedKindName(kind), "MorphoSyntaxAnalyses", StringComparison.OrdinalIgnoreCase));

    private static string DescribeCreated(IEnumerable<CreatedObject> objects) => string.Join(", ",
        objects.Select(item => $"{CreatedKindName(item.Kind).ToLowerInvariant()} {item.Id}"));
}
