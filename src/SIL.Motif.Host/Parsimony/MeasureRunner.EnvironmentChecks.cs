using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

/// <summary>One environment with the canonical keys of its two sides; a key is null when the side has no fact.</summary>
internal sealed record EnvironmentSideFact(string Guid, string? Name, string? Representation, string ParseStatus,
    string? LeftKey, string? RightKey);

/// <summary>One allomorph that names an environment, as a phone environment or an infix Position.</summary>
internal sealed record EnvironmentUseFact(string EnvironmentGuid, string AllomorphGuid, string Role);

/// <summary>Every environment and every allomorph reference to one.</summary>
internal sealed record EnvironmentDuplicateInput(IReadOnlyList<EnvironmentSideFact> Environments,
    IReadOnlyList<EnvironmentUseFact> Uses);

public static partial class MeasureRunner
{
    internal static ParsimonyMeasureResult DecideDuplicateEnvironments(MeasureDefinition measure,
        EnvironmentDuplicateInput input, MeasureContext context)
    {
        var eligible = input.Environments.Where(item => item.ParseStatus == "valid" && item.LeftKey is not null &&
            item.RightKey is not null).ToArray();
        var excluded = input.Environments.Count - eligible.Length;
        var uses = input.Uses.ToLookup(item => item.EnvironmentGuid, StringComparer.Ordinal);
        var findings = new List<ParsimonyFinding>();
        var redundant = 0L;
        foreach (var group in eligible.GroupBy(item => (item.LeftKey, item.RightKey)).Where(item => item.Count() > 1))
        {
            var members = group.OrderBy(item => item.Guid, StringComparer.Ordinal).ToArray();
            redundant += members.Length - 1;
            var identity = ParsimonyFindingIdentity.DuplicateEnvironmentsIdentity(members.Select(item => item.Guid));
            var memberUses = members.ToDictionary(item => item.Guid, item => uses[item.Guid]
                .OrderBy(use => use.AllomorphGuid, StringComparer.Ordinal).ThenBy(use => use.Role, StringComparer.Ordinal)
                .ToArray(), StringComparer.Ordinal);
            var limitations = new List<string>
            {
                "These environments have the same left and right sides once spacing and class names are ignored.",
            };
            limitations.AddRange(members.Select(item => DescribeEnvironmentUse(item, memberUses[item.Guid], context)));
            if (excluded > 0)
                limitations.Add($"{excluded} environment(s) that did not parse as valid were left out of the comparison.");
            limitations.Add("Merging the copies is the linguist's choice; no linguistic claim is inferred from equal keys.");
            var references = members.Select(item => EvidenceReference(context.BundleId, ParsimonyViews.StatementUsage,
                new { statementKind = "environment", objectGuid = item.Guid })).Concat(memberUses.Values
                .SelectMany(items => items).Select(item => item.AllomorphGuid).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).Select(guid => EvidenceReference(context.BundleId,
                    ParsimonyViews.AllomorphContext, new { objectGuid = guid }))).ToArray();
            var detail = JsonSerializer.Serialize(new
            {
                left = group.Key.LeftKey, right = group.Key.RightKey,
                members = members.Select(item => new
                {
                    item.Guid, item.Representation,
                    users = memberUses[item.Guid].Select(use => new { use.AllomorphGuid, use.Role }),
                }),
            });
            findings.Add(new ParsimonyFinding(ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id,
                measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                    GroupKind: ParsimonyGroupKind.DuplicateEnvironments),
                identity, new ParsimonyMeasureNumber(members.Length - 1, input.Environments.Count, measure.Unit),
                measure.Threshold!, MeasureEvidence.Compute(measure, context.Digest, detail), references,
                measure.RecipeLink, limitations, ParsimonyVerification.NotRun));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, input.Environments.Count, redundant, result, null);
    }

    private static string DescribeEnvironmentUse(EnvironmentSideFact environment,
        IReadOnlyList<EnvironmentUseFact> uses, MeasureContext context)
    {
        var label = $"Environment \"{environment.Representation}\"";
        if (uses.Count == 0) return $"{label} is not used by any allomorph.";
        var names = uses.Select(use => (context.Describe(use.AllomorphGuid) ?? use.AllomorphGuid) +
            (use.Role == "position" ? " (infix position)" : "")).Distinct(StringComparer.Ordinal).ToArray();
        return $"{label} is used by {Listed(names)}.";
    }
}
