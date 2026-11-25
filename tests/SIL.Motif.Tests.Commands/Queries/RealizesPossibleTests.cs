using SIL.LCModel;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Commands.RecordedMorphemes;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>realizes(M, possible)</c> over effects replies recorded from PanGloss v0.33.0.</summary>
public sealed class RealizesPossibleTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("realizes-possible").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string Words(params string[] words)
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(words.Select((word, index) => word + "\t" + (20 - index) + "\n")));
        return path;
    }

    private static EffectsResult Effects(string recording, params string[] forms) => EffectsResult.Read(RecordedRealizes.FillForms(recording, forms));

    [Fact]
    public void ARuleWithALiteralReplacementComputesTheVariantAndKeepsItsEnvironment()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.KatRoot, "kat"));

        Assert.Equal(["kat", "kad"], set.Forms.Select(form => form.Form));
        Assert.Equal(["Voicing"], set.Forms.Single(form => form.Form == "kad").Rules);
        var rule = Assert.Single(set.Rules!);
        Assert.Equal(("Voicing", "[V] _", "/t/ / [V] _", "/d/"), (rule.Name, rule.Environment, rule.MatchPattern, rule.Rhs));
        Assert.Equal(["left", "right"], rule.Edges);
        Assert.Equal("unknown/environment_unchecked", rule.Verdict);
        Assert.Equal(1, set.Effects!.Rules);
        Assert.Empty(set.Phon!);
        Assert.False(set.Complete);
        Assert.Contains("context_combinations_untested", set.Reasons.Select(reason => reason.Code));
        Assert.Equal("none", set.Anchor);
    }

    [Fact]
    public void ASuffixWithNoTargetOnItHasNoRulesAndSaysItIsPartial()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.SuffixNotOnForm, "+s"), 0, "");

        Assert.Equal(["s"], set.Forms.Select(form => form.Form));
        Assert.Empty(set.Rules!);
        Assert.Equal((0, 1), (set.Effects!.Rules, set.Effects.NotOnForm));
        Assert.Equal("end", set.Anchor);
        Assert.Equal(["form_partial"], set.Reasons.Select(reason => reason.Code));
    }

    [Fact]
    public void AFormPartialPrefixIsSearchedByTheRulePatternNarrowedToItsContext()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.PrefixTa, "ta+", "ta+"));

        Assert.Equal("start", set.Anchor);
        Assert.Equal(["ta"], set.Forms.Select(form => form.Form));
        Assert.Equal(2, set.Phon!.Count);
        var clause = set.Phon.Single(item => item.Side == "input");
        Assert.Equal(("/t/ / [V] _", "Voicing", @"\A.a"), (clause.Pattern, clause.Rule, clause.Context));
        Assert.False(set.Complete);
        Assert.Contains("form_partial", set.Reasons.Select(reason => reason.Code));
    }

    [Fact]
    public void AMetathesisRuleThatMayStraddleAnEdgeIsNamedAndMakesTheSetIncomplete()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.MetathesisStraddle, "tika"));

        var codes = set.Reasons.Select(reason => reason.Code).ToList();
        Assert.Contains("unmodelled:lhs_may_straddle_edge", codes);
        Assert.Contains("metathesis", codes);
        Assert.False(set.Complete);
    }

    [Fact]
    public void ThePossibleSetSearchesTheCorpusAndALabelledHitNamesItsForm()
    {
        var grammar = new ReplayGrammar { OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.KatRoot, "kat") };
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(Words("kat", "kad", "kata", "rad"), Where: $"realizes({KatEntry}:{KatMsa}, possible)",
            GrammarPath: "unused"), null, CancellationToken.None, null, realizesGrammar: grammar);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["kad", "kat", "kata"], outcome.Value!.Rows.Select(row => row.Form).Order(StringComparer.Ordinal));
        var set = Assert.Single(outcome.Value.Realizes!);
        Assert.Equal(("possible", false), (set.Tier, set.Complete));
        Assert.Equal(["kat", "kad"], set.Forms.Select(form => form.Form));
        Assert.Equal(KatEntry, grammar.Effected.Single().Morpheme.EntryGuid);
        Assert.Equal(KatMsa, grammar.Effected.Single().Morpheme.MsaGuid);
        Assert.Equal(["kad"], outcome.Value.Rows.Single(row => row.Form == "kad").Realizes!.Single().Forms);
    }

    [Fact]
    public void NegationOverACompletePossibleSetIsAllowedAndOverAnIncompleteOneIsRefused()
    {
        var complete = new ReplayGrammar { OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.KatNoRules, "kat") };
        var kept = CorpusQueryCommand.Forms(new CorpusFormsRequest(Words("kat", "kad", "rad"), Where: $"not realizes({KatEntry}:{KatMsa}, possible)",
            GrammarPath: ProcessFreeGrammar()), null, CancellationToken.None, null, realizesGrammar: complete);
        Assert.Equal(["kad", "rad"], kept.Value!.Rows.Select(row => row.Form).Order(StringComparer.Ordinal));

        var withRule = new ReplayGrammar { OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.KatRoot, "kat") };
        var surviving = CorpusQueryCommand.Forms(new CorpusFormsRequest(Words("kat", "kad", "rad"), Where: $"not realizes({KatEntry}:{KatMsa}, possible)",
            GrammarPath: ProcessFreeGrammar()), null, CancellationToken.None, null, realizesGrammar: withRule);
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, surviving.Refusal!.Code);

        var incomplete = new ReplayGrammar { OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.PrefixTa, "ta+", "ta+") };
        var refused = CorpusQueryCommand.Forms(new CorpusFormsRequest(Words("tada"), Where: $"not realizes({TaEntry}:{TaMsa}, possible)",
            GrammarPath: "unused"), null, CancellationToken.None, null, realizesGrammar: incomplete);
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, refused.Refusal!.Code);
        Assert.Contains("form_partial", refused.Refusal.Facts!["reasons"]);
    }

    [Fact]
    public void ARuleThatNeedsAPatternSearchLowersToTheMatchPatternAndItsContext()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.PrefixTa, "ta+", "ta+"));
        var result = new RealizesResult(RealizesResult.SchemaName, "ta", "possible", false, set.Reasons, null, set.Forms,
            RealizesPatternCompiler.Pattern(["ta"], "start"), Phon: set.Phon);

        var lowered = RealizesLowering.Lower(new QueryRealizes("ta", "possible"), _ => (result, ["ta"]));

        var any = Assert.IsType<QueryOr>(lowered);
        Assert.IsType<QueryField>(any.Items[0]);
        var and = Assert.IsType<QueryAnd>(any.Items[1]);
        Assert.Equal("/t/ / [V] _", Assert.IsType<QueryEnv>(and.Items[0]).Environment);
        Assert.Equal(@"\A.a", Assert.IsType<QueryField>(and.Items[1]).Value);
    }

    [Fact]
    public void AMorphemeNamedByOneIdOrAFormCannotBeAskedOfPanGloss()
    {
        var grammar = new ReplayGrammar { OnEffects = _ => throw new InvalidOperationException() };
        var one = CorpusQueryCommand.Forms(new CorpusFormsRequest(Words("kat"), Where: $"realizes({KatEntry}, possible)", GrammarPath: "unused"),
            null, CancellationToken.None, null, realizesGrammar: grammar);
        Assert.Equal(RefusalCodes.QueryRealizesNeedsIdentity, one.Refusal!.Code);
        Assert.Empty(grammar.Effected);
    }

    private string ProcessFreeGrammar() => WriteGrammar();

    // A grammar file built with the XML API: a root, then one rt per class with an optional morph type.
    private string WriteGrammar(params (string Class, string? MorphType)[] objects)
    {
        var root = new System.Xml.Linq.XElement("languageproject", new System.Xml.Linq.XAttribute("version", "1"));
        foreach (var (cls, type) in objects)
        {
            var item = new System.Xml.Linq.XElement("rt", new System.Xml.Linq.XAttribute("class", cls), new System.Xml.Linq.XAttribute("guid", Guid.NewGuid()));
            if (type is not null)
                item.Add(new System.Xml.Linq.XElement("MorphType", new System.Xml.Linq.XElement("objsur",
                    new System.Xml.Linq.XAttribute("guid", type), new System.Xml.Linq.XAttribute("t", "r"))));
            root.Add(item);
        }
        var path = Path.Combine(_folder, "grammar" + Directory.GetFiles(_folder).Length + ".fwdata");
        new System.Xml.Linq.XDocument(root).Save(path);
        return path;
    }

    private static string[] Codes(RealizesVariants set) => [.. set.Reasons.Select(reason => reason.Code)];

    [Fact]
    public void ARuleItCannotApplyByHandIsSearchedOnBothSidesOfTheRuleAndTheOutputSideIsTheOneThatFindsChangedWords()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.PrefixTa, "ta+", "ta+"));

        Assert.Equal([("/t/ / [V] _", "input"), ("/d/ / [V] _", "output")], set.Phon!.Select(clause => (clause.Pattern, clause.Side)));
        var words = new[] { "kad", "kat", "kata", "kada" };
        var input = ResidentPhonMatcher.Read(RecordedRealizes.FillWords(RecordedRealizes.KatInputSide, words));
        var output = ResidentPhonMatcher.Read(RecordedRealizes.FillWords(RecordedRealizes.KatOutputSide, words));
        Assert.Equal(["kat", "kata"], input.Words.Where(word => word.Spans.Count > 0).Select(word => word.Word));
        Assert.Equal(["kad", "kada"], output.Words.Where(word => word.Spans.Count > 0).Select(word => word.Word));
    }

    [Fact]
    public void ARuleThatWritesAClassHasNoSearchableOutputAndSaysSo()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.KatClassRhs, "kat"));

        Assert.Contains("output_side_unsearched", Codes(set));
        Assert.False(set.Complete);
        Assert.Equal(["kat"], set.Forms.Select(form => form.Form));
        Assert.All(set.Phon!, clause => Assert.Equal("input", clause.Side));
    }

    [Fact]
    public void TwoRulesThatCanChangeTheMorphemeMayFeedOrBleedEachOtherSoTheSetIsIncomplete()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.KatTwoRules, "kat"));

        Assert.Contains("rule_interaction", Codes(set));
        Assert.False(set.Complete);
        Assert.Equal(2, set.Effects!.Rules);
        Assert.Equal(["kat", "kad"], set.Forms.Select(form => form.Form));
    }

    [Fact]
    public void ARuleThatDeletesTheWholeMorphemeGivesTheEmptyVariantAndIsNeverDropped()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.WholeStemDeleted, "t"));

        Assert.Contains(RealizesPossible.Unrealised, set.Forms.Select(form => form.Form));
        Assert.Equal(["Voicing"], set.Forms.Single(form => form.Form == RealizesPossible.Unrealised).Rules);
        Assert.Contains("may_be_unrealised", Codes(set));
        Assert.False(set.Complete);
    }

    [Fact]
    public void ADeletedTargetIsAVariantOfTheMorphemeAndTheRuleIsNonLocal()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.KatDeleting, "kat"));

        Assert.Equal(["kat", "ka"], set.Forms.Select(form => form.Form));
        Assert.Contains("nonlocal", Codes(set));
    }

    [Fact]
    public void TheUnmodelledCountIncludesStraddleAndFedRulesWithAPerReasonBreakdown()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.MetathesisStraddle, "tika"));

        Assert.Equal(1, set.Effects!.Unmodelled);
        Assert.Equal(1, set.Effects.UnmodelledBy!["lhs_may_straddle_edge"]);
    }

    [Fact]
    public void PossibleNamedByIdsNeverOpensTheProject()
    {
        var grammar = new ReplayGrammar { OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.KatRoot, "kat") };
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(Words("kat", "kad"), Where: $"realizes({KatEntry}:{KatMsa}, possible)",
            GrammarPath: "unused", ProjectPath: Path.Combine(_folder, "no-such-project.fwdata")), null, CancellationToken.None, null, realizesGrammar: grammar);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
    }

    [Fact]
    public void ARuleFreeMorphemeIsCompleteOnlyWhenTheGrammarIsShownFreeOfAffixProcesses()
    {
        var effects = Effects(RecordedRealizes.KatNoRules, "kat");

        var shownFree = RealizesPossible.Reduce(effects, 0, "");
        Assert.True(shownFree.Complete);
        Assert.Empty(shownFree.Reasons);
        var infix = RealizesPossible.Reduce(effects, 0, "affix process");
        Assert.False(infix.Complete);
        Assert.Equal("morphological_process", Assert.Single(infix.Reasons).Code);
        var unknown = RealizesPossible.Reduce(effects);
        Assert.False(unknown.Complete);
        Assert.Equal("morphological_process", Assert.Single(unknown.Reasons).Code);
    }

    [Fact]
    public void AnAffixProcessOrAnAffixOfAnotherMorphTypeInTheGrammarFileIsFoundAndAPlainOneIsNot()
    {
        var plain = WriteGrammar(("MoAffixAllomorph", MoMorphTypeTags.kguidMorphEnclitic.ToString()));
        var process = WriteGrammar(("MoAffixProcess", null));
        var infix = WriteGrammar(("MoAffixAllomorph", MoMorphTypeTags.kguidMorphSimulfix.ToString()));

        Assert.Equal("", RealizesLexicon.Processes(plain));
        Assert.Equal("affix process", RealizesLexicon.Processes(process));
        Assert.Equal("affix of another morph type", RealizesLexicon.Processes(infix));
        var notXml = Path.Combine(_folder, "plain.txt");
        File.WriteAllText(notXml, "nothing");
        Assert.Null(RealizesLexicon.Processes(notXml));
    }

    [Fact]
    public void AnUntypedAffixACompoundingRuleOrAStemOfAnotherMorphTypeCountsAsAProcess()
    {
        Assert.Equal("affix with no morph type", RealizesLexicon.Processes(WriteGrammar(("MoAffixAllomorph", null))));
        Assert.Equal("compounding rule", RealizesLexicon.Processes(WriteGrammar(("MoEndoCompound", null))));
        Assert.Equal("compounding rule", RealizesLexicon.Processes(WriteGrammar(("MoExoCompound", null))));
        Assert.Equal("stem of another morph type", RealizesLexicon.Processes(WriteGrammar(("MoStemAllomorph", MoMorphTypeTags.kguidMorphPhrase.ToString()))));
        Assert.Equal("stem with no morph type", RealizesLexicon.Processes(WriteGrammar(("MoStemAllomorph", null))));
        Assert.Equal("", RealizesLexicon.Processes(WriteGrammar(("MoStemAllomorph", MoMorphTypeTags.kguidMorphBoundStem.ToString()))));
    }

    [Fact]
    public void AStemWithARuleAtBothEdgesIsNeverCompleteBecauseTheEdgesAreNotCombined()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.KatRoot, "kat"), 0, "");

        Assert.Equal(["left", "right"], set.Rules!.Single().Edges);
        Assert.Contains("context_combinations_untested", Codes(set));
        Assert.False(set.Complete);
    }

    [Fact]
    public void ARuleWithAnEmptyTargetIsUnprintableAndSearchedByNeitherPhonNorEnv()
    {
        var set = RealizesPossible.Reduce(Effects(RecordedRealizes.KatEpenthesis, "kat"), 0, "");

        Assert.Empty(set.Phon!);
        Assert.Contains("unprintable", Codes(set));
        Assert.False(set.Complete);
    }
}
