using System.Collections.Immutable;

namespace SIL.Motif.RecordStore;

public sealed record NativeProvenance(long EncounterOrdinal, string TraversalVersion,
    ImmutableDictionary<int, ImmutableArray<Guid>> Vectors)
{
    public ImmutableDictionary<string, long> RepositoryOrdinals { get; init; } = ImmutableDictionary<string, long>.Empty;
}
