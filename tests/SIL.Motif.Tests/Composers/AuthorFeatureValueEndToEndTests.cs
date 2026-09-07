using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// The grammar construct's full loop on a real project: authored (JSON through
/// <see cref="AuthorFeatureValueIntentParser"/>), lowered (<see cref="AuthorFeatureValueComposer.Build"/>),
/// dry-run and reviewed, applied, and saved -- the owning/col counterpart to
/// <see cref="AuthorFeatureStructureEndToEndTests"/>, which builds the <c>FsFeatStruc</c> this test adds
/// a specification to. Starts from an already-created feature structure and an existing feature: value
/// population is separate, later work against the created structure's own identity.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class AuthorFeatureValueEndToEndTests
{
    private readonly SeededProject _seed;
    private readonly string _fwDataPath;

    public AuthorFeatureValueEndToEndTests(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    [Fact]
    public void Authored_Lowered_DryRun_Applied_Saved_RoundTripsOnARealProject()
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(_fwDataPath);

        var (featStrucGuid, featureGuid) = SeedFeatStrucAndFeature(cache);
        loader.Save(cache);

        // --- authored: an agent's JSON, through the construct's own closed-schema parser ---
        var authoredJson = JsonSerializer.Serialize(new
        {
            featStruc = CanonicalId.FromGuid(featStrucGuid).Value,
            feature = CanonicalId.FromGuid(featureGuid).Value,
        });
        using var authoredDocument = JsonDocument.Parse(authoredJson);
        var intent = AuthorFeatureValueIntentParser.Parse(authoredDocument.RootElement);

        // --- lowered: one construct becomes two correctly-targeted, correctly-ordered Layer-0 operations ---
        var operations = AuthorFeatureValueComposer.Build(cache, intent);
        Assert.Equal(2, operations.Count);
        var newSpecId = operations[0].EntityId!.Value;

        var proposal = BuildProposal(operations);

        // --- dry-run and review: real before/after read back from LibLCM, nothing mutated yet ---
        var scratchRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Tests.AuthorFeatureValue", Guid.NewGuid().ToString("N"));
        using var dryRunScratch = DryRunScratch.Adopt(
            new ScratchCacheFactory(loader).CreateFromFileCopy(_fwDataPath, scratchRoot),
            $"file copy of {_fwDataPath}",
            onDisposed: () =>
            {
                if (Directory.Exists(scratchRoot)) Directory.Delete(scratchRoot, recursive: true);
            });
        var dryRun = ProposalDryRunner.Run(dryRunScratch, proposal);

        Assert.Equal(2, dryRun.ExpectedEffects.Count);
        var createEffect = dryRun.ExpectedEffects[0];
        Assert.Equal(CanonicalId.FromGuid(featStrucGuid), createEffect.CanonicalId);
        Assert.Empty(createEffect.Before);
        Assert.Equal(newSpecId.Value, createEffect.After["ref"]);

        var setFeatureEffect = dryRun.ExpectedEffects[1];
        Assert.Equal(newSpecId, setFeatureEffect.CanonicalId);
        Assert.Empty(setFeatureEffect.Before);
        Assert.Equal(CanonicalId.FromGuid(featureGuid).Value, setFeatureEffect.After["ref"]);

        AssertSpecIsAbsent(newSpecId.ToGuid());

        // --- applied and saved ---
        var receipt = ProposalApplier.Apply(
            cache, proposal, dryRun.Anchor, "motif-tests", "AuthorFeatureValue end-to-end test");
        Assert.False(receipt.AlreadyApplied);
        loader.Save(cache);

        // Re-open from disk: proves the Save above, not just the in-memory mutation, actually happened.
        using var reloaded = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        var featStruc = (IFsFeatStruc)reloaded.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(featStrucGuid);
        var spec = Assert.Single(featStruc.FeatureSpecsOC);
        Assert.Equal(newSpecId.ToGuid(), spec.Guid);
        Assert.IsAssignableFrom<IFsClosedValue>(spec);
        Assert.Equal(featureGuid, spec.FeatureRA.Guid);

        Assert.Single(ProjectAppliedLog.ReadAll(reloaded));
    }

    /// <summary>Test setup only: a bare feature structure and an unrelated closed feature.</summary>
    private (Guid FeatStrucGuid, Guid FeatureGuid) SeedFeatStrucAndFeature(LcmCache cache)
    {
        var actionHandler = cache.ServiceLocator.GetInstance<IActionHandler>();
        var msa = (IMoStemMsa)cache.ServiceLocator.GetInstance<ILexSenseRepository>()
            .GetObject(_seed.FirstSenseId).MorphoSyntaxAnalysisRA;

        Guid featStrucGuid = default;
        Guid featureGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            featStrucGuid = Guid.NewGuid();
            MoStemMsaMsFeaturesCreateLowering.Apply(cache, msa, featStrucGuid);

            var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            featureGuid = feature.Guid;
        });
        return (featStrucGuid, featureGuid);
    }

    private static Proposal BuildProposal(IReadOnlyList<OperationEnvelope> operations) => new(
        contractVersions: new Dictionary<string, string> { ["grammar"] = "1.0" },
        proposalId: CanonicalId.Mint(),
        requires: null,
        operations: operations);

    private void AssertSpecIsAbsent(Guid specGuid)
    {
        using var stillOnDisk = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        Assert.False(
            stillOnDisk.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(specGuid),
            "The dry run must not have mutated the saved project.");
    }
}
