using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>An intent can name an object by its form and gloss instead of by an id that differs between projects.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class LookupReferencesTests(PristineProjectFixture pristine)
{
    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    [Fact]
    public void AFormAndAGlossNameTheEntryTheSenseAndTheAnalysisThatSurviveACopyOfTheProject()
    {
        using var cache = pristine.NewScratch();
        var seed = pristine.Seed;
        Assert.Equal(Id(seed.FirstEntryId), LookupReferences.ResolveOne(cache, "lookup:entry:" + SeededProject.FirstForm));
        Assert.Equal(Id(seed.SecondEntryId),
            LookupReferences.ResolveOne(cache, $"lookup:entry:{SeededProject.SecondForm}|{SeededProject.SecondGloss}"));
        Assert.Equal(Id(seed.FirstSenseId),
            LookupReferences.ResolveOne(cache, $"lookup:sense:{SeededProject.FirstForm}|{SeededProject.FirstGloss}"));
        var msa = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId).MorphoSyntaxAnalysesOC.Single();
        Assert.Equal(Id(msa.Guid), LookupReferences.ResolveOne(cache, "lookup:msa:" + SeededProject.FirstForm));
        Assert.Equal(Id(seed.PartOfSpeechId), LookupReferences.ResolveOne(cache, "lookup:category:seedednoun"));
    }

    [Fact]
    public void ANameThatMatchesNothingOrManyObjectsIsRefusedWithTheCandidates()
    {
        using var cache = pristine.NewScratch();
        var missing = Assert.Throws<InvalidOperationException>(() => LookupReferences.ResolveOne(cache, "lookup:entry:absent"));
        Assert.Contains("matches nothing", missing.Message, StringComparison.Ordinal);

        var vernacular = cache.DefaultVernWs;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
                cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString(SeededProject.FirstForm, vernacular), "homograph gloss",
                new SandboxGenericMSA { MsaType = MsaType.kStem }));
        var ambiguous = Assert.Throws<InvalidOperationException>(
            () => LookupReferences.ResolveOne(cache, "lookup:entry:" + SeededProject.FirstForm));
        Assert.Contains("matches 2 objects", ambiguous.Message, StringComparison.Ordinal);
        Assert.Contains(Id(pristine.Seed.FirstEntryId), ambiguous.Message, StringComparison.Ordinal);
        Assert.Equal(Id(pristine.Seed.FirstEntryId),
            LookupReferences.ResolveOne(cache, $"lookup:entry:{SeededProject.FirstForm}|{SeededProject.FirstGloss}"));
    }

    [Fact]
    public void OnlyLookupStringsChangeAndIdsPassThrough()
    {
        using var cache = pristine.NewScratch();
        var intent = JsonSerializer.SerializeToElement(new
        {
            entry = "lookup:entry:" + SeededProject.FirstForm,
            other = Id(pristine.Seed.SecondEntryId),
            nested = new[] { new { sense = $"lookup:sense:{SeededProject.SecondForm}" } },
        });
        var resolved = LookupReferences.Resolve(cache, intent);
        Assert.Equal(Id(pristine.Seed.FirstEntryId), resolved.GetProperty("entry").GetString());
        Assert.Equal(Id(pristine.Seed.SecondEntryId), resolved.GetProperty("other").GetString());
        Assert.Equal(Id(pristine.Seed.SecondSenseId), resolved.GetProperty("nested")[0].GetProperty("sense").GetString());

        var plain = JsonSerializer.SerializeToElement(new { entry = Id(pristine.Seed.FirstEntryId) });
        Assert.Equal(plain.GetRawText(), LookupReferences.Resolve(cache, plain).GetRawText());
    }

    [Theory]
    [InlineData("lookup:entry")]
    [InlineData("lookup:thing:x")]
    [InlineData("lookup:entry:")]
    public void AMalformedReferenceIsRefused(string reference)
    {
        using var cache = pristine.NewScratch();
        Assert.Throws<InvalidOperationException>(() => LookupReferences.ResolveOne(cache, reference));
    }

    [Fact]
    public void AGlossOrFormThatBeginsWithTheLookupPrefixIsWrittenAsIsNotRefused()
    {
        using var cache = pristine.NewScratch();
        var intent = JsonSerializer.SerializeToElement(new
        {
            entry = "lookup:entry:" + SeededProject.FirstForm,
            form = "lookup:thing", gloss = "lookup: to search", nested = new { glosses = new[] { "lookup:x:y" } },
        });
        var resolved = LookupReferences.Resolve(cache, intent);
        Assert.Equal(Id(pristine.Seed.FirstEntryId), resolved.GetProperty("entry").GetString());
        Assert.Equal("lookup:thing", resolved.GetProperty("form").GetString());
        Assert.Equal("lookup: to search", resolved.GetProperty("gloss").GetString());
        Assert.Equal("lookup:x:y", resolved.GetProperty("nested").GetProperty("glosses")[0].GetString());
    }

    [Fact]
    public void ANameFindsAnEntryAnEarlierIntentOfTheSameBatchCreated()
    {
        string path;
        string vern;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            vern = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
        }
        Assert.True(ProposalCommands.New(new(path, "1.0", "samebatch", "Same batch")).Succeeded);
        var stem = Id(MoMorphTypeTags.kguidMorphStem);
        var batch = ProposalCommands.ComposeBatch(new(path, "1.0", "samebatch", JsonSerializer.Serialize(new
        {
            intents = new object[]
            {
                new
                {
                    verb = "compose-author-lexical-entry",
                    intent = new
                    {
                        form = "batchnew", formWs = vern, morphType = stem, gloss = "fresh", analysisWs = "en",
                        msa = new { kind = "stem", category = "lookup:category:SeededNoun" },
                    },
                },
                new
                {
                    verb = "compose-author-alternate-form",
                    intent = new { entry = "lookup:entry:batchnew|fresh", form = "batchvar", formWs = vern, morphType = stem },
                },
            },
        })));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
    }

    [Fact]
    public void AHomographOfAnExistingEntryIsANewEntryAndALaterIntentNamesItByGloss()
    {
        string path;
        using (var cache = pristine.NewScratch()) path = cache.ProjectId.Path;
        Assert.True(ProposalCommands.New(new(path, "1.0", "homograph", "Homograph")).Succeeded);
        var entry = JsonSerializer.Serialize(new
        {
            form = SeededProject.FirstForm, formWs = "en-x-vern", morphType = Id(MoMorphTypeTags.kguidMorphStem),
            gloss = "turn", analysisWs = "en",
            msa = new { kind = "stem", category = "lookup:category:SeededNoun" },
        });
        using (var cache = pristine.NewScratch())
            entry = entry.Replace("en-x-vern", cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id);
        var created = ProposalCommands.ComposeIntent(new(path, "1.0", "homograph", "compose-author-lexical-entry", entry));
        Assert.True(created.Succeeded, created.Refusal?.Message);

        var variant = JsonSerializer.Serialize(new
        {
            entry = $"lookup:entry:{SeededProject.FirstForm}|turn", form = SeededProject.FirstForm + "x",
            formWs = JsonDocument.Parse(entry).RootElement.GetProperty("formWs").GetString(),
            morphType = Id(MoMorphTypeTags.kguidMorphStem),
        });
        var named = ProposalCommands.ComposeIntent(new(path, "1.0", "homograph", "compose-author-alternate-form", variant));
        Assert.True(named.Succeeded, named.Refusal?.Message);
        var ambiguous = ProposalCommands.ComposeIntent(new(path, "1.0", "homograph", "compose-author-alternate-form",
            variant.Replace("|turn", "")));
        Assert.False(ambiguous.Succeeded);
        Assert.Contains("matches 2 objects", ambiguous.Refusal!.Message, StringComparison.Ordinal);
    }
}
