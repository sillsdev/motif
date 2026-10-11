using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Projection;

[Collection(LcmCacheTestCollection.Name)]
public sealed class HumanJudgmentFieldResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-judgment-resolver-" + Guid.NewGuid().ToString("N"));

    public HumanJudgmentFieldResolverTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AProjectWithoutTheFieldAsksForInitialization()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);

        var resolution = HumanJudgmentFieldResolver.Resolve(cache);

        Assert.Equal(HumanJudgmentFieldCapability.Missing, resolution.Capability);
        Assert.Null(resolution.Signature);
        Assert.Contains("Project initialization is required", resolution.Message);
    }

    [Fact]
    public void TheInitializedFieldResolvesWithItsFullSignature()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        NotebookJudgmentFixture.InitializeReservedField(cache);

        var resolution = HumanJudgmentFieldResolver.Resolve(cache);

        Assert.Equal(HumanJudgmentFieldCapability.Available, resolution.Capability);
        var signature = Assert.IsType<HumanJudgmentFieldSignature>(resolution.Signature);
        Assert.Equal(HumanJudgmentFieldResolver.DeclaringClass, signature.DeclaringClass);
        Assert.Equal(CellarPropertyType.String.ToString(), signature.Type);
        Assert.True(signature.IsCustom);
        Assert.Equal(WritingSystemServices.kwsAnal, signature.WritingSystemSelector);
        Assert.Equal(0, signature.DestinationClass);
        Assert.Equal(Guid.Empty, signature.ListRoot);
    }

    [Fact]
    public void TwoFieldsWithTheReservedNameAreAmbiguousAndCarryNoSignature()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        NotebookJudgmentFixture.InitializeReservedField(cache);
        AddField(cache, "LexEntry", CellarPropertyType.String, WritingSystemServices.kwsAnal);

        var resolution = HumanJudgmentFieldResolver.Resolve(cache);

        Assert.Equal(HumanJudgmentFieldCapability.Ambiguous, resolution.Capability);
        Assert.Null(resolution.Signature);
        Assert.Contains("2 definitions", resolution.Message);
    }

    [Theory]
    [InlineData("RnGenericRec", CellarPropertyType.Integer, WritingSystemServices.kwsAnal)]
    [InlineData("RnGenericRec", CellarPropertyType.String, WritingSystemServices.kwsVern)]
    [InlineData("LexEntry", CellarPropertyType.String, WritingSystemServices.kwsAnal)]
    public void AFieldWithADifferentOwnerTypeOrWritingSystemIsIncompatibleAndSaysWhy(
        string owner, CellarPropertyType type, int selector)
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        AddField(cache, owner, type, selector);

        var resolution = HumanJudgmentFieldResolver.Resolve(cache);

        Assert.Equal(HumanJudgmentFieldCapability.Incompatible, resolution.Capability);
        var signature = Assert.IsType<HumanJudgmentFieldSignature>(resolution.Signature);
        Assert.Equal(owner, signature.DeclaringClass);
        Assert.Equal(type.ToString(), signature.Type);
        Assert.Equal(selector, signature.WritingSystemSelector);
        Assert.Contains("unsupported schema", resolution.Message);
        Assert.Contains(owner, resolution.Message);
    }

    [Fact]
    public void AMissingCacheIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => HumanJudgmentFieldResolver.Resolve(null!));
    }

    private static void AddField(LcmCache cache, string ownerClass, CellarPropertyType type, int selector)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.AddCustomField(ownerClass, HumanJudgmentFieldResolver.InternalName, type, 0, "Judgment",
                selector, Guid.Empty));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
