using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Warns at compose time when an authored rule's alpha variables multiply past the Dry Run limit.</summary>
public static class AlphaRuleComposeWarning
{
    /// <summary>The kind the warning carries in the related objects of a composed intent.</summary>
    public const string Kind = "alpha-rule-expansion";

    /// <summary>One warning, worded as the Dry Run words it, when the rule's alpha variables expand past the limit.</summary>
    public static IReadOnlyList<ComposedRelatedObject> Read(LcmCache cache, AuthorPhonologicalRuleIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var uses = intent.Input.Concat(intent.Outcomes.SelectMany(o => o.Output))
            .SelectMany(item => item.Variables ?? [])
            .Concat(intent.Outcomes.SelectMany(o => o.Left.Concat(o.Right)).SelectMany(item => item.Variables ?? []))
            .ToArray();
        if (uses.Length == 0) return [];
        var variables = AlphaRuleExpansionWarnings.Variables(cache, uses);
        return AlphaRuleExpansionWarnings.Warning(intent.Name, variables) is { } warning
            ? [new(Kind, uses[0].Feature.Value, warning)]
            : [];
    }
}
