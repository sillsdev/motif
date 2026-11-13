using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

/// <summary>One node of a pattern tree; Text is the readable token of a literal, when the facts give one.</summary>
internal sealed record ContextNodeFact(long NodeId, long? ParentNodeId, int Ordinal, string Kind, string? Text);

/// <summary>One environment side or rule context side with the nodes of its pattern tree.</summary>
/// <param name="OwnerKind">Either environment or phonologicalRule.</param>
/// <param name="OwnerGuid">The environment or the rule.</param>
/// <param name="Side">Either left or right.</param>
/// <param name="Ordinal">The right-hand-side number of a rule context; zero for an environment.</param>
/// <param name="Nodes">Every node of the side's tree.</param>
internal sealed record ContextSideFact(string OwnerKind, string OwnerGuid, string Side, int Ordinal,
    IReadOnlyList<ContextNodeFact> Nodes);

/// <summary>The sides to inspect, the names of their owners, and the allomorphs that use each environment.</summary>
/// <param name="Sides">Every non-empty environment side and rule context side.</param>
/// <param name="OwnerNames">A readable name for each owner, by GUID.</param>
/// <param name="Uses">Every allomorph reference to an environment.</param>
/// <param name="RulesRead">Whether rule contexts were readable in the facts.</param>
internal sealed record LiteralHostInput(IReadOnlyList<ContextSideFact> Sides,
    IReadOnlyDictionary<string, string> OwnerNames, IReadOnlyList<EnvironmentUseFact> Uses, bool RulesRead);

/// <summary>What one side is made of.</summary>
/// <param name="LiteralOnly">Whether the side holds nothing but literal segments and boundaries.</param>
/// <param name="Segments">How many literal segments the side spells, boundaries not counted.</param>
/// <param name="HasBoundary">Whether the side holds a word anchor, a morpheme boundary or an affix hyphen.</param>
/// <param name="Text">The side's tokens in order, for a reader.</param>
internal sealed record LiteralSideShape(bool LiteralOnly, int Segments, bool HasBoundary, string Text)
{
    /// <summary>A side that is a boundary and nothing else, which edges the host across the target.</summary>
    public bool OnlyBoundaries => LiteralOnly && Segments == 0 && HasBoundary;
}

public static partial class MeasureRunner
{
    private const int LiteralHostSegmentsAlone = 3;
    private const int LiteralHostSegmentsAtBoundary = 2;

    internal static ParsimonyMeasureResult DecideLiteralHost(MeasureDefinition measure, LiteralHostInput input,
        MeasureContext context)
    {
        var shapes = input.Sides.ToDictionary(side => side, side => ShapeOfSide(side.Nodes));
        var units = input.Sides.ToLookup(side => (side.OwnerKind, side.OwnerGuid, side.Ordinal));
        var uses = input.Uses.ToLookup(item => item.EnvironmentGuid, StringComparer.Ordinal);
        var findings = new List<ParsimonyFinding>();
        foreach (var side in input.Sides)
        {
            var shape = shapes[side];
            var other = units[(side.OwnerKind, side.OwnerGuid, side.Ordinal)].FirstOrDefault(item => item != side);
            var edged = shape.HasBoundary || other is not null && shapes[other].OnlyBoundaries;
            if (!shape.LiteralOnly || !(shape.Segments >= LiteralHostSegmentsAlone ||
                shape.Segments >= LiteralHostSegmentsAtBoundary && edged)) continue;
            findings.Add(LiteralHostFinding(measure, input, context, side, shape, edged, uses, input.Sides.Count));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        if (input.RulesRead) return Result(measure, ParsimonyMeasureStatus.Computed, input.Sides.Count, result.Length,
            result, null);
        return Result(measure, ParsimonyMeasureStatus.Inconclusive, input.Sides.Count, result.Length, result,
            "Rule contexts were not read; only environment sides were checked.",
            "phonological rule contexts are not in the grammar facts, so only environments were checked.");
    }

    private static ParsimonyFinding LiteralHostFinding(MeasureDefinition measure, LiteralHostInput input,
        MeasureContext context, ContextSideFact side, LiteralSideShape shape, bool edged,
        ILookup<string, EnvironmentUseFact> uses, int inspected)
    {
        var isRule = side.OwnerKind == "phonologicalRule";
        var kind = isRule ? "rule" : "environment";
        var owner = input.OwnerNames.GetValueOrDefault(side.OwnerGuid, side.OwnerGuid);
        var identity = $"literal-context/{kind}/{side.OwnerGuid}/{side.Side}/{side.Ordinal}";
        var why = shape.Segments >= LiteralHostSegmentsAlone
            ? $"{LiteralHostSegmentsAlone} or more literal segments"
            : $"{LiteralHostSegmentsAtBoundary} or more literal segments next to a boundary";
        var limitations = new List<string>
        {
            isRule
                ? $"Rule \"{owner}\", right-hand side {side.Ordinal + 1}: its {side.Side} context is {shape.Segments} literal " +
                  $"segments ({shape.Text}) with no natural class, variable or repetition."
                : $"Environment \"{owner}\": its {side.Side} side is {shape.Segments} literal segments " +
                  $"({shape.Text}) with no natural class, variable or repetition.",
            $"Flagged for {why}; a boundary anchor, morpheme boundary or affix hyphen counts as a boundary.",
        };
        if (!isRule)
            limitations.Add(DescribeEnvironmentUse(new EnvironmentSideFact(side.OwnerGuid, null, owner, "valid", null,
                null), uses[side.OwnerGuid].OrderBy(use => use.AllomorphGuid, StringComparer.Ordinal)
                .ThenBy(use => use.Role, StringComparer.Ordinal).ToArray(), context));
        limitations.Add("Literal segments that spell one word may list that word in the phonology; " +
            "a real sound pattern shared by several words is not flagged by this check.");
        if (!input.RulesRead)
            limitations.Add("Rule contexts were not read, so only environment sides were checked.");
        var users = isRule ? [] : uses[side.OwnerGuid].Select(use => use.AllomorphGuid)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        ParsimonyEvidenceReference[] references = isRule ? [] : new[]
            {
                EvidenceReference(context.BundleId, ParsimonyViews.StatementUsage,
                    new { statementKind = "environment", objectGuid = side.OwnerGuid }),
            }.Concat(users.Select(guid => EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                new { objectGuid = guid }))).ToArray();
        var detail = JsonSerializer.Serialize(new
        {
            kind, side.OwnerGuid, side.Side, side.Ordinal, shape.Segments, shape.Text, edged, users,
        });
        return new ParsimonyFinding(ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id,
            measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.LiteralContext),
            $"literal-context/{kind}", new ParsimonyMeasureNumber(1, inspected, measure.Unit), measure.Threshold!,
            MeasureEvidence.Compute(measure, context.Digest, detail), references, measure.RecipeLink, limitations,
            ParsimonyVerification.NotRun);
    }

    internal static LiteralSideShape ShapeOfSide(IReadOnlyList<ContextNodeFact> nodes)
    {
        var children = nodes.ToLookup(node => node.ParentNodeId);
        var literal = true;
        var segments = 0;
        var boundary = false;
        var text = new List<string>();
        void Leaf(string? token)
        {
            text.Add(token ?? "?");
            if (IsBoundaryText(token)) boundary = true;
            else segments++;
        }
        void Visit(ContextNodeFact node)
        {
            var below = children[node.NodeId].OrderBy(child => child.Ordinal).ToArray();
            switch (node.Kind)
            {
                case "sequence":
                    foreach (var child in below) Visit(child);
                    break;
                case "literalSegments":
                    // The compiler lists one phoneme node per segment under the literal; a lone hyphen is a boundary.
                    if (IsBoundaryText(node.Text)) Leaf(node.Text!);
                    else if (below.Length > 0)
                    {
                        text.Add(node.Text ?? "");
                        segments += below.Count(child => child.Kind != "boundary");
                        boundary |= below.Any(child => child.Kind == "boundary");
                    }
                    else foreach (var token in (node.Text ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        Leaf(token);
                    break;
                case "phoneme":
                case "characterDefinition":
                    Leaf(node.Text);
                    break;
                case "boundary":
                case "wordBoundary":
                case "leftAnchor":
                case "rightAnchor":
                    boundary = true;
                    text.Add(node.Kind == "boundary" ? node.Text ?? "+" : "#");
                    break;
                default:
                    literal = false;
                    break;
            }
        }
        foreach (var root in children[null].OrderBy(node => node.Ordinal)) Visit(root);
        return new LiteralSideShape(literal, segments, boundary, string.Join(" ", text));
    }

    // A segment spelled with no letter or digit, such as the hyphen of an affix, marks a morpheme edge.
    private static bool IsBoundaryText(string? text) =>
        !string.IsNullOrEmpty(text) && text.All(letter => !char.IsLetterOrDigit(letter));
}
