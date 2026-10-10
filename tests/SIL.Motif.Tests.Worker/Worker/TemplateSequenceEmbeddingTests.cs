using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class TemplateSequenceEmbeddingTests
{
    [Fact]
    public void PrefixCompiledOrderReversesIntoSurfaceOrderWhileSuffixOrderStaysForward()
    {
        var surface = TemplateSequenceEmbedding.InSurfaceOrder(
        [
            Slot("prefix-outer", "prefix", 4),
            Slot("prefix-inner", "prefix", 3),
            Slot("suffix-inner", "suffix", 0),
            Slot("suffix-outer", "suffix", 1),
        ]);

        Assert.Equal(new[] { "prefix-outer", "prefix-inner", "suffix-inner", "suffix-outer" },
            surface.Select(slot => slot.Guid));
    }

    [Fact]
    public void EmbeddingAllowsOmittedOptionalSlotAndReportsOnlyMissingObligations()
    {
        var slots = TemplateSequenceEmbedding.InSurfaceOrder(
        [
            Slot("optional-prefix", "prefix", 0, optional: true, msaGuid: "optional-prefix-msA"),
            Slot("filled-prefix", "prefix", 1, msaGuid: "prefix-msA"),
            Slot("optional-suffix", "suffix", 2, optional: true, msaGuid: "optional-suffix-msA"),
            Slot("filled-suffix", "suffix", 3, msaGuid: "suffix-msA"),
        ]);
        var occurrences = new[]
        {
            new TemplateEmbeddingOccurrence(0, "prefix-msA", "prefix"),
            new TemplateEmbeddingOccurrence(1, "suffix-msA", "suffix"),
        };

        var result = TemplateSequenceEmbedding.Enumerate(occurrences, slots);

        Assert.True(result.Complete);
        var assignment = Assert.Single(result.Embeddings);
        Assert.Equal(new[] { "filled-prefix", "filled-suffix" }, assignment.Select(item => item.SlotGuid));
        Assert.Empty(TemplateSequenceEmbedding.MissingObligatorySlots(slots, assignment));
    }

    [Fact]
    public void EmbeddingEnumeratesAlternativeSlotsWithoutReusingOne()
    {
        var slots = TemplateSequenceEmbedding.InSurfaceOrder(
        [
            Slot("first", "suffix", 0, msaGuid: "same-msa"),
            Slot("second", "suffix", 1, msaGuid: "same-msa"),
            Slot("third", "suffix", 2, msaGuid: "same-msa"),
        ]);
        var occurrences = new[]
        {
            new TemplateEmbeddingOccurrence(0, "same-msa", "suffix"),
            new TemplateEmbeddingOccurrence(1, "same-msa", "suffix"),
        };

        var result = TemplateSequenceEmbedding.Enumerate(occurrences, slots);

        Assert.True(result.Complete);
        Assert.Equal(3, result.Embeddings.Count);
        Assert.All(result.Embeddings, embedding => Assert.Equal(2,
            embedding.Select(item => item.SlotGuid).Distinct(StringComparer.Ordinal).Count()));
    }

    [Fact]
    public void SearchAbstainsWhenMoreThanTheCapWouldBeEnumerated()
    {
        var slots = Enumerable.Range(0, 18).Select(index => Slot("s" + index, "suffix", index,
            msaGuid: "same-msa")).ToArray();
        var occurrences = Enumerable.Range(0, 9).Select(index =>
            new TemplateEmbeddingOccurrence(index, "same-msa", "suffix")).ToArray();

        var result = TemplateSequenceEmbedding.Enumerate(occurrences, slots);

        Assert.False(result.Complete);
        Assert.Equal(TemplateSequenceEmbedding.MaximumEmbeddings, result.Embeddings.Count);
    }

    private static TemplateEmbeddingSlot Slot(string guid, string side, int order, bool optional = false,
        string? msaGuid = null) => new(guid, side, order, optional,
        new HashSet<string>(msaGuid is null ? Array.Empty<string>() : new[] { msaGuid }, StringComparer.Ordinal));
}
