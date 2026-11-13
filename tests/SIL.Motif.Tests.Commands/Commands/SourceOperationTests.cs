using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>The five Motif Sources operations write the list as <see cref="SourceListFormat"/> reads it.</summary>
[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SourceOperationTests(PristineProjectFixture pristine)
{
    private static OperationEnvelope Create(LcmCache cache, CanonicalId item, string id, string name = "World English Bible",
        string versification = "English", string context = "GEN-ROM; 1CO 13-15", string kind = "Paratext") =>
        new(CanonicalId.Mint(), ScriptureSourceOperationKinds.Create, entityId: item,
            target: CanonicalId.FromGuid(cache.LangProject.Guid),
            after: JsonSerializer.SerializeToElement(new { id, name, versification, context, kind }));

    private static OperationEnvelope Set(string kind, CanonicalId item, string property, string value) =>
        new(CanonicalId.Mint(), kind, target: item, after: JsonSerializer.SerializeToElement(
            new Dictionary<string, string> { [property] = value }));

    private static void Apply(LcmCache cache, params OperationEnvelope[] operations) =>
        UndoableUnitOfWorkHelper.Do("test", "test", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            foreach (var operation in operations)
                OperationHandlerRegistry.Resolve(operation.Kind, "test").ApplyAndCaptureEffect(cache, operation, []);
        });

    [Fact]
    public void AddingASourceCreatesTheListAndTheFieldsAndReadsBackExactly()
    {
        using var cache = pristine.NewScratch();
        var item = CanonicalId.Mint();

        Apply(cache, Create(cache, item, "WEB"));

        var entry = Assert.Single(SourceList.Read(cache));
        Assert.True(entry.IsValid, entry.Error);
        Assert.Equal("WEB", entry.Id);
        Assert.Equal("World English Bible", entry.Name);
        Assert.Equal("GEN-ROM; 1CO 13-15", entry.ContextText);
        Assert.Equal("English", entry.VersificationName);
        Assert.Equal(SourceKind.Paratext, entry.Kind);
        Assert.Equal(SourceStatus.Active, entry.Status);
        var created = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().GetObject(item.ToGuid());
        Assert.Equal(item.ToGuid(), created.Guid);
    }

    [Fact]
    public void ASecondSourceGoesIntoTheSameList()
    {
        using var cache = pristine.NewScratch();

        Apply(cache, Create(cache, CanonicalId.Mint(), "WEB"), Create(cache, CanonicalId.Mint(), "CEB", "Cebuano", "Original", "NT"));

        Assert.Equal(["WEB", "CEB"], SourceList.Read(cache).Select(entry => entry.Id));
        Assert.Single(cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances(),
            list => list.Name.AvailableWritingSystemIds.Any(ws => list.Name.get_String(ws).Text == SourceListFormat.ListName));
    }

    [Theory]
    [InlineData("WEB")]
    [InlineData("web")]
    public void AnIdAlreadyInTheListIsRefusedWhateverItsCase(string again)
    {
        using var cache = pristine.NewScratch();
        Apply(cache, Create(cache, CanonicalId.Mint(), "WEB"));

        var exception = Assert.Throws<InvalidOperationException>(() => Apply(cache, Create(cache, CanonicalId.Mint(), again)));

        Assert.Contains("already in use", exception.Message);
    }

    [Theory]
    [InlineData("", "Paratext")]
    [InlineData("W EB", "Paratext")]
    [InlineData("WEB", "Corpus")]
    public void AnUnusableIdOrKindIsRefused(string id, string kind)
    {
        using var cache = pristine.NewScratch();

        Assert.Throws<InvalidOperationException>(() => Apply(cache, Create(cache, CanonicalId.Mint(), id, kind: kind)));
    }

    [Fact]
    public void TheSettersChangeOnlyTheirOwnPart()
    {
        using var cache = pristine.NewScratch();
        var item = CanonicalId.Mint();
        Apply(cache, Create(cache, item, "WEB"));

        Apply(cache,
            Set(ScriptureSourceOperationKinds.SetName, item, "name", "Berean"),
            Set(ScriptureSourceOperationKinds.SetContext, item, "context", "MAT-JHN"),
            Set(ScriptureSourceOperationKinds.SetVersification, item, "versification", "Original"),
            Set(ScriptureSourceOperationKinds.SetStatus, item, "status", "Removed"));

        var entry = Assert.Single(SourceList.Read(cache));
        Assert.Equal("WEB", entry.Id);
        Assert.Equal("Berean", entry.Name);
        Assert.Equal("MAT-JHN", entry.ContextText);
        Assert.Equal("Original", entry.VersificationName);
        Assert.Equal(SourceStatus.Removed, entry.Status);
        Apply(cache, Set(ScriptureSourceOperationKinds.SetStatus, item, "status", "Active"));
        Assert.Equal(SourceStatus.Active, Assert.Single(SourceList.Read(cache)).Status);
    }

    [Fact]
    public void RenamingAndRecontextingKeepsEveryOtherWritingSystemAlternative()
    {
        using var cache = pristine.NewScratch();
        var item = CanonicalId.Mint();
        Apply(cache, Create(cache, item, "WEB"));
        cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var french);
        var possibility = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().GetObject(item.ToGuid());
        UndoableUnitOfWorkHelper.Do("test", "test", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            possibility.Name.set_String(french.Handle, TsStringUtils.MakeString("Bible Anglaise", french.Handle));
            possibility.Description.set_String(french.Handle, TsStringUtils.MakeString("Genese a Romains", french.Handle));
        });

        Apply(cache,
            Set(ScriptureSourceOperationKinds.SetName, item, "name", "Berean"),
            Set(ScriptureSourceOperationKinds.SetContext, item, "context", "MAT-JHN"));

        // Stored alternatives, not the visible name, which depends on SourceList.
        Assert.Equal("Berean", possibility.Name.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal("MAT-JHN", possibility.Description.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal("Bible Anglaise", possibility.Name.get_String(french.Handle).Text);
        Assert.Equal("Genese a Romains", possibility.Description.get_String(french.Handle).Text);
    }

    [Fact]
    public void AnUnknownStatusIsRefused()
    {
        using var cache = pristine.NewScratch();
        var item = CanonicalId.Mint();
        Apply(cache, Create(cache, item, "WEB"));

        Assert.Throws<InvalidOperationException>(() =>
            Apply(cache, Set(ScriptureSourceOperationKinds.SetStatus, item, "status", "Gone")));
    }

    [Fact]
    public void ReadingTheFootprintWritesNothingAndSeesAnIdHeldByAnotherItem()
    {
        using var cache = pristine.NewScratch();
        var item = CanonicalId.Mint();
        var create = Create(cache, item, "WEB");

        var before = OperationHandlerRegistry.Resolve(create.Kind, "test").ReadCurrentFootprint(cache, create);

        Assert.Empty(SourceList.Read(cache));
        Assert.Empty(before.Before);
        Apply(cache, Create(cache, CanonicalId.Mint(), "WEB"));
        var held = OperationHandlerRegistry.Resolve(create.Kind, "test").ReadCurrentFootprint(cache, create);
        Assert.True(held.Before.ContainsKey("idHeldBy"));
    }

    [Fact]
    public void AnIncompatibleReservedFieldFailsTheCheckAndTheAdd()
    {
        using var cache = pristine.NewScratch();
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.AddCustomField("CmPossibility", SourceListFormat.StatusField, CellarPropertyType.Integer, 0,
                "status", WritingSystemServices.kwsAnal, Guid.Empty));

        Assert.Throws<InvalidOperationException>(() => ScriptureSourceOperationKinds.RequireCompatibleFields(cache));
        Assert.Throws<InvalidOperationException>(() => Apply(cache, Create(cache, CanonicalId.Mint(), "WEB")));
        Assert.Empty(SourceList.Read(cache));
    }

    [Fact]
    public void ACompatibleProjectWithNoFieldsPassesTheCheck()
    {
        using var cache = pristine.NewScratch();

        ScriptureSourceOperationKinds.RequireCompatibleFields(cache);
    }

    [Fact]
    public void EveryKindHasASlotPolicy()
    {
        foreach (var kind in new[]
                 {
                     ScriptureSourceOperationKinds.Create, ScriptureSourceOperationKinds.SetName, ScriptureSourceOperationKinds.SetContext,
                     ScriptureSourceOperationKinds.SetVersification, ScriptureSourceOperationKinds.SetStatus,
                 })
            Assert.Contains(kind, OperationSlotRegistry.RegisteredKinds);
    }
}
