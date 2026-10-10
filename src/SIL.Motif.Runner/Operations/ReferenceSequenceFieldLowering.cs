using System.Collections.Generic;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

internal static class ReferenceSequenceFieldLowering
{
    public static void ApplyAddRef<TRef>(LcmCache cache, IList<TRef> sequence, CanonicalId memberId,
        Placement? placement, string kind) where TRef : ICmObject
    {
        var member = ReferenceFieldLowering.Resolve<TRef>(cache, memberId, kind);
        var currentIndex = IndexOf(sequence, memberId);
        if (currentIndex >= 0 && placement is null) return;

        var remaining = new List<TRef>(sequence.Count);
        for (var index = 0; index < sequence.Count; index++)
        {
            if (index != currentIndex) remaining.Add(sequence[index]);
        }

        var insertionIndex = InsertionIndex(remaining, placement, kind);
        if (currentIndex >= 0)
        {
            if (currentIndex == insertionIndex) return;
            throw new InvalidOperationException(
                $"'{kind}': the member is already present at another position; use its move kind.");
        }

        sequence.Insert(insertionIndex, member);
    }

    public static void ApplyMove<TRef>(IList<TRef> sequence, CanonicalId memberId,
        Placement? placement, string kind) where TRef : ICmObject
    {
        var currentIndex = IndexOf(sequence, memberId);
        if (currentIndex < 0)
            throw new InvalidOperationException($"'{kind}': the member to move is absent from the sequence.");

        var remaining = new List<TRef>(sequence.Count - 1);
        for (var index = 0; index < sequence.Count; index++)
        {
            if (index != currentIndex) remaining.Add(sequence[index]);
        }

        var insertionIndex = InsertionIndex(remaining, placement, kind);
        if (currentIndex == insertionIndex) return;

        var member = sequence[currentIndex];
        sequence.RemoveAt(currentIndex);
        sequence.Insert(insertionIndex, member);
    }

    public static void ApplyMove<TRef>(ILcmOwningSequence<TRef> sequence, CanonicalId memberId,
        Placement? placement, string kind) where TRef : class, ICmObject
    {
        var currentIndex = IndexOf(sequence, memberId);
        if (currentIndex < 0)
            throw new InvalidOperationException($"'{kind}': the member to move is absent from the sequence.");

        var remaining = new List<TRef>(sequence.Count - 1);
        for (var index = 0; index < sequence.Count; index++)
        {
            if (index != currentIndex) remaining.Add(sequence[index]);
        }

        var insertionIndex = InsertionIndex(remaining, placement, kind);
        if (currentIndex == insertionIndex) return;

        var destinationIndex = currentIndex < insertionIndex ? insertionIndex + 1 : insertionIndex;
        sequence.MoveTo(currentIndex, currentIndex, sequence, destinationIndex);
    }

    private static int InsertionIndex<TRef>(IList<TRef> sequence, Placement? placement, string kind)
        where TRef : ICmObject
    {
        if (placement is null)
        {
            if (sequence.Count == 0) return 0;
            throw new InvalidOperationException($"'{kind}': a nonempty sequence requires identity-relative placement.");
        }

        var left = placement.After is { } after ? IndexOf(sequence, after) : -1;
        var right = placement.Before is { } before ? IndexOf(sequence, before) : sequence.Count;
        if ((placement.After is not null && left < 0) || (placement.Before is not null && right < 0) ||
            right != left + 1)
        {
            throw new InvalidOperationException($"'{kind}': the placement anchor is stale or ambiguous for the current sequence.");
        }

        return right;
    }

    private static int IndexOf<TRef>(IList<TRef> sequence, CanonicalId memberId) where TRef : ICmObject
    {
        for (var index = 0; index < sequence.Count; index++)
        {
            if (CanonicalId.FromGuid(sequence[index].Guid) == memberId) return index;
        }

        return -1;
    }
}
