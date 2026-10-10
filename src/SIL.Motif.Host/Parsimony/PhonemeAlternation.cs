using System.Text;

namespace SIL.Motif.Host.Parsimony;

internal sealed record PhonemeGraphemeFact(string PhonemeGuid, string WritingSystem, string Grapheme);

internal enum PhonemeTokenizationStatus
{
    Unique,
    Unmapped,
    Ambiguous,
    OverBound,
}

internal sealed record PhonemeTokenizationResult(PhonemeTokenizationStatus Status,
    IReadOnlyList<string> PhonemeGuids, int SegmentationCount);

internal static class PhonemeTokenizer
{
    internal const int MaximumSegmentations = 16;

    internal static PhonemeTokenizationResult Tokenize(string form, string writingSystem,
        IReadOnlyList<PhonemeGraphemeFact> mappings)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentException.ThrowIfNullOrWhiteSpace(writingSystem);
        ArgumentNullException.ThrowIfNull(mappings);
        if (form is "^0" or "*0" or "&0" or "∅")
            return new PhonemeTokenizationResult(PhonemeTokenizationStatus.Unique, Array.Empty<string>(), 1);
        var normalizedForm = form.Normalize(NormalizationForm.FormD);
        var graphemes = mappings.Where(item => item.WritingSystem == writingSystem && item.Grapheme.Length > 0)
            .Select(item => new PhonemeGraphemeFact(item.PhonemeGuid, item.WritingSystem,
                item.Grapheme.Normalize(NormalizationForm.FormD)))
            .OrderByDescending(item => item.Grapheme.Length)
            .ThenBy(item => item.Grapheme, StringComparer.Ordinal)
            .ThenBy(item => item.PhonemeGuid, StringComparer.Ordinal)
            .ToArray();
        var memo = new Dictionary<int, SegmentationSummary>();

        SegmentationSummary ReadSuffix(int offset)
        {
            if (memo.TryGetValue(offset, out var cached)) return cached;
            if (offset == normalizedForm.Length)
                return memo[offset] = new SegmentationSummary(1, []);
            var count = 0;
            string[]? unique = null;
            foreach (var mapping in graphemes)
            {
                if (!normalizedForm.AsSpan(offset).StartsWith(mapping.Grapheme.AsSpan(), StringComparison.Ordinal))
                    continue;
                var suffix = ReadSuffix(offset + mapping.Grapheme.Length);
                if (suffix.Count == 0) continue;
                if (count == 0 && suffix.Count == 1)
                    unique = [mapping.PhonemeGuid, .. suffix.UniquePath!];
                else
                    unique = null;
                count = Math.Min(MaximumSegmentations + 1, count + suffix.Count);
                if (count > MaximumSegmentations) break;
            }
            return memo[offset] = new SegmentationSummary(count, count == 1 ? unique : null);
        }

        var summary = ReadSuffix(0);
        var status = summary.Count switch
        {
            0 => PhonemeTokenizationStatus.Unmapped,
            1 => PhonemeTokenizationStatus.Unique,
            > MaximumSegmentations => PhonemeTokenizationStatus.OverBound,
            _ => PhonemeTokenizationStatus.Ambiguous,
        };
        return new PhonemeTokenizationResult(status,
            status == PhonemeTokenizationStatus.Unique
                ? Array.AsReadOnly(summary.UniquePath!)
                : Array.Empty<string>(), summary.Count);
    }

    private sealed record SegmentationSummary(int Count, string[]? UniquePath);
}

internal sealed record PhonemeFeatureVector(bool Complete, IReadOnlyDictionary<string, string> Values);

internal enum PhonemeAlignmentStatus
{
    Unique,
    Tied,
    RewriteUnavailable,
    NoAlignment,
}

internal enum PhonemeEditKind
{
    Equal,
    Substitution,
    Insertion,
    Deletion,
}

internal sealed record PhonemeAlignmentStep(PhonemeEditKind Kind, int InputIndex, int OutputIndex,
    string? InputPhonemeGuid, string? OutputPhonemeGuid);

internal sealed record PhonemeAlignmentResult(PhonemeAlignmentStatus Status, long CostNumerator,
    int CostDenominator, IReadOnlyList<PhonemeAlignmentStep> Steps)
{
    internal PhonemeAlignmentStep? SingleEdit => Status == PhonemeAlignmentStatus.Unique &&
        Steps.Count(step => step.Kind != PhonemeEditKind.Equal) == 1
            ? Steps.Single(step => step.Kind != PhonemeEditKind.Equal)
            : null;
}

internal static class PhonemeAligner
{
    internal static bool TryAlignHammingOne(IReadOnlyList<string> input, IReadOnlyList<string> output,
        out PhonemeAlignmentResult alignment)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        alignment = new PhonemeAlignmentResult(PhonemeAlignmentStatus.NoAlignment, 0, 1, []);
        if (input.Count != output.Count) return false;
        var mismatchCount = 0;
        var steps = new PhonemeAlignmentStep[input.Count];
        for (var index = 0; index < input.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(input[index], output[index]))
                steps[index] = new PhonemeAlignmentStep(PhonemeEditKind.Equal, index, index,
                    input[index], output[index]);
            else
            {
                mismatchCount++;
                if (mismatchCount > 1) return false;
                steps[index] = new PhonemeAlignmentStep(PhonemeEditKind.Substitution, index, index,
                    input[index], output[index]);
            }
        }
        if (mismatchCount != 1) return false;
        alignment = new PhonemeAlignmentResult(PhonemeAlignmentStatus.Unique, 0, 1,
            Array.AsReadOnly(steps));
        return true;
    }

    internal static PhonemeAlignmentResult Align(IReadOnlyList<string> input, IReadOnlyList<string> output,
        IReadOnlyDictionary<string, PhonemeFeatureVector> features, int relevantFeatureCount)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(features);
        if (relevantFeatureCount < 0) throw new ArgumentOutOfRangeException(nameof(relevantFeatureCount));
        var scale = Math.Max(1, relevantFeatureCount);
        var known = new AlignmentState?[input.Count + 1, output.Count + 1];
        var possible = new AlignmentState?[input.Count + 1, output.Count + 1];
        known[0, 0] = new AlignmentState(0, 1, null);

        for (var i = 0; i <= input.Count; i++)
        for (var j = 0; j <= output.Count; j++)
        {
            if (i == 0 && j == 0) continue;
            if (i > 0)
            {
                Extend(known[i - 1, j], known, i, j, 2L * scale,
                    new PreviousStep(i - 1, j, PhonemeEditKind.Deletion), false);
                Extend(possible[i - 1, j], possible, i, j, 2L * scale,
                    new PreviousStep(i - 1, j, PhonemeEditKind.Deletion), false);
            }
            if (j > 0)
            {
                Extend(known[i, j - 1], known, i, j, 2L * scale,
                    new PreviousStep(i, j - 1, PhonemeEditKind.Insertion), false);
                Extend(possible[i, j - 1], possible, i, j, 2L * scale,
                    new PreviousStep(i, j - 1, PhonemeEditKind.Insertion), false);
            }
            if (i == 0 || j == 0) continue;
            var left = input[i - 1];
            var right = output[j - 1];
            if (left == right)
            {
                var previous = new PreviousStep(i - 1, j - 1, PhonemeEditKind.Equal);
                Extend(known[i - 1, j - 1], known, i, j, 0, previous, false);
                Extend(possible[i - 1, j - 1], possible, i, j, 0, previous, false);
                continue;
            }
            var leftVector = features.GetValueOrDefault(left);
            var rightVector = features.GetValueOrDefault(right);
            var previousSubstitution = new PreviousStep(i - 1, j - 1, PhonemeEditKind.Substitution);
            if (relevantFeatureCount > 0 && HasAllRelevantFeatures(leftVector, relevantFeatureCount) &&
                HasAllRelevantFeatures(rightVector, relevantFeatureCount))
            {
                var mismatches = leftVector!.Values.Count(pair =>
                    !StringComparer.Ordinal.Equals(pair.Value, rightVector!.Values.GetValueOrDefault(pair.Key)));
                var cost = (long)relevantFeatureCount + mismatches;
                Extend(known[i - 1, j - 1], known, i, j, cost, previousSubstitution, false);
                Extend(possible[i - 1, j - 1], possible, i, j, cost, previousSubstitution, false);
            }
            else
            {
                Extend(known[i - 1, j - 1], possible, i, j, scale,
                    previousSubstitution, true);
                Extend(possible[i - 1, j - 1], possible, i, j, scale,
                    previousSubstitution, true);
            }
        }

        var knownBest = known[input.Count, output.Count];
        var possibleBest = possible[input.Count, output.Count];
        if (possibleBest is not null && (knownBest is null || possibleBest.Cost <= knownBest.Cost))
            return new PhonemeAlignmentResult(PhonemeAlignmentStatus.RewriteUnavailable,
                possibleBest.Cost, scale, []);
        if (knownBest is null)
            return new PhonemeAlignmentResult(PhonemeAlignmentStatus.NoAlignment, 0, scale, []);
        if (knownBest.Count > 1)
            return new PhonemeAlignmentResult(PhonemeAlignmentStatus.Tied, knownBest.Cost, scale, []);

        var steps = Backtrack(known, input, output);
        return new PhonemeAlignmentResult(PhonemeAlignmentStatus.Unique, knownBest.Cost, scale,
            Array.AsReadOnly(steps));
    }

    private static bool HasAllRelevantFeatures(PhonemeFeatureVector? vector, int count) =>
        vector is { Complete: true } && vector.Values.Count == count;

    private static void Extend(AlignmentState? previous, AlignmentState?[,] target, int i, int j, long cost,
        PreviousStep step, bool hasUnknownSubstitution)
    {
        if (previous is null) return;
        var candidate = new AlignmentState(previous.Cost + cost, previous.Count,
            new PreviousStep(step.InputIndex, step.OutputIndex, step.Kind),
            previous.HasUnknownSubstitution || hasUnknownSubstitution);
        var current = target[i, j];
        if (current is null || candidate.Cost < current.Cost)
        {
            target[i, j] = candidate;
            return;
        }
        if (candidate.Cost == current.Cost)
            target[i, j] = current with { Count = Math.Min(2, current.Count + candidate.Count) };
    }

    private static PhonemeAlignmentStep[] Backtrack(AlignmentState?[,] states,
        IReadOnlyList<string> input, IReadOnlyList<string> output)
    {
        var steps = new List<PhonemeAlignmentStep>();
        var i = input.Count;
        var j = output.Count;
        while (i > 0 || j > 0)
        {
            var previous = states[i, j]?.Previous ??
                throw new InvalidOperationException("A unique phoneme alignment has no predecessor.");
            string? inputGuid = null;
            string? outputGuid = null;
            if (previous.Kind is PhonemeEditKind.Equal or PhonemeEditKind.Substitution)
            {
                inputGuid = input[i - 1];
                outputGuid = output[j - 1];
            }
            else if (previous.Kind == PhonemeEditKind.Deletion)
                inputGuid = input[i - 1];
            else
                outputGuid = output[j - 1];
            steps.Add(new PhonemeAlignmentStep(previous.Kind, i - (inputGuid is null ? 0 : 1),
                j - (outputGuid is null ? 0 : 1), inputGuid, outputGuid));
            i = previous.InputIndex;
            j = previous.OutputIndex;
        }
        steps.Reverse();
        return steps.ToArray();
    }

    private sealed record PreviousStep(int InputIndex, int OutputIndex, PhonemeEditKind Kind);

    private sealed record AlignmentState(long Cost, int Count, PreviousStep? Previous,
        bool HasUnknownSubstitution = false);
}
