using System.Text.Json;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Host.Assess;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Checks bounded producer bytes against the exact inputs of one phonology invocation.</summary>
internal static class PhonologyExamplesReader
{
    internal static PhonologyMeasurement Read(ReadOnlyMemory<byte> bytes,
        IReadOnlyList<PhonologyExample> requested, PhonologyLimits limits, PhonologyInvocationInputs inputs)
    {
        if (bytes.Length > PhonologyInvocationInputs.MaxBytes) throw new JsonException("Output exceeds 32 MiB.");
        using var document = JsonDocument.Parse(bytes);
        Unique(document.RootElement);
        var result = document.RootElement.Deserialize<Response>(PhonologyWire.Options)
            ?? throw new JsonException("Response is null.");
        Require(result.Version == 1, "Unsupported response version.");
        Hash(result.GrammarSha256); Hash(result.RequestSha256); Hash(result.RequestFileSha256);
        Require(result.GrammarSha256 == inputs.GrammarSha256 && result.RequestFileSha256 == inputs.RequestFileSha256,
            "Grammar or request-file binding differs from staged bytes.");
        PhonologyInvocationInputs.Text(result.Build, 256);
        Require(result.GrammarDependencies is not null && result.GrammarDependencies.Length == inputs.Dependencies.Count,
            "Grammar dependencies differ from staged bytes.");
        for (var i = 0; i < result.GrammarDependencies!.Length; i++)
        {
            var dependency = result.GrammarDependencies[i];
            Require(dependency is not null, "Null grammar dependency.");
            Hash(dependency!.Sha256);
            Require(dependency.Path == inputs.Dependencies[i].Path && dependency.Sha256 == inputs.Dependencies[i].Sha256,
                "Grammar dependency binding differs from staged bytes.");
        }
        var effective = result.Limits;
        Require(effective is not null && effective.Scope == "orderedPhonologyFolds" &&
            effective.StepAllocation == "balancedByRequestOrder" && effective.TimeAllocation == "sharedMonotonicDeadline" &&
            effective.StepCap == limits.StepCap && effective.WorkCap == limits.WorkCap && effective.TimeoutMs == limits.TimeoutMs,
            "Requested limits or allocation policy differ.");
        Require(result.Rows is not null && result.Rows.Length == requested.Count, "Rows differ from requested count.");
        var rows = new List<PhonologyMeasuredRow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var totalRules = 0;
        var remainingTime = limits.TimeoutMs;
        for (var i = 0; i < result.Rows!.Length; i++)
        {
            var row = result.Rows[i];
            Require(row is not null, "Null row.");
            var example = requested[i];
            Require(row!.Id == example.Id && ids.Add(row.Id), "Row ids or order differ from request.");
            var context = PhonologyWire.ReadContext(row.RequestedContext);
            var resolved = row.ResolvedContext.ValueKind == JsonValueKind.Null ? null : PhonologyWire.ReadContext(row.ResolvedContext);
            Require(context == example.Context && (resolved is null || resolved == context), "Context binding differs.");
            Require(row.Underlying == example.Underlying && row.Expected is not null &&
                row.Expected.SequenceEqual(example.Expected, StringComparer.Ordinal), "Authored forms differ.");
            Require(row.ExpectedObserved is not null && row.ExpectedObserved.Length == example.Expected.Count,
                "Expectation facts differ from authored count.");
            var rowLimits = row.EffectiveLimits;
            Require(rowLimits is not null && rowLimits.StepCap == Allocation(limits.StepCap, requested.Count, i) &&
                rowLimits.WorkCap == Allocation(limits.WorkCap, requested.Count, i) && rowLimits.TimeoutMs <= remainingTime,
                "Row allocation differs from request-wide ceilings.");
            remainingTime = rowLimits!.TimeoutMs;
            Require(row.Steps <= rowLimits.StepCap && row.WorkSteps <= rowLimits.WorkCap, "Counters exceed allocated ceilings.");
            if (row.Observed is not null) PhonologyInvocationInputs.Text(row.Observed, 512, allowEmpty: true);
            var disposition = Parse<PhonologyDisposition>(row.Disposition);
            var completion = Parse<PhonologyCompletion>(row.Completion);
            var reason = row.Reason is null ? (PhonologyReason?)null : Parse<PhonologyReason>(row.Reason);
            switch (completion)
            {
                case PhonologyCompletion.Complete:
                    Require(resolved is not null && result.MeasuredEntryCount > 0 && row.Observed is not null &&
                        reason is null && !row.Capped && !row.TimedOut &&
                        disposition == (row.ExpectedObserved!.All(b => b) ? PhonologyDisposition.Agree : PhonologyDisposition.Disagree),
                        "Completed row classification or flags are inconsistent.");
                    break;
                case PhonologyCompletion.Incomplete:
                    Require(resolved is not null && result.MeasuredEntryCount > 0 && disposition == PhonologyDisposition.Unavailable &&
                        ((reason is PhonologyReason.StepCap or PhonologyReason.WorkCap && row.Capped && !row.TimedOut) ||
                         (reason == PhonologyReason.Timeout && row.TimedOut)),
                        "Incomplete row reason or flags are inconsistent.");
                    break;
                case PhonologyCompletion.NotStarted:
                    Require(disposition == PhonologyDisposition.Unavailable && !row.Capped && !row.TimedOut &&
                        row.Steps == 0 && row.WorkSteps == 0 && row.Observed is null && !row.ExpectedObserved!.Any(b => b) &&
                        reason is PhonologyReason.EmptyLexicon or PhonologyReason.UnknownEntry or PhonologyReason.AmbiguousContext or
                            PhonologyReason.MsaNotOwnedByEntry or PhonologyReason.InvalidShape or PhonologyReason.InvalidStratum or
                            PhonologyReason.ResultLimit, "Not-started row reason or facts are inconsistent.");
                    Require(result.MeasuredEntryCount != 0 || reason == PhonologyReason.EmptyLexicon,
                        "No measured contexts contradicts the unavailable reason.");
                    Require(reason is not (PhonologyReason.InvalidShape or PhonologyReason.InvalidStratum or PhonologyReason.ResultLimit) ||
                        resolved is not null, "Shape or stratum refusal lacks a resolved context.");
                    Require(reason != PhonologyReason.EmptyLexicon || (result.MeasuredEntryCount == 0 && resolved is null),
                        "Empty lexicon contradicts measured contexts.");
                    Require(reason is not (PhonologyReason.UnknownEntry or PhonologyReason.AmbiguousContext or
                        PhonologyReason.MsaNotOwnedByEntry) || resolved is null, "Unresolved context reason has a resolved context.");
                    break;
            }
            var ordered = Rules(row.OrderedRuleIds, ref totalRules);
            var visited = Rules(row.VisitedRuleIds, ref totalRules);
            var applied = Rules(row.AppliedRuleIds, ref totalRules);
            Require(completion != PhonologyCompletion.NotStarted || (ordered.Count == 0 && visited.Count == 0 && applied.Count == 0),
                "Not-started row has rule activity.");
            rows.Add(new PhonologyMeasuredRow(row.Id, context, resolved, row.Underlying,
                Array.AsReadOnly(row.Expected!), rowLimits, disposition, completion, Array.AsReadOnly(row.ExpectedObserved!),
                row.Observed, reason, row.Steps, row.WorkSteps, row.Capped, row.TimedOut, ordered, visited, applied));
        }
        var counts = result.Counts;
        var completions = result.CompletionCounts;
        Require(counts is not null && counts.Total == (ulong)rows.Count &&
            counts.Agree == Count(rows, PhonologyDisposition.Agree) && counts.Disagree == Count(rows, PhonologyDisposition.Disagree) &&
            counts.Unavailable == Count(rows, PhonologyDisposition.Unavailable), "Disposition counts contradict rows.");
        Require(completions is not null && completions.Complete == Count(rows, PhonologyCompletion.Complete) &&
            completions.Incomplete == Count(rows, PhonologyCompletion.Incomplete) &&
            completions.NotStarted == Count(rows, PhonologyCompletion.NotStarted), "Completion counts contradict rows.");
        return new PhonologyMeasurement(result.GrammarSha256, inputs.Dependencies, result.RequestFileSha256,
            result.RequestSha256, result.Build, inputs.ExecutableSha256, PhonologyInvocationInputs.Digest(bytes.Span), bytes.Length,
            limits, result.MeasuredEntryCount, rows.AsReadOnly());
    }

    private static ulong Count(List<PhonologyMeasuredRow> rows, PhonologyDisposition disposition) =>
        (ulong)rows.Count(r => r.Disposition == disposition);
    private static ulong Count(List<PhonologyMeasuredRow> rows, PhonologyCompletion completion) =>
        (ulong)rows.Count(r => r.Completion == completion);
    internal static ulong Allocation(ulong total, int count, int index) => total / (ulong)count +
        ((ulong)index < total % (ulong)count ? 1UL : 0UL);

    private static IReadOnlyList<string> Rules(string[]? rules, ref int total)
    {
        Require(rules is not null && rules.Length <= 1024, "Rule list exceeds row bound or is null.");
        total += rules!.Length;
        Require(total <= 4096, "Rule lists exceed aggregate bound.");
        foreach (var id in rules) PhonologyInvocationInputs.Text(id, 256);
        return Array.AsReadOnly(rules);
    }

    private static T Parse<T>(string? text) where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
            if (JsonNamingPolicy.CamelCase.ConvertName(value.ToString()) == text) return value;
        throw new JsonException("Unknown phonology tag: " + text);
    }

    private static void Hash(string? hash) => Require(hash is { Length: 64 } &&
        hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Invalid SHA-256 encoding.");
    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new JsonException(detail);
    }

    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), "Duplicate field: " + property.Name);
                Unique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Unique(item);
    }

    private sealed record Response(int Version, string GrammarSha256, Dependency[] GrammarDependencies,
        string RequestSha256, string RequestFileSha256, string Build, Limits Limits, ulong MeasuredEntryCount,
        Row[] Rows, Counts Counts, CompletionCounts CompletionCounts);
    private sealed record Dependency(string Path, string Sha256);
    private sealed record Limits(string Scope, ulong StepCap, ulong WorkCap, uint TimeoutMs,
        string StepAllocation, string TimeAllocation);
    private sealed record Counts(ulong Agree, ulong Disagree, ulong Unavailable, ulong Total);
    private sealed record CompletionCounts(ulong Complete, ulong Incomplete, ulong NotStarted);
    private sealed record Row(string Id, JsonElement RequestedContext, JsonElement ResolvedContext, string Underlying,
        string[] Expected, PhonologyLimits EffectiveLimits, string Disposition, string Completion,
        bool[] ExpectedObserved, string? Observed, string? Reason, ulong Steps, ulong WorkSteps, bool Capped,
        bool TimedOut, string[] OrderedRuleIds, string[] VisitedRuleIds, string[] AppliedRuleIds);
}
