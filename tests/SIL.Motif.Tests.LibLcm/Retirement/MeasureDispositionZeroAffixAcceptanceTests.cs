using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

public sealed partial class RedundantZeroAffixRecipeAcceptanceTests
{
    [ParsimonyFactsFact]
    public async Task ZeroAffixDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        var project = RetireRedundantZeroAffixComposerTests.CreateZeroAffixProject(pristine, "^0",
            optional: true, prepareParser: true);
        using (var cache = project.Cache)
        {
            var fixture = project.Fixture;
            var category = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
                .GetObject(pristine.Seed.PartOfSpeechId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
                category.AffixTemplatesOS.Add(template);
                template.Final = true;
                template.SuffixSlotsRS.Add(fixture.Slot);
            });
            _ = AddApprovedRootReadings(cache, "ka", "mi");
            AddReviewedNegative(cache, "to");
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "zero-affix-dispositions"), "B-affix-null-vs-optional",
            "The zero affix contributes a distinct reading and remains in the grammar.",
            "Does the cited zero affix contribute a feature or distinct reading, or only permit an already optional slot to be empty?",
            "Defer until the compiler confirms that the authored zero marker is loaded as a final morphology output.");
    }
}
