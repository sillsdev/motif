using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class OrderedSequenceDiffTests
{
    private static readonly CanonicalId[] Members = Enumerable.Range(0, 5)
        .Select(_ => CanonicalId.Mint()).ToArray();

    [Fact]
    public void APrefixedSpellingOfTheSameMembersInTheSameOrderIsNoEdit()
    {
        var source = Members[..3];
        var target = source.Select(member => CanonicalId.Parse("environment/" + member.Suffix)).ToArray();

        Assert.Empty(OrderedSequenceDiff.Create(source, target));
    }

    [Fact]
    public void APrefixedReorderMovesMembersAndAnchorsByTheSpellingTheCallerAuthored()
    {
        var source = Members[..3];
        var target = new[] { Members[2], Members[1], Members[0] }
            .Select(member => CanonicalId.Parse("environment/" + member.Suffix)).ToArray();

        var edits = OrderedSequenceDiff.Create(source, target);

        Assert.All(edits, edit => Assert.Equal(OrderedSequenceEditKind.Move, edit.Kind));
        Assert.All(edits, edit => Assert.StartsWith("environment/", edit.Member.Value, StringComparison.Ordinal));
        Assert.All(edits.Select(edit => edit.Placement!), placement =>
            Assert.All(new[] { placement.After, placement.Before }.OfType<CanonicalId>(),
                anchor => Assert.StartsWith("environment/", anchor.Value, StringComparison.Ordinal)));
    }

    [Fact]
    public void TwoSpellingsOfOneMemberAreADuplicate()
    {
        var target = new[] { Members[0], CanonicalId.Parse("environment/" + Members[0].Suffix) };

        Assert.Throws<ArgumentException>(() => OrderedSequenceDiff.Create([], target));
    }

    // Raw text sorts an alternate suffix before a smaller identity; the canonical key does not.
    [Fact]
    public void AnAlternateSuffixSortsWithTheIdentityItEncodes()
    {
        var smaller = CanonicalId.Parse("AAAAAAAAAAAAAAAAAAABAA");
        var larger = CanonicalId.Parse("AAAAAAAAAAAAAAAAAAABAw");
        var alias = CanonicalId.Parse("AAAAAAAAAAAAAAAAAAABA0");
        Assert.Equal(larger.ToGuid(), alias.ToGuid());

        Assert.Equal(larger.CanonicalSuffix, alias.CanonicalSuffix);
        Assert.Equal(new[] { smaller, alias },
            new[] { alias, smaller }.OrderBy(id => id.CanonicalSuffix, StringComparer.Ordinal));
    }

    [Fact]
    public void ReversalsUseDeterministicLisTieBreakingAndPlacementAnchors()
    {
        var source = Members[..3];
        var target = new[] { Members[2], Members[1], Members[0] };

        var edits = OrderedSequenceDiff.Create(source, target);

        Assert.Equal(new[] { OrderedSequenceEditKind.Move, OrderedSequenceEditKind.Move },
            edits.Select(edit => edit.Kind));
        Assert.Equal(new[] { Members[0], Members[1] }, edits.Select(edit => edit.Member));
        Assert.Equal(new Placement(Members[2], null), edits[0].Placement);
        Assert.Equal(new Placement(Members[2], Members[0]), edits[1].Placement);
        Assert.Equal(target, Apply(source, edits));
    }

    [Fact]
    public void SourceOnlyDeletesAndTargetOnlyAddsProduceTheTargetInDeterministicOrder()
    {
        var source = new[] { Members[0], Members[1], Members[2] };
        var target = new[] { Members[3], Members[2], Members[0], Members[4] };

        var edits = OrderedSequenceDiff.Create(source, target);

        Assert.Equal(new[]
        {
            OrderedSequenceEditKind.Remove,
            OrderedSequenceEditKind.Add,
            OrderedSequenceEditKind.Move,
            OrderedSequenceEditKind.Add,
        }, edits.Select(edit => edit.Kind));
        Assert.Equal(new[] { Members[1], Members[4], Members[0], Members[3] },
            edits.Select(edit => edit.Member));
        Assert.Equal(target, Apply(source, edits));
    }

    [Fact]
    public void MoveCountMatchesBreadthFirstOracleForEveryFiveMemberPermutation()
    {
        var permutations = Permutations(Members).ToArray();
        var distances = permutations.ToDictionary(Key, BreadthFirstDistances);

        foreach (var source in permutations)
        {
            foreach (var target in permutations)
            {
                var edits = OrderedSequenceDiff.Create(source, target);
                Assert.Equal(distances[Key(source)][Key(target)],
                    edits.Count(edit => edit.Kind == OrderedSequenceEditKind.Move));
                Assert.Equal(target, Apply(source, edits));
            }
        }
    }

    [Fact]
    public void DuplicateIdentitiesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => OrderedSequenceDiff.Create(
            [Members[0], Members[0]], [Members[0]]));
        Assert.Throws<ArgumentException>(() => OrderedSequenceDiff.Create(
            [Members[0]], [Members[1], Members[1]]));
    }

    private static CanonicalId[] Apply(IReadOnlyList<CanonicalId> source,
        IReadOnlyList<OrderedSequenceEdit> edits)
    {
        var current = source.ToList();
        foreach (var edit in edits)
        {
            var existingIndex = current.IndexOf(edit.Member);
            if (edit.Kind == OrderedSequenceEditKind.Remove)
            {
                Assert.True(existingIndex >= 0);
                current.RemoveAt(existingIndex);
                continue;
            }

            if (edit.Kind == OrderedSequenceEditKind.Move)
            {
                Assert.True(existingIndex >= 0);
                current.RemoveAt(existingIndex);
            }
            else
            {
                Assert.Equal(-1, existingIndex);
            }

            var insertionIndex = InsertionIndex(current, edit.Placement);
            current.Insert(insertionIndex, edit.Member);
        }

        return current.ToArray();
    }

    private static int InsertionIndex(IReadOnlyList<CanonicalId> current, Placement? placement)
    {
        if (current.Count == 0)
        {
            Assert.Null(placement);
            return 0;
        }

        Assert.NotNull(placement);
        var afterIndex = placement.After is { } after ? IndexOf(current, after) : -1;
        var beforeIndex = placement.Before is { } before ? IndexOf(current, before) : current.Count;
        Assert.Equal(afterIndex + 1, beforeIndex);
        return beforeIndex;
    }

    private static int IndexOf(IReadOnlyList<CanonicalId> values, CanonicalId value)
    {
        for (var index = 0; index < values.Count; index++)
            if (values[index] == value) return index;
        return -1;
    }

    private static IEnumerable<CanonicalId[]> Permutations(IReadOnlyList<CanonicalId> values)
    {
        var buffer = values.ToArray();
        return Generate(0);

        IEnumerable<CanonicalId[]> Generate(int index)
        {
            if (index == buffer.Length)
            {
                yield return buffer.ToArray();
                yield break;
            }

            for (var swap = index; swap < buffer.Length; swap++)
            {
                (buffer[index], buffer[swap]) = (buffer[swap], buffer[index]);
                foreach (var permutation in Generate(index + 1)) yield return permutation;
                (buffer[index], buffer[swap]) = (buffer[swap], buffer[index]);
            }
        }
    }

    private static Dictionary<string, int> BreadthFirstDistances(CanonicalId[] source)
    {
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [Key(source)] = 0 };
        var queue = new Queue<CanonicalId[]>();
        queue.Enqueue(source);

        while (queue.TryDequeue(out var current))
        {
            var nextDistance = distances[Key(current)] + 1;
            for (var from = 0; from < current.Length; from++)
            {
                var remaining = current.Where((_, index) => index != from).ToList();
                for (var to = 0; to <= remaining.Count; to++)
                {
                    var next = remaining.ToList();
                    next.Insert(to, current[from]);
                    var key = Key(next);
                    if (!distances.TryAdd(key, nextDistance)) continue;
                    queue.Enqueue(next.ToArray());
                }
            }
        }

        return distances;
    }

    private static string Key(IEnumerable<CanonicalId> members) =>
        string.Join(',', members.Select(member => member.Value));
}
