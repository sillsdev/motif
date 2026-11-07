using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>Projects producer traces into bounded, cost-free report nodes.</summary>
public static class ReviewTraceProjection
{
    /// <summary>The maximum number of nodes retained from one diagnostic.</summary>
    public const int CaptureLimit = 10_000;

    /// <summary>Captures ordered presentation nodes and records whether the tree fit.</summary>
    public static (IReadOnlyList<TraceNode> Nodes, bool Complete) Capture(TraceStep root)
    {
        var result = new List<TraceNode>();
        var pending = new Stack<(TraceStep Step, string? Parent, string Id)>();
        var nextId = 1;
        var omitted = false;
        pending.Push((root, null, "0"));
        while (pending.Count != 0 && result.Count < CaptureLimit)
        {
            var (step, parent, id) = pending.Pop();
            var refs = step.SourceIdentityId is { Length: > 0 } source ? new[] { Short(source) } : Array.Empty<string>();
            result.Add(new TraceNode(id, parent, Short(step.Type), Short(step.Source ?? step.Type),
                step.FailureReason is null ? null : Short(step.FailureReason), refs, null));
            var count = Math.Min(step.Children.Count, CaptureLimit - result.Count - pending.Count);
            if (count < step.Children.Count) omitted = true;
            for (var index = count - 1; index >= 0; index--)
                pending.Push((step.Children[index], id, (nextId++).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return (result, pending.Count == 0 && !omitted);
    }

    private static string Short(string value) => value.Length <= 512 ? value : value[..512];
}
