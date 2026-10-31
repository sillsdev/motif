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
