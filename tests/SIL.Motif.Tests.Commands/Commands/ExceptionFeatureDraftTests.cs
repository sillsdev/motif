using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ExceptionFeatureDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static string Intent(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void ComposeIntentStagesANewExceptionFeatureWithItsNameAndAbbreviation()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "feature", "Add a feature")).Succeeded);

        var staged = ProposalCommands.ComposeIntent(new(path, Version, "feature", "compose-author-exception-feature",
            Intent(new { name = "Loan stem", ws, abbreviation = "loan" })));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(["lists/cmPossibilityList/createPossibilities", "lists/cmPossibility/setName",
                "lists/cmPossibility/setAbbreviation"],
            staged.Value!.Operations.Select(item => item.Kind));
    }

    [Fact]
    public void AFeatureStagedInADraftCanBeNamedByLaterComposersInTheSameDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "same", "Use a new feature")).Succeeded);
        var feature = ProposalCommands.ComposeIntent(new(path, Version, "same", "compose-author-exception-feature",
            Intent(new { name = "Loan stem", ws })));
        Assert.True(feature.Succeeded, feature.Refusal?.Message);
        var featureId = Assert.Single(feature.Value!.Operations,
            item => item.Kind == "lists/cmPossibilityList/createPossibilities").EntityId!;
        var entry = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, "same", Intent(new
        {
            form = "pag", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
            gloss = "agentive", analysisWs = ws, msa = new { kind = "derivational", category, toCategory = category },
        })));
        Assert.True(entry.Succeeded, entry.Refusal?.Message);
        var msaId = Assert.Single(entry.Value!.Operations,
            item => item.Kind == "lexical/lexEntry/createMorphoSyntaxAnalyses").EntityId!;

        var restricted = ProposalCommands.ComposeIntent(new(path, Version, "same",
            "compose-edit-derivational-affix-restriction", Intent(new { msa = msaId, fromExceptionFeatures = new[] { featureId } })));
        var stem = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, "same", Intent(new
        {
            form = "kaun", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            gloss = "eat", analysisWs = ws, msa = new { kind = "stem", category },
        })));
        Assert.True(stem.Succeeded, stem.Refusal?.Message);
        var stemMsa = Assert.Single(stem.Value!.Operations,
            item => item.Kind == "lexical/lexEntry/createMorphoSyntaxAnalyses").EntityId!;
        var tagged = ProposalCommands.ComposeIntent(new(path, Version, "same", "compose-edit-exception-features",
            Intent(new { msa = stemMsa, exceptionFeatures = new[] { featureId } })));

        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "same",
            Intent(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var phonemeId = Assert.Single(phoneme.Value!.Operations,
            item => item.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!;
        var rule = ProposalCommands.ComposeIntent(new(path, Version, "same", "compose-author-phonological-rule", Intent(new
        {
            name = "insert a", direction = "left-to-right", input = Array.Empty<object>(),
            output = new[] { new { phoneme = phonemeId } }, left = Array.Empty<object>(), right = Array.Empty<object>(),
        })));
        Assert.True(rule.Succeeded, rule.Refusal?.Message);
        var ruleId = Assert.Single(rule.Value!.Operations,
            item => item.Kind == "grammar/phPhonData/createPhonRules").EntityId!;
        var skipped = ProposalCommands.ComposeIntent(new(path, Version, "same", "compose-edit-phonological-rule-features",
            Intent(new { rule = ruleId, excludedFeatures = new[] { featureId } })));

        Assert.True(restricted.Succeeded, restricted.Refusal?.Message);
        Assert.True(tagged.Succeeded, tagged.Refusal?.Message);
        Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
    }

    [Fact]
    public void ATagWithAFeatureTheProjectDoesNotHoldIsRefusedAndStagesNothing()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "tag", "Tag a stem")).Succeeded);

        var refused = ProposalCommands.ComposeIntent(new(path, Version, "tag", "compose-edit-exception-features",
            Intent(new
            {
                msa = CanonicalId.FromGuid(Guid.NewGuid()).Value,
                exceptionFeatures = new[] { CanonicalId.FromGuid(Guid.NewGuid()).Value },
            })));

        Assert.False(refused.Succeeded);
    }

    [Fact]
    public void OneIntentTagsManyStemsStagedInTheSameDraftAndIsOnlyOnTheCommandLine()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "bulk", "Tag stems")).Succeeded);
        var feature = ProposalCommands.ComposeIntent(new(path, Version, "bulk", "compose-author-exception-feature",
            Intent(new { name = "Loan stem", ws })));
        Assert.True(feature.Succeeded, feature.Refusal?.Message);
        var featureId = Assert.Single(feature.Value!.Operations,
            item => item.Kind == "lists/cmPossibilityList/createPossibilities").EntityId!;
        var msas = new List<string>();
        foreach (var form in new[] { "kaun", "inum", "tulog" })
        {
            var stem = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, "bulk", Intent(new
            {
                form, formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
                gloss = form, analysisWs = ws, msa = new { kind = "stem", category },
            })));
            Assert.True(stem.Succeeded, stem.Refusal?.Message);
            msas.Add(Assert.Single(stem.Value!.Operations,
                item => item.Kind == "lexical/lexEntry/createMorphoSyntaxAnalyses").EntityId!);
        }

        var tagged = ProposalCommands.ComposeIntent(new(path, Version, "bulk", "compose-add-exception-features",
            Intent(new { msas, exceptionFeatures = new[] { featureId } })));

        Assert.True(tagged.Succeeded, tagged.Refusal?.Message);
        Assert.Equal(msas.Count, tagged.Value!.Operations.Count(item => item.Kind == MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict));
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == "compose-add-exception-features");
        Assert.False(descriptor.IsAgentTool);
    }

    [Fact]
    public void TaggingByQuerySeesStemsStagedEarlierInTheDraftAndReportsTheCountFirst()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "query", "Tag by query")).Succeeded);
        var feature = ProposalCommands.ComposeIntent(new(path, Version, "query", "compose-author-exception-feature",
            Intent(new { name = "Keeps n", ws })));
        var featureId = Assert.Single(feature.Value!.Operations,
            item => item.Kind == "lists/cmPossibilityList/createPossibilities").EntityId!;
        foreach (var form in new[] { "kaun", "inum", "tulog" })
            Assert.True(ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, "query", Intent(new
            {
                form, formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
                gloss = form, analysisWs = ws, msa = new { kind = "stem", category },
            }))).Succeeded);

        var tagged = ProposalCommands.ComposeIntent(new(path, Version, "query", "compose-tag-stems-by-query",
            Intent(new { query = new { finalLetters = "n" }, exceptionFeature = featureId, gate = "rule" })));

        Assert.True(tagged.Succeeded, tagged.Refusal?.Message);
        Assert.Equal(1, tagged.Value!.Operations.Count(item => item.Kind == MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict));
        Assert.StartsWith("1 of ", Assert.Single(tagged.Value.RelatedObjects!).Name);
    }

    [Fact]
    public void TwoBatchStepsAddingTheSameFeatureToTheSameStemStageOneAddition()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "twice", "Tag a stem twice")).Succeeded);
        object[] intents =
        [
            new { verb = "compose-author-exception-feature", @as = "loan", intent = new { name = "Loan stem", ws } },
            new
            {
                verb = "compose-author-lexical-entry", @as = "stem",
                intent = new
                {
                    form = "kaun", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
                    gloss = "eat", analysisWs = ws, msa = new { kind = "stem", category },
                },
            },
            new { verb = "compose-add-exception-features", intent = new { msas = new[] { "@stem:msa" }, exceptionFeatures = new[] { "@loan" } } },
            new { verb = "compose-add-exception-features", intent = new { msas = new[] { "@stem:msa" }, exceptionFeatures = new[] { "@loan" } } },
        ];

        var batch = ProposalCommands.ComposeBatch(new(path, Version, "twice", Intent(new { intents })));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Equal(1, batch.Value!.Intents.SelectMany(intent => intent.Operations)
            .Count(item => item.Kind == MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict));
    }
}
