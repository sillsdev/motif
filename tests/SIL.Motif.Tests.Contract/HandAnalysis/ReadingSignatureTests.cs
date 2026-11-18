using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Contract.HandAnalysis;

public sealed class ReadingSignatureTests
{
    private static readonly JsonSerializerOptions Json = MotifJson.CreateHandAnalysisReadOptions();

    [Theory]
    [InlineData(false, "ambiguous")]
    [InlineData(true, "explicit")]
    public void NativeAndParserFactsMatchThePinnedWireSignature(bool explicitSense, string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "HandAnalysis", "reading-signature-vectors.json")));
        var vector = Assert.Single(fixture.RootElement.EnumerateArray(), row => row.GetProperty("name").GetString() == name);
        var digest = vector.GetProperty("id").GetString();
        var signature = vector.GetProperty("signature");
        var native = new ReadingSignature(ReadingSignatureFixture.NativeFacts(explicitSense));
        var parser = new ReadingSignature(ReadingSignatureFixture.ParserFacts(explicitSense));

        Assert.Equal(digest, native.Id);
        Assert.Equal(digest, parser.Id);
        Assert.Equal(native, parser);
        Assert.Equal(native.GetHashCode(), parser.GetHashCode());
        var bytes = native.GetCanonicalBytes();
        Assert.Equal(bytes, parser.GetCanonicalBytes());
        Assert.Equal(CanonicalJson.CanonicalizeToUtf8(signature.GetRawText()), bytes);
        Assert.Equal(native, signature.Deserialize<ReadingSignature>(Json));
        Assert.Equal(CanonicalJson.CanonicalizeToUtf8(JsonSerializer.Serialize(native, Json)), bytes);
        using var wire = JsonDocument.Parse(bytes);
        Assert.Equal("morphemes", Assert.Single(wire.RootElement.EnumerateObject()).Name);
        bytes[0] = 0;
        Assert.Equal((byte)'{', native.GetCanonicalBytes()[0]);

        Assert.Equal(ReadingSignatureFixture.StemEntryId, native.EntryId);
        Assert.Equal(ReadingSignatureFixture.StemMsaId, native.MsaId);
        Assert.Equal(explicitSense ? ReadingSignatureFixture.StemSenseId : null, native.SenseId);
        Assert.Equal(explicitSense ? ReadingSenseStatus.Resolved : ReadingSenseStatus.Ambiguous, native.SenseStatus);
        Assert.Equal(explicitSense ? Array.Empty<string>() :
            new[] { ReadingSignatureFixture.StemSenseId, ReadingSignatureFixture.OtherStemSenseId }, native.SenseCandidates);
        Assert.Equal(2, native.FeatureValues.Length);
        Assert.All(native.FeatureValues, feature => Assert.Equal(ReadingFeatureRole.Stem, feature.Role));
        Assert.Equal(native, JsonSerializer.Deserialize<ReadingSignature>(native.GetCanonicalBytes(), Json));
    }

    [Fact]
    public void SummaryUsesTheOnlyStemOrTheOnlyMorpheme()
    {
        var facts = ReadingSignatureFixture.NativeFacts(true);
        var stem = facts[0];
        var affix = facts[1];
        Assert.Equal(stem.EntryId, new ReadingSignature([affix, stem]).EntryId);
        Assert.Equal(affix.EntryId, new ReadingSignature([affix]).EntryId);
        foreach (var noHead in new[] { new ReadingSignature([]),
                     new ReadingSignature([stem, affix with { IsStem = true }]),
                     new ReadingSignature([stem with { IsStem = false }, affix]) })
        {
            Assert.Null(noHead.EntryId);
            Assert.Null(noHead.SenseId);
            Assert.Null(noHead.MsaId);
            Assert.Equal(ReadingSenseStatus.Unresolved, noHead.SenseStatus);
            Assert.Empty(noHead.SenseCandidates);
            Assert.Empty(noHead.FeatureValues);
        }
    }

    [Fact]
    public void MorphemeOrderNestedPathAndDerivationArePartOfIdentity()
    {
        var facts = new ReadingSignature(ReadingSignatureFixture.NativeFacts(true)).Morphemes;
        var original = new ReadingSignature(facts);
        var affix = facts[1];
        var input = affix.FeatureValues[0];
        var output = affix.FeatureValues[1];
        ReadingSignature ChangeAffix(ReadingMorpheme changed) => new([facts[0], changed]);

        Assert.NotEqual(original, new ReadingSignature([.. facts.Reverse()]));
        Assert.NotEqual(original.Id, ChangeAffix(affix with
        {
            FeatureValues = [input with { FeaturePath = [ReadingSignatureFixture.Id(21), input.FeaturePath[1]] }, output]
        }).Id);
        Assert.NotEqual(original.Id, ChangeAffix(affix with
        {
            FeatureValues = [input with { ValueIds = output.ValueIds }, output with { ValueIds = input.ValueIds }]
        }).Id);
        Assert.NotEqual(original.Id, ChangeAffix(affix with
        {
            Derivation = new(affix.Derivation!.ToCategoryId, affix.Derivation.FromCategoryId)
        }).Id);
        Assert.NotEqual(original.Id, new ReadingSignature(ReadingSignatureFixture.NativeFacts(false)).Id);
    }

    [Fact]
    public void NullCollectionElementsAreRefusedBeforeIdentityCanBeUsed()
    {
        var stem = ReadingSignatureFixture.NativeFacts(false)[0];
        var feature = stem.FeatureValues[0];
        Assert.Throws<ArgumentException>(() => new ReadingSignature([null!]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with { SenseCandidates = [null!] }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with { FeatureValues = [null!] }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with
        {
            FeatureValues = [feature with { FeaturePath = [null!] }]
        }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with
        {
            FeatureValues = [feature with { ValueIds = [null!] }]
        }]));
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("lex/AwAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AwAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("00000003-0000-0000-0000-000000000000")]
    public void NativeIdsRequireTheUniqueNonemptyGuidSpelling(string id)
    {
        var stem = ReadingSignatureFixture.NativeFacts(true)[0];
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with { EntryId = id }]));
    }

    [Fact]
    public void CanonicalSenseStatusAndFeaturePathsRequireCompleteShape()
    {
        var stem = ReadingSignatureFixture.NativeFacts(false)[0];
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with { SenseCandidates = [stem.SenseCandidates[0]] }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with { SenseId = stem.SenseCandidates[0] }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with { SenseStatus = ReadingSenseStatus.Resolved }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with { SenseStatus = ReadingSenseStatus.Unresolved }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with
        {
            FeatureValues = [stem.FeatureValues[0] with { FeaturePath = [] }]
        }]));
        Assert.Throws<ArgumentException>(() => new ReadingSignature([stem with
        {
            FeatureValues = [stem.FeatureValues[0] with { ValueIds = [] }]
        }]));
    }

    [Theory]
    [InlineData("\"entryId\":\"bogus\",\"senseStatus\":\"resolved\"")]
    [InlineData("\"senseId\":null")]
    [InlineData("\"senseCandidates\":[]")]
    [InlineData("\"senseStatus\":\"resolved\"")]
    [InlineData("\"msaId\":null")]
    [InlineData("\"featureValues\":[]")]
    [InlineData("\"id\":\"sha256:bogus\"")]
    public void WireRejectsCallerSuppliedSummaryAndDigest(string members)
    {
        var json = "{\"morphemes\":[]," + members + "}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReadingSignature>(json, Json));
    }

    [Theory]
    [InlineData("entryId")]
    [InlineData("senseCandidates")]
    [InlineData("featurePath")]
    [InlineData("valueIds")]
    [InlineData("fromCategoryId")]
    public void WireRejectsDifferentTrailingBitsForTheSameGuid(string member)
    {
        const string canonical = "AAAAAAAAAAAAAAAAAAAAAQ";
        const string alias = "AAAAAAAAAAAAAAAAAAAAAR";
        Assert.Equal(CanonicalId.Parse(canonical).ToGuid(), CanonicalId.Parse(alias).ToGuid());
        Assert.Equal(canonical, CanonicalId.FromGuid(CanonicalId.Parse(alias).ToGuid()).Value);
        var wire = JsonNode.Parse(new ReadingSignature(ReadingSignatureFixture.NativeFacts(false)).GetCanonicalBytes())!;
        var stem = wire["morphemes"]![0]!;
        var feature = stem["featureValues"]![0]!;
        switch (member)
        {
            case "entryId": stem[member] = alias; break;
            case "senseCandidates": stem[member]![0] = alias; break;
            case "fromCategoryId": wire["morphemes"]![1]!["derivation"]![member] = alias; break;
            default: feature[member]![0] = alias; break;
        }

        Assert.Throws<JsonException>(() => wire.Deserialize<ReadingSignature>(Json));
    }

    [Fact]
    public void WireRejectsAnEmptyNestedFeaturePath()
    {
        var wire = JsonNode.Parse(new ReadingSignature(ReadingSignatureFixture.NativeFacts(false)).GetCanonicalBytes())!;
        wire["morphemes"]![0]!["featureValues"]![0]!["featurePath"] = new JsonArray();
        Assert.Throws<JsonException>(() => wire.Deserialize<ReadingSignature>(Json));
    }

    [Theory]
    [InlineData("senseCandidates")]
    [InlineData("featureValues")]
    [InlineData("featurePath")]
    [InlineData("valueIds")]
    public void WireCannotHydrateNullReadingMembers(string collection)
    {
        var signature = new ReadingSignature(ReadingSignatureFixture.NativeFacts(false));
        var wire = JsonNode.Parse(signature.GetCanonicalBytes())!;
        var morpheme = wire["morphemes"]![0]!;
        var feature = morpheme["featureValues"]![0]!;
        var owner = collection switch
        {
            "featurePath" or "valueIds" => feature,
            _ => morpheme
        };
        owner[collection]!.AsArray()[0] = null;
        Assert.Throws<JsonException>(() => wire.Deserialize<ReadingSignature>(Json));
    }

    [Theory]
    [InlineData("{\"morphemes\":[null]}")]
    [InlineData("{\"morphemes\":null}")]
    [InlineData("{\"morphemes\":[],\"morphemes\":[]}")]
    [InlineData("{}")]
    public void WireRequiresExactlyOneMorphemeArray(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReadingSignature>(json, Json));
}
