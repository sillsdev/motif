using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parsimony;

public sealed partial class ConditionReviewRecipeAcceptanceTests
{
    [RealParserFact]
    public async Task DisapprovedReadingDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        IReadOnlyList<string> words;
        using (var cache = pristine.NewScratch())
        {
            var fixture = MakeUnconditionedAllomorph(cache, broadFirst: true, includeSeparateFallback: true);
            projectPath = cache.ProjectId.Path;
            words = fixture.Words;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "disapproved-reading-dispositions"), "R-word-disapproved-produced",
            "The Disapproved label concerns sense rather than this morphology, so both readings remain available.",
            "The Disapproved morphology on pka has a competing Approved reading. Does the rejected reading have a distinct grammatical condition?",
            "Defer until the exact Disapproved signature and its wordform attribution are complete.", words);
    }

    [ParsimonyFactsFact]
    public async Task UnconditionedAllomorphDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            _ = MakeUnconditionedAllomorph(cache, broadFirst: true, includeSeparateFallback: false);
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "unconditioned-allomorph-dispositions"), "R-allo-unconditioned",
            "The unconditioned variant is the confirmed final fallback outside the conditioned contexts.",
            "Should the cited unconditioned variant be the final fallback after its conditioned siblings, or be limited to specific contexts?",
            "Defer until effective allomorph order and the competing sibling conditions are available.");
    }

    [RealParserFact]
    public async Task BroadEnvironmentDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        string targetAllomorph;
        IReadOnlyList<string> words;
        using (var cache = pristine.NewScratch())
        {
            var fixture = MakeBroadEnvironmentDispositionFixture(cache);
            targetAllomorph = fixture.TargetAllomorph;
            words = fixture.Words;
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "broad-environment-dispositions"), "R-env-broad",
            "The held-out form confirms that the broader environment remains productive.",
            "The cited environment licenses an extra context absent from these Texts. Is the contrasting form possible: valid, invalid, or unknown?",
            "Defer until the context universe, compiled order, and unique alignment are available.", words,
            findingIdentity: targetAllomorph);
    }

    [ParsimonyFactsFact]
    public async Task NaturalClassDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        string targetAllomorph;
        using (var cache = pristine.NewScratch())
        {
            var fixture = MakeBroadEnvironment(cache, includeWrongContextNegative: true);
            targetAllomorph = fixture.WideClass;
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "natural-class-dispositions"), "R-nc-excess",
            "The additional class members remain productive in held-out forms.",
            "Should this process also apply to the cited extra segments, or only to the observed subset? Please provide independent contrasts.",
            "Defer until class membership and the exact usage site can be attributed completely.",
            findingIdentity: targetAllomorph);
    }

}
