using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>The MCP lexicon and grammar reads say what a bare exact form also matched and what they read at, as the CLI does.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class GrammarReadAtParityTests(PristineProjectFixture pristine)
{
    private const string UnknownProposal = "AaHnUob1K5UlJwJZtEhh_w";

    private WalkthroughProject CaptureProject()
    {
        var project = new WalkthroughProject(pristine, configure: (cache, _, _) =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                void Entry(string form, Guid morphType, bool affix)
                {
                    var entry = services.GetInstance<ILexEntryFactory>().Create();
                    IMoForm lexeme = affix
                        ? services.GetInstance<IMoAffixAllomorphFactory>().Create()
                        : services.GetInstance<IMoStemAllomorphFactory>().Create();
                    entry.LexemeFormOA = lexeme;
                    lexeme.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(morphType);
                    lexeme.Form.set_String(cache.DefaultVernWs, form);
                }

                Entry("ka", MoMorphTypeTags.kguidMorphStem, affix: false);
                Entry("zi", MoMorphTypeTags.kguidMorphStem, affix: false);
                Entry("zi", MoMorphTypeTags.kguidMorphPrefix, affix: true);
            }));
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return project;
    }

    private static string[] Headwords(System.Text.Json.Nodes.JsonObject result) => result["entries"]!.AsArray()
        .Select(entry => entry!["headword"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void ABareExactFormSaysItAlsoMatchedTheMarkedAffixAndAMarkedFormOrAMorphTypeSaysNothing()
    {
        using var project = CaptureProject();
        LexiconFilter Filter(string exact, params string[] types) => new(Exact: exact, MorphTypesOrNull: types);

        var bare = BaselineReads.ReadLexicon(project.FwDataPath, Filter("zi"), 25, 0, detailed: false);
        Assert.True(bare.Succeeded, bare.Refusal?.Message);
        Assert.Equal(["zi", "zi-"], Headwords(bare.Value!));
        var note = bare.Value!["exactNote"]!.GetValue<string>();
        Assert.Contains("also matched 1 marked headword(s): zi-", note, StringComparison.Ordinal);
        Assert.Contains("Give the marker (for example 'zi-')", note, StringComparison.Ordinal);

        Assert.False(BaselineReads.ReadLexicon(project.FwDataPath, Filter("zi-"), 25, 0, false).Value!.ContainsKey("exactNote"));
        Assert.False(BaselineReads.ReadLexicon(project.FwDataPath, Filter("ka"), 25, 0, false).Value!.ContainsKey("exactNote"));
        var narrowed = BaselineReads.ReadLexicon(project.FwDataPath, Filter("zi", "prefix"), 25, 0, false);
        Assert.Equal(["zi-"], Headwords(narrowed.Value!));
        Assert.False(narrowed.Value!.ContainsKey("exactNote"));
    }

    [Fact]
    public void AtBaselineOrAbsentReadsTheBaselineAndSaysSo()
    {
        using var project = CaptureProject();

        foreach (var at in new string?[] { null, "baseline" })
        {
            var lexicon = BaselineReads.ReadLexicon(project.FwDataPath, new LexiconFilter(Exact: "ka"), 25, 0, false, at);
            Assert.True(lexicon.Succeeded, lexicon.Refusal?.Message);
            Assert.Equal("baseline", lexicon.Value!["at"]!.GetValue<string>());
            Assert.False(lexicon.Value!.ContainsKey("atLabel"));
            Assert.NotNull(lexicon.Value!["source"]);
        }

        var grammar = BaselineReads.ReadAt(project.FwDataPath, "baseline", cache => GrammarReader.Summary(cache));
        Assert.Equal("baseline", grammar.Value!["at"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("last-week")]
    [InlineData("  ")]
    public void AnAtThatIsNeitherBaselineNorAProposalIdIsRefusedAsInvalid(string at)
    {
        using var project = CaptureProject();

        var lexicon = BaselineReads.ReadLexicon(project.FwDataPath, new LexiconFilter(), 25, 0, false, at);
        var grammar = BaselineReads.ReadAt(project.FwDataPath, at, cache => GrammarReader.Summary(cache));

        Assert.Equal("grammar-read.invalid-request", lexicon.Refusal!.Code);
        Assert.Equal("grammar-read.invalid-request", grammar.Refusal!.Code);
        Assert.DoesNotContain("--at", lexicon.Refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAtProposalWithNoTrialIsRefusedAsTrialNone()
    {
        using var project = CaptureProject();

        var lexicon = BaselineReads.ReadLexicon(project.FwDataPath, new LexiconFilter(), 25, 0, false, UnknownProposal);
        var grammar = BaselineReads.ReadAt(project.FwDataPath, UnknownProposal, cache => GrammarReader.Summary(cache));

        Assert.Equal("trial.none", lexicon.Refusal!.Code);
        Assert.Equal("trial.none", grammar.Refusal!.Code);
    }
}
