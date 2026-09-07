using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using Xunit;
using ContractIntentDigest = SIL.Motif.Contract.Canonicalization.IntentDigest;

namespace SIL.Motif.Tests.Runner;

/// <summary>
/// Round-trip proof for <c>FsFeatStruc.FeatureSpecs</c> (owning/col, <c>create|delete</c>) against a
/// real project -- the owning/col creation-validity precedent alongside
/// <see cref="LexEntryLexemeFormOperationsTests"/> (owning/atomic, abstract concrete-class choice).
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class FsFeatStrucFeatureSpecsOperationsTests : IDisposable
{
    private readonly FwDataProjectLoader _loader = new();
    private readonly SeededProject _seed;
    private LcmCache _cache;

    public FsFeatStrucFeatureSpecsOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void Create_OnAFeatStruc_BuildsAConcreteFsClosedValue_RoundTripsDryRunAndApply()
    {
        var featStrucGuid = CreateBareFeatStruc();
        var newSpecId = CanonicalId.Mint();

        var proposal = BuildCreateProposal(CanonicalId.FromGuid(featStrucGuid), newSpecId);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Empty(effect.Before);
        Assert.Equal(newSpecId.Value, effect.After[ReferenceFieldAlternativesKey]);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");
        Assert.False(receipt.AlreadyApplied);

        var featStruc = (IFsFeatStruc)_cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(featStrucGuid);
        var spec = Assert.Single(featStruc.FeatureSpecsOC);
        Assert.Equal(newSpecId.ToGuid(), spec.Guid);
        Assert.IsAssignableFrom<IFsClosedValue>(spec);
        Assert.Null(spec.FeatureRA);
        Assert.Null(((IFsClosedValue)spec).ValueRA);
    }

    [Fact]
    public void Create_TargetDoesNotExist_ThrowsNamingTheGuid()
    {
        var bogusTarget = Guid.NewGuid();
        var proposal = BuildCreateProposal(CanonicalId.FromGuid(bogusTarget), CanonicalId.Mint());

        var ex = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Contains(bogusTarget.ToString(), ex.Message);
    }

    [Fact]
    public void Create_TargetResolvesButIsTheWrongType_ThrowsNamingTheMismatch()
    {
        // The seeded entry's own id, presented where an FsFeatStruc is required.
        var proposal = BuildCreateProposal(CanonicalId.FromGuid(_seed.FirstEntryId), CanonicalId.Mint());

        var ex = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Contains("not a FsFeatStruc", ex.Message);
        Assert.Contains("LexEntry", ex.Message);
    }

    [Fact]
    public void Delete_RemovesTheFeatureSpec_RoundTripsDryRunAndApply()
    {
        var (featStrucGuid, specGuid) = CreateFeatStrucWithOneBareSpec();
        var proposal = BuildDeleteProposal(CanonicalId.FromGuid(specGuid));

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(specGuid, CanonicalId.Parse(effect.Before[ReferenceFieldAlternativesKey]).ToGuid());
        Assert.Empty(effect.After);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");
        Assert.False(receipt.AlreadyApplied);

        var featStruc = (IFsFeatStruc)_cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(featStrucGuid);
        Assert.Empty(featStruc.FeatureSpecsOC);
        Assert.False(_cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(specGuid));
    }

    [Fact]
    public void Delete_TargetDoesNotExist_ThrowsNamingTheGuid()
    {
        var bogusTarget = Guid.NewGuid();
        var proposal = BuildDeleteProposal(CanonicalId.FromGuid(bogusTarget));

        var ex = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Contains(bogusTarget.ToString(), ex.Message);
    }

    [Fact]
    public void Delete_TargetIsASpecOwnedByAFeatDefnDefault_NotByAFeatStruc_ThrowsRatherThanDeletingIt()
    {
        // FsFeatDefn.Default is a second, unrelated owning/atomic FsFeatureSpecification slot.
        var specGuid = CreateSpecOwnedByFeatDefnDefault();
        var proposal = BuildDeleteProposal(CanonicalId.FromGuid(specGuid));

        var ex = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Contains("not a member of an FsFeatStruc", ex.Message);

        Assert.True(_cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(specGuid));
    }

    private const string ReferenceFieldAlternativesKey = "ref";

    /// <summary>Persists the project: DryRun's scratch is a file copy, so it'd miss an uncommitted edit.</summary>
    private Guid CreateBareFeatStruc()
    {
        var actionHandler = _cache.ServiceLocator.GetInstance<IActionHandler>();
        var senseRepo = _cache.ServiceLocator.GetInstance<ILexSenseRepository>();
        var msa = (IMoStemMsa)senseRepo.GetObject(_seed.FirstSenseId).MorphoSyntaxAnalysisRA;
        Guid featStrucGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            featStrucGuid = Guid.NewGuid();
            MoStemMsaMsFeaturesCreateLowering.Apply(_cache, msa, featStrucGuid);
        });
        _loader.Save(_cache);
        return featStrucGuid;
    }

    private (Guid FeatStrucGuid, Guid SpecGuid) CreateFeatStrucWithOneBareSpec()
    {
        var featStrucGuid = CreateBareFeatStruc();
        var actionHandler = _cache.ServiceLocator.GetInstance<IActionHandler>();
        var featStruc = (IFsFeatStruc)_cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(featStrucGuid);
        Guid specGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            specGuid = Guid.NewGuid();
            FsFeatStrucFeatureSpecsCreateLowering.Apply(_cache, featStruc, specGuid);
        });
        _loader.Save(_cache);
        return (featStrucGuid, specGuid);
    }

    private Guid CreateSpecOwnedByFeatDefnDefault()
    {
        var actionHandler = _cache.ServiceLocator.GetInstance<IActionHandler>();
        Guid specGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            var feature = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            var defaultSpec = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            feature.DefaultOA = defaultSpec;
            specGuid = defaultSpec.Guid;
        });
        _loader.Save(_cache);
        return specGuid;
    }

    private static OperationEnvelope BuildCreateOperation(CanonicalId target, CanonicalId entityId) =>
        new(
            operationId: CanonicalId.Mint(),
            kind: FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
            entityId: entityId,
            target: target,
            after: EmptyObject());

    private static Proposal BuildCreateProposal(CanonicalId target, CanonicalId entityId) =>
        new(
            contractVersions: new Dictionary<string, string> { ["grammar"] = "1.0" },
            proposalId: CanonicalId.Mint(),
            requires: null,
            operations: new[] { BuildCreateOperation(target, entityId) });

    private static Proposal BuildDeleteProposal(CanonicalId target)
    {
        var operation = new OperationEnvelope(
            operationId: CanonicalId.Mint(),
            kind: FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs,
            target: target,
            after: EmptyObject());

        return new Proposal(
            contractVersions: new Dictionary<string, string> { ["grammar"] = "1.0" },
            proposalId: CanonicalId.Mint(),
            requires: null,
            operations: new[] { operation });
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}

/// <summary>
/// Closed-schema rejection tests for the <c>FsFeatStruc.FeatureSpecs</c> create/delete payloads -- no
/// <c>LcmCache</c> involved, so unlike <see cref="FsFeatStrucFeatureSpecsOperationsTests"/> this class
/// needs no <see cref="PristineProjectFixture"/>.
/// </summary>
public sealed class FsFeatStrucFeatureSpecsSchemaTests
{
    [Fact]
    public void CreatePayload_UnknownProperty_IsRejectedByTheClosedSchema()
    {
        var afterJson = JsonSerializer.Serialize(new { feature = CanonicalId.Mint().Value });
        using var afterDocument = JsonDocument.Parse(afterJson);

        Assert.Throws<ContractParseException>(
            () => FsFeatStrucFeatureSpecsPayload.Parse(afterDocument.RootElement, FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs));
    }

    [Fact]
    public void DeletePayload_AnyProperty_IsRejectedByTheClosedSchema()
    {
        var afterJson = JsonSerializer.Serialize(new { reason = "cleanup" });
        using var afterDocument = JsonDocument.Parse(afterJson);

        Assert.Throws<ContractParseException>(
            () => FsFeatStrucFeatureSpecsPayload.Parse(afterDocument.RootElement, FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs));
    }

    [Fact]
    public void Payload_NotAnObject_IsRejectedByTheClosedSchema()
    {
        using var afterDocument = JsonDocument.Parse("[]");

        Assert.Throws<ContractParseException>(
            () => FsFeatStrucFeatureSpecsPayload.Parse(afterDocument.RootElement, FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs));
    }
}
