using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed record TemplateOwnershipCount(string Owner, int Count, int MaximumDepth);

internal sealed record ControlSubtreeSnapshot(
    int ControlCount,
    int MaximumDepth,
    IReadOnlyList<ScaleControlCount> ControlTypes,
    IReadOnlyList<TemplateOwnershipCount> TemplateParts);

internal static class AvaloniaScaleCounts
{
    public static ScaleCountSnapshot CaptureRealizedControls(Control root) =>
        ScaleCountHarness.Capture(
            root,
            root.GetVisualDescendants().OfType<Control>(),
            control => ReferenceEquals(control, root) ? 0 :
                control.GetVisualAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, root)).Count() + 1,
            control => control.Name == "WordStrip" ? "WordStrip" : control.GetType().FullName ?? control.GetType().Name,
            control => control.Name == "WordStrip" ||
                control.GetType().Assembly.GetName().Name?.StartsWith("SIL.Motif.App", StringComparison.Ordinal) == true);

    public static ControlSubtreeSnapshot CaptureSubtree(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var controls = new[] { root }.Concat(root.GetVisualDescendants().OfType<Control>()).ToArray();
        var typeCounts = new Dictionary<string, (int Count, int MaximumDepth)>(StringComparer.Ordinal);
        var templateOwners = new Dictionary<AvaloniaObject, (int Index, int Count, int MaximumDepth)>(
            ReferenceEqualityComparer.Instance);
        var maximumDepth = 0;
        foreach (var control in controls)
        {
            var depth = ReferenceEquals(control, root) ? 0 :
                control.GetVisualAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, root)).Count() + 1;
            maximumDepth = Math.Max(maximumDepth, depth);
            var type = control.GetType().FullName ?? control.GetType().Name;
            var typeCount = typeCounts.GetValueOrDefault(type);
            typeCounts[type] = (typeCount.Count + 1, Math.Max(typeCount.MaximumDepth, depth));

            if (control.TemplatedParent is not { } owner) continue;
            if (!templateOwners.TryGetValue(owner, out var ownerCount))
                ownerCount = (templateOwners.Count, 0, 0);
            templateOwners[owner] = (ownerCount.Index, ownerCount.Count + 1,
                Math.Max(ownerCount.MaximumDepth, depth));
        }

        var templateParts = templateOwners
            .OrderBy(pair => pair.Value.Index)
            .Select(pair => new TemplateOwnershipCount(
                $"{pair.Key.GetType().FullName ?? pair.Key.GetType().Name}" +
                (pair.Key is Control { Name.Length: > 0 } named ? $" ({named.Name})" : string.Empty) +
                $" #{pair.Value.Index + 1}",
                pair.Value.Count,
                pair.Value.MaximumDepth))
            .ToArray();
        return new ControlSubtreeSnapshot(controls.Length, maximumDepth,
            typeCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ScaleControlCount(pair.Key, pair.Value.Count, pair.Value.MaximumDepth)).ToArray(),
            templateParts);
    }

    public static void AssertSubtreeBudget(ControlSubtreeSnapshot snapshot, string rootName,
        int maximumControls, int maximumDepth)
    {
        if (snapshot.ControlCount > maximumControls || snapshot.MaximumDepth > maximumDepth)
            throw new InvalidOperationException(
                $"{rootName} subtree exceeded {maximumControls} controls/depth {maximumDepth}: " +
                $"{snapshot.ControlCount} controls/depth {snapshot.MaximumDepth}.");
    }
}
