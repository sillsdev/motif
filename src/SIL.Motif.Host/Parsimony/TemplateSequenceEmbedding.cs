namespace SIL.Motif.Host.Parsimony;

internal sealed record TemplateEmbeddingSlot(string Guid, string Side, int CompiledOrder, bool Optional,
    IReadOnlySet<string> MsaGuids);

internal sealed record TemplateEmbeddingOccurrence(int MorphOrdinal, string MsaGuid, string Side);

internal sealed record TemplateEmbeddingAssignment(int MorphOrdinal, string SlotGuid);

internal sealed record TemplateEmbeddingSearch(bool Complete,
    IReadOnlyList<IReadOnlyList<TemplateEmbeddingAssignment>> Embeddings);

internal static class TemplateSequenceEmbedding
{
    internal const int MaximumEmbeddings = 256;

    internal static IReadOnlyList<TemplateEmbeddingSlot> InSurfaceOrder(
        IEnumerable<TemplateEmbeddingSlot> slots) => Array.AsReadOnly(slots
        .OrderBy(slot => slot.Side, StringComparer.Ordinal)
        .ThenBy(slot => slot.Side == "prefix" ? -slot.CompiledOrder : slot.CompiledOrder)
        .ThenBy(slot => slot.Guid, StringComparer.Ordinal).ToArray());

    internal static TemplateEmbeddingSearch Enumerate(IReadOnlyList<TemplateEmbeddingOccurrence> occurrences,
        IReadOnlyList<TemplateEmbeddingSlot> slots, int maximumEmbeddings = MaximumEmbeddings)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEmbeddings);

        var candidates = occurrences.Select(occurrence => slots.Select((slot, index) => (slot, index))
            .Where(candidate => candidate.slot.Side == occurrence.Side &&
                candidate.slot.MsaGuids.Contains(occurrence.MsaGuid))
            .Select(candidate => candidate.index).ToArray()).ToArray();
        if (candidates.Any(candidate => candidate.Length == 0))
            return new TemplateEmbeddingSearch(true, []);

        var embeddings = new List<IReadOnlyList<TemplateEmbeddingAssignment>>();
        var selected = new int[occurrences.Count];
        var lastBySide = new Dictionary<string, int>(StringComparer.Ordinal);
        var exceeded = false;

        void Search(int occurrenceIndex)
        {
            if (exceeded) return;
            if (occurrenceIndex == occurrences.Count)
            {
                if (embeddings.Count == maximumEmbeddings)
                {
                    exceeded = true;
                    return;
                }
                embeddings.Add(Array.AsReadOnly(occurrences.Select((occurrence, index) =>
                    new TemplateEmbeddingAssignment(occurrence.MorphOrdinal, slots[selected[index]].Guid)).ToArray()));
                return;
            }

            var occurrence = occurrences[occurrenceIndex];
            var lastIndex = lastBySide.GetValueOrDefault(occurrence.Side, -1);
            foreach (var candidateIndex in candidates[occurrenceIndex])
            {
                if (candidateIndex <= lastIndex) continue;
                lastBySide[occurrence.Side] = candidateIndex;
                selected[occurrenceIndex] = candidateIndex;
                Search(occurrenceIndex + 1);
                if (lastIndex < 0) lastBySide.Remove(occurrence.Side);
                else lastBySide[occurrence.Side] = lastIndex;
                if (exceeded) return;
            }
        }

        Search(0);
        return new TemplateEmbeddingSearch(!exceeded, Array.AsReadOnly(embeddings.ToArray()));
    }

    internal static IReadOnlyList<string> MissingObligatorySlots(IReadOnlyList<TemplateEmbeddingSlot> slots,
        IReadOnlyList<TemplateEmbeddingAssignment> assignment)
    {
        var filled = assignment.Select(item => item.SlotGuid).ToHashSet(StringComparer.Ordinal);
        return Array.AsReadOnly(slots.Where(slot => !slot.Optional && !filled.Contains(slot.Guid))
            .Select(slot => slot.Guid).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
