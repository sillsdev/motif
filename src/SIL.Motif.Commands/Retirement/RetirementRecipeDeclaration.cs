using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Commands.Store;

namespace SIL.Motif.Commands.Retirement;

/// <summary>How a retirement recipe's Draft names the Finding it answers, declared once by the recipe.</summary>
/// <param name="ComposerName">The composer whose provenance entry carries the Finding binding.</param>
/// <param name="AcceptsSubject">Whether a judgment subject of this shape may bind the recipe to a Finding.</param>
internal sealed record RetirementFindingBinding(string ComposerName, Func<HumanJudgmentSubject, bool> AcceptsSubject)
{
    /// <summary>Whether <paramref name="subject"/> names <paramref name="finding"/>, decided by the identity module.</summary>
    internal bool Binds(HumanJudgmentSubject subject, ParsimonyFinding finding) =>
        AcceptsSubject(subject) && ParsimonyFindingIdentity.SubjectMatches(subject, finding, string.Empty);

    /// <summary>The provenance entries of <paramref name="draft"/> that carry this binding.</summary>
    internal JsonElement[] Entries(DraftDocument draft) => draft.ComposerProvenance.Where(item =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("composer", out var composer) &&
        composer.ValueKind == JsonValueKind.String && composer.GetString() == ComposerName &&
        item.TryGetProperty("input", out _)).ToArray();
}

/// <summary>
/// What a retirement recipe declares so the review machinery never rediscovers it: the composer and input format
/// that mark its provenance, how that input parses, and how its Draft binds to a Finding. A recipe with no
/// declaration has no review path.
/// </summary>
/// <typeparam name="TIntent">The recipe's parsed intent.</typeparam>
internal sealed record RetirementRecipeDeclaration<TIntent>(
    string Id,
    string ComposerName,
    string ProvenanceFormat,
    Func<string, TIntent> ParseIntent,
    RetirementFindingBinding FindingBinding) where TIntent : class
{
    /// <summary>The recipe's intents in <paramref name="draft"/>'s composer provenance, parsed.</summary>
    internal IReadOnlyList<TIntent> Compositions(DraftDocument draft)
    {
        var found = new List<TIntent>();
        foreach (var provenance in draft.ComposerProvenance)
        {
            if (provenance.ValueKind != JsonValueKind.Object ||
                !provenance.TryGetProperty("composer", out var composer) ||
                composer.ValueKind != JsonValueKind.String || composer.GetString() != ComposerName ||
                !provenance.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object ||
                !input.TryGetProperty("format", out var format) || format.GetString() != ProvenanceFormat)
                continue;
            found.Add(ParseIntent(input.GetRawText()));
        }
        return found;
    }
}

/// <summary>The retirement recipes that have a paired-Assessment review path.</summary>
internal static class RetirementRecipes
{
    /// <summary>Replacing listed affix forms with a sound rule.</summary>
    internal static readonly RetirementRecipeDeclaration<ReplaceListedAllomorphsWithRuleIntent> RuleReplacement = new(
        "rule-replacement", "RetireAllomorph", "motif-allomorph-retirement",
        AllomorphRetirementCodec.Parse,
        new RetirementFindingBinding("RecordParsimonyDisposition", subject => subject is
            ParsimonyFindingJudgmentSubject or GroupJudgmentSubject { Role: not JudgmentGroupRole.AdhocCluster }));
}
