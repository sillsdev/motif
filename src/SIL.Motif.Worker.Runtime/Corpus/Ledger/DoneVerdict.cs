using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Worker.Corpus.Ledger;

internal enum DoneVerdictKind { Pass, Fail, Unmeasured }

internal sealed record DoneTest(DoneVerdictKind Verdict, IReadOnlyList<string> Reasons, IReadOnlyDictionary<string, long> Counts)
{
    public static DoneTest Unmeasured(params string[] reasons) => new(DoneVerdictKind.Unmeasured, reasons, new Dictionary<string, long>());
}

internal sealed record DoneResult(DoneTest Accounted, DoneTest Correct, DoneTest Stable)
{
    public static DoneResult Unmeasured { get; } = new(DoneTest.Unmeasured("not-computed"), DoneTest.Unmeasured("not-computed"),
        DoneTest.Unmeasured("not-computed"));

    public DoneVerdictKind Verdict => Conjunction(Accounted.Verdict, Correct.Verdict, Stable.Verdict);

    public static DoneVerdictKind Conjunction(params DoneVerdictKind[] tests) =>
        tests.Contains(DoneVerdictKind.Fail) ? DoneVerdictKind.Fail
        : tests.All(test => test == DoneVerdictKind.Pass) ? DoneVerdictKind.Pass : DoneVerdictKind.Unmeasured;
}

/// <summary>Accounted from the report; Correct and Stable from their frozen artifacts when present.</summary>
internal static class DoneVerdict
{
    public const int MinimumSample = 1500;

    public static DoneResult Compute(LedgerCoverageReport report, LedgerCoverageInputs inputs) =>
        new(Accounted(report), Correct(report, inputs.AuditSampleDirectory), Stable(report, inputs.ProtectedSetPath));

    public static DoneTest Accounted(LedgerCoverageReport report)
    {
        var counts = new Dictionary<string, long>
        {
            ["recurringOpenTypes"] = report.RecurringOpenCurrent.Types + report.RecurringOpenStale.Types,
            ["recurringOpenTokens"] = report.RecurringOpenCurrent.Tokens + report.RecurringOpenStale.Tokens,
            ["recurringOpenCurrentTypes"] = report.RecurringOpenCurrent.Types,
            ["recurringOpenStaleTypes"] = report.RecurringOpenStale.Types,
            ["openSingletonTypes"] = report.OpenSingletons.Types,
            ["openSingletonTokens"] = report.OpenSingletons.Tokens,
            ["unresolvedRecurringTypes"] = report.UnresolvedRecurring.Types,
        };
        counts["recurringLostStaleTypes"] = report.RecurringLostStale.Types;
        counts["recurringUnsupportedDispositionTypes"] = report.RecurringUnsupportedDisposition.Types;
        // Only a current Open proves a failure; a stale Lost, like any stale Open, is unproved until rechecked.
        if (report.RecurringOpenCurrent.Types > 0)
            return new(DoneVerdictKind.Fail, ["recurring-open-current"], counts);
        List<string> reasons = [];
        if (report.Join.Blocked) reasons.AddRange(report.Join.Blocks.Select(block => "join-" + block));
        if (report.Join.MissingTypes > 0) reasons.Add("ledger-rows-missing");
        if (report.Join.DuplicateTypes > 0) reasons.Add("duplicate-normalized-keys");
        if (report.Join.RevisionTypes > 0) reasons.Add("incompatible-revisions");
        if (report.Join.ConflictTypes > 0) reasons.Add("conflicting-weights");
        if (report.RecurringOpenStale.Types - report.RecurringUnsupportedDisposition.Types - report.RecurringLostStale.Types > 0)
            reasons.Add("recurring-open-unproved");
        if (report.RecurringLostStale.Types > 0) reasons.Add("recurring-lost-stale");
        if (report.RecurringUnsupportedDisposition.Types > 0) reasons.Add("recurring-disposition-in-review");
        if (report.UnresolvedRecurring.Types > 0) reasons.Add("recurring-types-without-current-check");
        return reasons.Count == 0 ? new(DoneVerdictKind.Pass, [], counts) : new(DoneVerdictKind.Unmeasured, reasons, counts);
    }

    public static DoneTest Correct(LedgerCoverageReport report, string? directory)
    {
        var manifestPath = directory is null ? null : Path.Combine(directory, "manifest.json");
        if (manifestPath is null || !File.Exists(manifestPath)) return DoneTest.Unmeasured("audit-sample-absent");
        try
        {
            return CorrectFrom(report, directory!, manifestPath);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or KeyNotFoundException
            or InvalidOperationException or FormatException)
        {
            return DoneTest.Unmeasured("audit-sample-malformed");
        }
    }

    private static DoneTest CorrectFrom(LedgerCoverageReport report, string directory, string manifestPath)
    {
        using var manifestDocument = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var manifest = manifestDocument.RootElement;
        string[] required = ["membershipDigest", "frequencySha256", "tokenizerSha256", "samplingMethod", "seed", "selectionDigest",
            "scorerId", "scoringPolicyVersion", "referenceRevision", "referenceDigest"];
        if (required.Any(key => !manifest.TryGetProperty(key, out var value) || Text(value) is not { Length: > 0 }) ||
            !manifest.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0)
            return DoneTest.Unmeasured("audit-pins-incomplete");
        string Pin(string key) => manifest.GetProperty(key).GetString() ?? Text(manifest.GetProperty(key))!;
        if (!string.Equals(Pin("frequencySha256"), report.Stamp.FrequencySha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Pin("tokenizerSha256"), report.Stamp.TokenizerSha256, StringComparison.OrdinalIgnoreCase))
            return DoneTest.Unmeasured("audit-pins-mismatch");

        var selected = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(directory, "selection.jsonl")).Where(line => line.Length > 0))
        {
            using var document = JsonDocument.Parse(line);
            selected.Add(document.RootElement.GetProperty("occurrenceId").GetString()!);
        }
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(selected.Select(id => id + "\n")))))
            .ToLowerInvariant();
        if (selected.Count != selected.Distinct(StringComparer.Ordinal).Count() ||
            !string.Equals(digest, Pin("selectionDigest"), StringComparison.OrdinalIgnoreCase))
            return DoneTest.Unmeasured("audit-selection-digest-mismatch");
        if (selected.Count < MinimumSample) return DoneTest.Unmeasured("audit-sample-too-small");

        var chosen = selected.ToHashSet(StringComparer.Ordinal);
        var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicting = new HashSet<string>(StringComparer.Ordinal);
        long extra = 0;
        foreach (var line in File.ReadLines(Path.Combine(directory, "outcomes.jsonl")).Where(line => line.Length > 0))
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            var id = row.GetProperty("occurrenceId").GetString()!;
            if (!chosen.Contains(id)) { extra++; continue; }
            var bound = Text(row.GetProperty("selectionDigest")) == Pin("selectionDigest") &&
                Text(row.GetProperty("referenceDigest")) == Pin("referenceDigest") &&
                Text(row.GetProperty("grammarRevision")) == report.Stamp.GrammarRevision;
            var outcome = bound ? row.GetProperty("outcome").GetString()! : "unmeasured";
            if (!outcomes.TryAdd(id, outcome)) conflicting.Add(id);
        }
        long right = 0, wrongOnly = 0, miss = 0;
        foreach (var id in selected)
            switch (conflicting.Contains(id) ? "unmeasured" : outcomes.GetValueOrDefault(id, "unmeasured"))
            {
                case "right": right++; break;
                case "wrong-only": wrongOnly++; break;
                case "miss": miss++; break;
            }
        var size = (long)selected.Count;
        var unmeasured = size - right - wrongOnly - miss;
        var counts = new Dictionary<string, long>
        {
            ["size"] = size, ["right"] = right, ["wrongOnly"] = wrongOnly, ["miss"] = miss, ["unmeasured"] = unmeasured,
            ["extraOutcomes"] = extra,
        };
        if (wrongOnly * 100 > size) return new(DoneVerdictKind.Fail, ["wrong-only-above-1-percent"], counts);
        if ((right + unmeasured) * 100 < size * 98) return new(DoneVerdictKind.Fail, ["right-below-98-percent"], counts);
        if (extra > 0) return new(DoneVerdictKind.Unmeasured, ["extra-outcomes-residual"], counts);
        return right * 100 >= size * 98 ? new(DoneVerdictKind.Pass, [], counts)
            : new(DoneVerdictKind.Unmeasured, ["unmeasured-outcomes"], counts);
    }

    public static DoneTest Stable(LedgerCoverageReport report, string? path)
    {
        if (path is null || !File.Exists(path)) return DoneTest.Unmeasured("protected-set-absent");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var pairs = root.GetProperty("pairs").EnumerateArray()
                .Select(pair => (Type: LedgerJoiner.Key(pair.GetProperty("type").GetString()!), Reading: pair.GetProperty("reading").GetString()!))
                .OrderBy(pair => pair.Type, StringComparer.Ordinal).ThenBy(pair => pair.Reading, StringComparer.Ordinal).ToArray();
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Concat(pairs.Select(pair => pair.Type + "\t" + pair.Reading + "\n"))))).ToLowerInvariant();
            if (!string.Equals(digest, root.GetProperty("digest").GetString(), StringComparison.OrdinalIgnoreCase) || pairs.Length == 0 ||
                pairs.Distinct().Count() != pairs.Length)
                return DoneTest.Unmeasured("protected-set-digest-mismatch");
            if (root.GetProperty("grammarRevision").GetString() != report.Stamp.GrammarRevision)
                return DoneTest.Unmeasured("protected-set-revision-mismatch");
            var rows = report.Rows.ToDictionary(row => row.Type.Key, StringComparer.Ordinal);
            long lost = 0, unmeasured = 0, kept = 0;
            foreach (var (type, reading) in pairs)
            {
                var check = rows.GetValueOrDefault(type)?.Check;
                if (check is not { Completeness: CheckCompleteness.Complete, Comparable: true, ProducedReadings: { } produced })
                    unmeasured++;
                else if (produced.Contains(reading)) kept++;
                else lost++;
            }
            var counts = new Dictionary<string, long>
            {
                ["pairs"] = pairs.Length, ["kept"] = kept, ["lost"] = lost, ["unmeasured"] = unmeasured,
            };
            return lost > 0 ? new(DoneVerdictKind.Fail, ["protected-reading-lost"], counts)
                : unmeasured > 0 ? new(DoneVerdictKind.Unmeasured, ["protected-comparison-unmeasured"], counts)
                : new(DoneVerdictKind.Pass, [], counts);
        }
        catch (Exception exception) when (exception is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            return DoneTest.Unmeasured("protected-set-malformed");
        }
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };
}
