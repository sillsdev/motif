using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

public sealed partial class AllomorphRuleRetirementAcceptanceTests
{
    [ParsimonyFactsFact]
    public async Task AlternationFamilyDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            _ = BuildOwnerFixture(cache, pristine.Seed);
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "alternation-family-dispositions"), "P-allo-alternation-family",
            "The listed alternants are lexical patterns; a shared sound rule is not confirmed.",
            "The two cited morphemes have n/m alternants. Does this pattern extend to held-out bilabial contexts, and where should it be ordered relative to rule R?",
            "Defer until the context and rule order are known beyond the exact comparison and every replacement identity can be mapped.");
    }
}
