using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Warns when a rule Output names a segment-list class of several members. HermitCrab and PanGloss apply only the
/// features the members share, so the input keeps its own value for every feature the members differ on; the Output
/// never chooses a member (fieldworks-parser-conformance#38).
/// </summary>
public static class MultiMemberOutputWarning
{
    /// <summary>The kind a warning carries in the related objects of a composed intent.</summary>
    public const string Kind = "multi-member-output";

    /// <summary>One warning for each distinct multi-member segment-list class an Output item names.</summary>
    public static IReadOnlyList<ComposedRelatedObject> Read(LcmCache cache, IEnumerable<PhonologicalRuleItem> outputs)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var repository = cache.ServiceLocator.ObjectRepository;
        var warnings = new List<ComposedRelatedObject>();
        var seen = new HashSet<Guid>();
        foreach (var id in outputs.Select(item => item.NaturalClass).OfType<CanonicalId>())
        {
            var guid = id.ToGuid();
            if (!seen.Add(guid) || !repository.TryGetObject(guid, out var found) ||
                found is not IPhNCSegments { SegmentsRC.Count: > 1 } naturalClass) continue;
            var name = naturalClass.Name.BestAnalysisAlternative.Text;
            warnings.Add(new(Kind, id.Value, $"Output class '{name}' lists {naturalClass.SegmentsRC.Count} segments, but " +
                "HermitCrab and PanGloss never choose one: the Output applies only the features the members share, and the " +
                "input keeps its own value for every feature the members differ on. For free variation, author alternate forms on the entry; for a " +
                "determinate change, use a feature-class Output or a single phoneme."));
        }
        return warnings;
    }
}
