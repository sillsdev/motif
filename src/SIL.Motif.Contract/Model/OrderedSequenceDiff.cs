using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Contract.Model;

/// <summary>The kind of one identity-preserving edit in an ordered sequence diff.</summary>
public enum OrderedSequenceEditKind
{
    /// <summary>The identity exists only in the target sequence.</summary>
    Add,

    /// <summary>The identity exists only in the source sequence.</summary>
    Remove,

    /// <summary>The identity exists in both sequences but is outside the retained LIS.</summary>
    Move,
}

/// <summary>One add, remove, or move required to transform an ordered sequence.</summary>
/// <param name="Kind">The sequence operation.</param>
/// <param name="Member">The stable identity being added, removed, or moved.</param>
/// <param name="Placement">Identity-relative placement for an add or move; absent for a remove and for an add into an empty sequence.</param>
public sealed record OrderedSequenceEdit(
    OrderedSequenceEditKind Kind,
    CanonicalId Member,
    Placement? Placement);

/// <summary>Builds deterministic minimum-edit plans between sequences of unique canonical identities.</summary>
/// <remarks>
/// Source-only members are removed first. Remaining edits walk the target from right to left, inserting
/// each added or moved member immediately before its next target neighbor; the last member is placed at
/// the end. A patience-sorting LIS uses lower-bound replacement while source identities are visited in
/// source order, which fixes the retained subsequence when several longest choices exist.
/// </remarks>
public static class OrderedSequenceDiff
{
    /// <summary>Creates a deterministic minimum-edit plan from <paramref name="source"/> to <paramref name="target"/>.</summary>
    /// <param name="source">The ordered identities before the change.</param>
    /// <param name="target">The ordered identities after the change.</param>
    public static IReadOnlyList<OrderedSequenceEdit> Create(
        IReadOnlyList<CanonicalId> source,
        IReadOnlyList<CanonicalId> target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        RequireUnique(source, nameof(source));
        RequireUnique(target, nameof(target));

        var targetIndices = target.Select((member, index) => (member, index))
            .ToDictionary(item => item.member, item => item.index);
        var retained = LongestIncreasingSubsequence(source, targetIndices);
        var edits = new List<OrderedSequenceEdit>();
        var current = new LinkedList<CanonicalId>(source);
        var nodes = new Dictionary<CanonicalId, LinkedListNode<CanonicalId>>();
        for (var node = current.First; node is not null; node = node.Next)
            nodes.Add(node.Value, node);

        foreach (var member in source)
        {
            if (targetIndices.ContainsKey(member)) continue;
            edits.Add(new OrderedSequenceEdit(OrderedSequenceEditKind.Remove, member, null));
            current.Remove(nodes[member]);
            nodes.Remove(member);
        }

        for (var targetIndex = target.Count - 1; targetIndex >= 0; targetIndex--)
        {
            var member = target[targetIndex];
            var isPresent = nodes.TryGetValue(member, out var existingNode);
            if (isPresent && retained.Contains(member)) continue;

            var kind = isPresent ? OrderedSequenceEditKind.Move : OrderedSequenceEditKind.Add;
            if (existingNode is not null)
            {
                current.Remove(existingNode);
                nodes.Remove(member);
            }

            Placement? placement;
            LinkedListNode<CanonicalId> insertedNode;
            if (targetIndex == target.Count - 1)
            {
                placement = current.Last is { } last ? new Placement(last.Value, null) : null;
                insertedNode = current.AddLast(member);
            }
            else
            {
                var nextMember = target[targetIndex + 1];
                if (!nodes.TryGetValue(nextMember, out var nextNode))
                    throw new InvalidOperationException("The ordered edit plan lost a target neighbor.");
                placement = nextNode.Previous is { } previous
                    ? new Placement(previous.Value, nextMember)
                    : new Placement(null, nextMember);
                insertedNode = current.AddBefore(nextNode, member);
            }

            edits.Add(new OrderedSequenceEdit(kind, member, placement));
            nodes.Add(member, insertedNode);
        }

        if (!current.SequenceEqual(target))
            throw new InvalidOperationException("The ordered edit plan did not produce its target sequence.");

        return edits;
    }

    private static HashSet<CanonicalId> LongestIncreasingSubsequence(
        IReadOnlyList<CanonicalId> source,
        IReadOnlyDictionary<CanonicalId, int> targetIndices)
    {
        var sourcePositions = new List<int>();
        var values = new List<int>();
        for (var index = 0; index < source.Count; index++)
        {
            if (!targetIndices.TryGetValue(source[index], out var targetIndex)) continue;
            sourcePositions.Add(index);
            values.Add(targetIndex);
        }

        var tailValues = new List<int>();
        var tailPositions = new List<int>();
        var predecessors = new int[values.Count];
        Array.Fill(predecessors, -1);

        for (var index = 0; index < values.Count; index++)
        {
            var low = 0;
            var high = tailValues.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (tailValues[middle] < values[index]) low = middle + 1;
                else high = middle;
            }

            if (low > 0) predecessors[index] = tailPositions[low - 1];
            if (low == tailValues.Count)
            {
                tailValues.Add(values[index]);
                tailPositions.Add(index);
            }
            else
            {
                tailValues[low] = values[index];
                tailPositions[low] = index;
            }
        }

        var retained = new HashSet<CanonicalId>();
        if (tailPositions.Count == 0) return retained;

        var position = tailPositions[^1];
        while (position >= 0)
        {
            retained.Add(source[sourcePositions[position]]);
            position = predecessors[position];
        }

        return retained;
    }

    private static void RequireUnique(IReadOnlyList<CanonicalId> members, string parameterName)
    {
        if (members.Distinct().Count() != members.Count)
            throw new ArgumentException("An ordered sequence must contain unique member identities.", parameterName);
    }
}
