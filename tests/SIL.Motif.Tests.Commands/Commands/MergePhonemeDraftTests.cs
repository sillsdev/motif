using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class MergePhonemeDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static string Intent(object value) => JsonSerializer.Serialize(value);

    private static string StagePhoneme(string path, string draft, string name, params string[] representations)
    {
        var staged = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, draft,
            Intent(new { name, representations })));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        return Assert.Single(staged.Value!.Operations, o => o.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;
    }

    [Fact]
    public void ComposeIntentStagesAMergeOfTwoProjectPhonemesAndNamesTheClassesTheSourceLeaves()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Phonemes(cache, out var target, out var source);
        AddClass(cache, "Vowels", source, target);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "merge", "Merge o into u")).Succeeded);

        var merged = ProposalCommands.ComposeIntent(new(path, Version, "merge", "compose-merge-phoneme",
            Intent(new { source = CanonicalId.FromGuid(source).Value, target = CanonicalId.FromGuid(target).Value })));

        Assert.True(merged.Succeeded, merged.Refusal?.Message);
        Assert.Equal(["grammar/phNCSegments/removeRefSegments", "grammar/phPhonemeSet/deletePhonemes",
                "grammar/phTerminalUnit/createCodes", "grammar/phCode/setRepresentation"],
            merged.Value!.Operations.Select(item => item.Kind));
        Assert.Contains(merged.Value.RelatedObjects!, item => item.Kind == "natural-class"
            && item.Name.Contains("Vowels", StringComparison.Ordinal)
            && item.Name.Contains("dropped from this class", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposeIntentNamesEachRuleTheMergedPhonemeNewlyMeetsAndWhereItMatches()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Phonemes(cache, out var target, out var source);
        var rounded = AddNamedClass(cache, "Rounded", source, PhonemeOf(cache, "m"));
        var back = AddNamedClass(cache, "Back", target, PhonemeOf(cache, "t"));
        var both = AddNamedClass(cache, "Both", source, target);
        var newInput = AddRule(cache, "Input gains", input: back);
        var newLeft = AddRule(cache, "Left gains", left: target);
        var oldInput = AddRule(cache, "Input kept", input: rounded);
        var shared = AddRule(cache, "Shared", input: both, right: both);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "reach", "Merge o into u")).Succeeded);

        var merged = ProposalCommands.ComposeIntent(new(path, Version, "reach", "compose-merge-phoneme",
            Intent(new { source = CanonicalId.FromGuid(source).Value, target = CanonicalId.FromGuid(target).Value })));

        Assert.True(merged.Succeeded, merged.Refusal?.Message);
        var met = merged.Value!.NewlyMetRules!;
        Assert.Equal(2, met.Count);
        var input = Assert.Single(met, rule => rule.RuleId == CanonicalId.FromGuid(newInput).Value);
        Assert.Contains("Input gains", input.RuleName, StringComparison.Ordinal);
        Assert.Contains(input.Where, place => place.StartsWith("input", StringComparison.Ordinal)
            && place.Contains("Back", StringComparison.Ordinal));
        var left = Assert.Single(met, rule => rule.RuleId == CanonicalId.FromGuid(newLeft).Value);
        Assert.Contains(left.Where, place => place.StartsWith("left environment", StringComparison.Ordinal));
        Assert.DoesNotContain(merged.Value.RelatedObjects ?? [], item => item.Kind == "newly-met-rule");
        Assert.DoesNotContain(met, rule => rule.RuleId == CanonicalId.FromGuid(oldInput).Value
            || rule.RuleId == CanonicalId.FromGuid(shared).Value);
    }

    [Fact]
    public void ComposeIntentSplitsRelatedObjectsFromNewlyMetAndUncheckedRules()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Phonemes(cache, out var target, out var source);
        var rounded = AddNamedClass(cache, "Rounded", source, PhonemeOf(cache, "m"));
        var back = AddNamedClass(cache, "Back", target, PhonemeOf(cache, "t"));
        var met = AddRule(cache, "Input gains", input: back);
        var featured = AddFeatureRule(cache, "By features");
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "split", "Merge o into u")).Succeeded);

        var merged = ProposalCommands.ComposeIntent(new(path, Version, "split", "compose-merge-phoneme",
            Intent(new { source = CanonicalId.FromGuid(source).Value, target = CanonicalId.FromGuid(target).Value })));

        Assert.True(merged.Succeeded, merged.Refusal?.Message);
        var related = merged.Value!.RelatedObjects!;
        Assert.Contains(related, item => item.Kind == "natural-class" && item.Id == CanonicalId.FromGuid(rounded).Value);
        Assert.DoesNotContain(related, item => item.Kind is "newly-met-rule" or "unchecked-rule");
        Assert.Equal(CanonicalId.FromGuid(met).Value, Assert.Single(merged.Value.NewlyMetRules!).RuleId);
        var unchecked_ = Assert.Single(merged.Value.UncheckedRules!);
        Assert.Equal(CanonicalId.FromGuid(featured).Value, unchecked_.RuleId);
        Assert.Contains("By features", unchecked_.RuleName, StringComparison.Ordinal);
        Assert.Contains("feature-based", unchecked_.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMergeThatMeetsNoNewRuleReportsNone()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Phonemes(cache, out var target, out var source);
        AddClass(cache, "Vowels", source, target);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "none", "Merge")).Succeeded);

        var merged = ProposalCommands.ComposeIntent(new(path, Version, "none", "compose-merge-phoneme",
            Intent(new { source = CanonicalId.FromGuid(source).Value, target = CanonicalId.FromGuid(target).Value })));

        Assert.True(merged.Succeeded, merged.Refusal?.Message);
        Assert.Null(merged.Value!.NewlyMetRules);
        Assert.Null(merged.Value.UncheckedRules);
    }

    [Fact]
    public void AMergeOfAPhonemeStagedInTheSameDraftIsRefusedAsConflictingEdits()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "staged", "Merge")).Succeeded);
        var u = StagePhoneme(path, "staged", "u", "u");
        var o = StagePhoneme(path, "staged", "o", "o");

        var refused = ProposalCommands.ComposeIntent(new(path, Version, "staged", "compose-merge-phoneme",
            Intent(new { source = o, target = u })));

        Assert.False(refused.Succeeded);
        Assert.Contains("address the same slot", refused.Refusal!.Message);
    }

    [Fact]
    public void ARefusedMergeStagesNothingAndNamesTheRefusal()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "same", "Merge")).Succeeded);
        var u = StagePhoneme(path, "same", "u", "u");

        var refused = ProposalCommands.ComposeIntent(new(path, Version, "same", "compose-merge-phoneme",
            Intent(new { source = u, target = u })));

        Assert.False(refused.Succeeded);
        Assert.Contains("phoneme-merge-same", refused.Refusal!.Message);
    }

    private static void AddClass(LcmCache cache, string name, params Guid[] members) =>
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var naturalClass = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
            naturalClass.Name.set_String(cache.DefaultAnalWs, name);
            naturalClass.Abbreviation.set_String(cache.DefaultAnalWs, "V");
            foreach (var member in members)
                naturalClass.SegmentsRC.Add(
                    (IPhPhoneme)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(member));
        });

    private static Guid PhonemeOf(LcmCache cache, string letter) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.Single(p => p.CodesOS.Any(c =>
            c.Representation.get_String(cache.DefaultVernWs)?.Text == letter)).Guid;

    private static Guid AddNamedClass(LcmCache cache, string name, params Guid[] members)
    {
        Guid id = default;
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var naturalClass = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
            naturalClass.Name.set_String(cache.DefaultAnalWs, name);
            naturalClass.Abbreviation.set_String(cache.DefaultAnalWs, name);
            foreach (var member in members)
                naturalClass.SegmentsRC.Add(
                    (IPhPhoneme)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(member));
            id = naturalClass.Guid;
        });
        return id;
    }

    private static Guid AddFeatureRule(LcmCache cache, string name)
    {
        Guid id = default;
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var naturalClass = cache.ServiceLocator.GetInstance<IPhNCFeaturesFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
            naturalClass.Name.set_String(cache.DefaultAnalWs, "Featured");
            var rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, name);
            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
            rule.StrucDescOS.Add(context);
            context.FeatureStructureRA = naturalClass;
            id = rule.Guid;
        });
        return id;
    }

    private static Guid AddRule(LcmCache cache, string name, Guid? input = null, Guid? left = null, Guid? right = null)
    {
        Guid id = default;
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            var rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, name);
            var rhs = cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create();
            rule.RightHandSidesOS.Add(rhs);
            if (input is { } inputClass)
            {
                var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
                rule.StrucDescOS.Add(context);
                context.FeatureStructureRA = (IPhNaturalClass)objects.GetObject(inputClass);
            }
            if (left is { } leftPhoneme)
            {
                var context = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
                rhs.LeftContextOA = context;
                context.FeatureStructureRA = (IPhPhoneme)objects.GetObject(leftPhoneme);
            }
            if (right is { } rightClass)
            {
                var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
                rhs.RightContextOA = context;
                context.FeatureStructureRA = (IPhNaturalClass)objects.GetObject(rightClass);
            }
            id = rule.Guid;
        });
        return id;
    }

    internal static void Phonemes(LcmCache cache, out Guid target, out Guid source)
    {
        RealParserProject.PrepareForParsing(cache, "m", "o", "u", "t", "i", "f", "b", "a");
        var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
        Guid Of(string letter) => set.PhonemesOC.Single(p => p.CodesOS.Any(c =>
            c.Representation.get_String(cache.DefaultVernWs)?.Text == letter)).Guid;
        target = Of("u");
        source = Of("o");
    }
}
