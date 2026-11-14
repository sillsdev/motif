using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Contract.Review;

public sealed class GrammarSizeDeltaCounterTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly JsonElement Process = JsonDocument.Parse("{\"class\":\"MoAffixProcess\"}").RootElement.Clone();

    [Fact]
    public void ANewEntryCountsOnceAndItsOwnPartsAreNotCountedAgain()
    {
        var entry = Id();
        var delta = GrammarSizeDeltaCounter.Count([
            Create("lexical/lexDb/createEntries", entry), Child("lexical/lexEntry/createSenses", entry),
            Child("lexical/lexEntry/createMorphoSyntaxAnalyses", entry), Child("lexical/lexEntry/createLexemeForm", entry)]);
        Assert.Equal(1, delta.Entries);
        Assert.Equal(0, delta.Senses);
        Assert.Equal(0, delta.Msas);
        Assert.Equal(0, delta.Allomorphs);
        Assert.Equal(1, delta.ObjectsAdded);
    }

    [Fact]
    public void AVariantEntryCountsAsAVariantNotAnEntry()
    {
        var entry = Id();
        var delta = GrammarSizeDeltaCounter.Count([
            Create("lexical/lexDb/createEntries", entry), Child("lexical/lexEntry/createLexemeForm", entry),
            Child("lexical/lexEntry/createVariantRef", entry)]);
        Assert.Equal(0, delta.Entries);
        Assert.Equal(1, delta.Variants);
        Assert.Equal(1, delta.ObjectsAdded);
    }

    [Fact]
    public void AVariantEntryWithSeveralRefsCountsAsOneVariant()
    {
        var entry = Id();
        var delta = GrammarSizeDeltaCounter.Count([
            Create("lexical/lexDb/createEntries", entry), Child("lexical/lexEntry/createVariantRef", entry),
            Child("lexical/lexEntry/createVariantRef", entry)]);
        Assert.Equal(1, delta.Variants);
        Assert.Equal(1, delta.ObjectsAdded);
    }

    [Fact]
    public void HostsOfFeaturesThatAlreadyExistAreNotCounted()
    {
        var delta = GrammarSizeDeltaCounter.Count([Host("grammar/moStemMsa/addRefProdRestrict", Id(), Id())]);
        Assert.Equal(0, delta.ExceptionFeatureHosts);
    }

    [Fact]
    public void PartsAddedToAnExistingEntryEachCountAsObjects()
    {
        var existing = Id();
        var delta = GrammarSizeDeltaCounter.Count([
            Child("lexical/lexEntry/createMorphoSyntaxAnalyses", existing), Child("lexical/lexEntry/createAlternateForms", existing),
            Child("lexical/lexEntry/createAlternateForms", existing, Process)]);
        Assert.Equal(1, delta.Msas);
        Assert.Equal(1, delta.Allomorphs);
        Assert.Equal(1, delta.AffixProcesses);
        Assert.Equal(3, delta.ObjectsAdded);
    }

    [Fact]
    public void RulesEnvironmentsAndExceptionFeaturesWithTheirHostsAreCountedByKind()
    {
        var feature = Id();
        var host = Id();
        var delta = GrammarSizeDeltaCounter.Count([
            Create("grammar/phPhonData/createPhonRules", Id()), Create("grammar/phPhonData/createEnvironments", Id()),
            Create("lists/cmPossibilityList/createPossibilities", feature),
            Create("lists/cmPossibilityList/createPossibilities", Id()),
            Host("grammar/moStemMsa/addRefProdRestrict", host, feature),
            Host("grammar/moInflAffMsa/addRefFromProdRestrict", Id(), feature)]);
        Assert.Equal(1, delta.PhonologicalRules);
        Assert.Equal(1, delta.Environments);
        Assert.Equal(1, delta.ExceptionFeatures);
        Assert.Equal(2, delta.ExceptionFeatureHosts);
        Assert.Equal(1, delta.OtherObjects);
        Assert.Equal(4, delta.ObjectsAdded);
    }

    [Fact]
    public void OperationsThatAddNoObjectAreIgnored()
    {
        var delta = GrammarSizeDeltaCounter.Count([Create("lexical/lexSense/setGloss", Id()), Create("lexical/lexEntry/removeSenses", Id())]);
        Assert.Equal(0, delta.ObjectsAdded);
        Assert.Equal(GrammarSizeDelta.None, delta);
    }

    [Fact]
    public void CountingCebScaleOperationsTakesUnderOneSecond()
    {
        var operations = new List<OperationEnvelope>();
        var features = Enumerable.Range(0, 200).Select(_ => Id()).ToArray();
        foreach (var feature in features) operations.Add(Create("lists/cmPossibilityList/createPossibilities", feature));
        for (var i = 0; i < 20_000; i++)
        {
            var entry = Id();
            operations.Add(Create("lexical/lexDb/createEntries", entry));
            operations.Add(Child("lexical/lexEntry/createSenses", entry));
            operations.Add(Child("lexical/lexEntry/createLexemeForm", entry));
            operations.Add(Host("grammar/moStemMsa/addRefProdRestrict", Id(), features[i % features.Length]));
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var delta = GrammarSizeDeltaCounter.Count(operations);
        watch.Stop();
        Assert.Equal(20_000, delta.Entries);
        Assert.Equal(20_000, delta.ExceptionFeatureHosts);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms for {operations.Count} operations");
    }

    private static CanonicalId Id() => CanonicalId.Mint("x/");
    private static OperationEnvelope Create(string kind, CanonicalId entity) =>
        new(Id(), kind, entityId: entity, after: Empty);
    private static OperationEnvelope Child(string kind, CanonicalId owner, JsonElement? after = null) =>
        new(Id(), kind, entityId: Id(), target: owner, after: after ?? Empty);
    private static OperationEnvelope Host(string kind, CanonicalId msa, CanonicalId feature) =>
        new(Id(), kind, target: msa, after: JsonDocument.Parse("{\"ref\":\"" + feature.Value + "\"}").RootElement.Clone());
}
