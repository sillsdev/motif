using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Follows two choices of one stored analysis through the composed window's own command client, against a
/// seeded project and its Baseline, into the Review page, so the replacement shown is the command's own.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingChangeReplacementWalkthroughTests(PristineProjectFixture pristine)
{
    private const string Word = "stored-word";

    [Fact]
    public void ChoosingAStoredAnalysisAgainReplacesTheEarlierChoiceOnTheReviewPage()
    {
        using var project = new WalkthroughProject(pristine);
        Assert.False(File.Exists(Path.ChangeExtension(project.FwDataPath, ".motif.db")),
            "the prepared project copy already contains a Motif store");
        var storedId = AddStoredAnalysis(project.FwDataPath, pristine.Seed);
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            var changes = walkthrough.Workspace.PageModel<ReviewPageModel>().Changes;

            await changes.ApproveStoredAnalysisAsync(Word, storedId, "stored reading", WorkspacePage.TryAWord);
            Assert.Null(changes.LastRefusal?.Message);
            var first = Assert.Single(changes.Items).ChangeId;
            Assert.Null(changes.CollectionNotice);

            await changes.ApproveStoredAnalysisAsync(Word, storedId, "stored reading", WorkspacePage.TryAWord);

            Assert.Null(changes.LastRefusal?.Message);
            var second = Assert.Single(changes.Items);
            Assert.NotEqual(first, second.ChangeId);
            Assert.Equal(ChangeKinds.Approve, second.Kind);
            Assert.True(second.Fit?.StillFits);
            Assert.Equal(first, changes.Snapshot.ReplacedChangeId);
            Assert.Equal("Replaced an earlier pending change.", changes.CollectionNotice);
        }, WalkthroughSteps.Remaining(deadline));
    }

    // Starts with no opinion, pinned by `AddHumanApproval_RoundTripsThroughDryRunAndApply`, so both approvals act.
    private static string AddStoredAnalysis(string fwDataPath, SeededProject seed)
    {
        var loader = new FwDataProjectLoader();
        var analysisId = Guid.Empty;
        using (var cache = loader.LoadCache(fwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(Word, cache.DefaultVernWs));
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                analysisId = analysis.Guid;
            });
            loader.Save(cache);
        }
        return CanonicalId.FromGuid(analysisId).Value;
    }
}
