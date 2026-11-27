using SIL.Motif.Contract.Ids;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class ObjectIdTests
{
    private static readonly Guid Sample = Guid.Parse("3f2a8c10-5b7e-4d21-9a63-0c1e2f3a4b5c");

    [Fact]
    public void AGuidAndItsCanonicalSpellingParseToTheSameIdentity()
    {
        var canonical = CanonicalId.FromGuid(Sample);

        Assert.True(ObjectId.TryParse(Sample.ToString("D"), out var fromGuid));
        Assert.True(ObjectId.TryParse(canonical.Value, out var fromCanonical));
        Assert.Equal(canonical, fromGuid);
        Assert.Equal(canonical, fromCanonical);
        Assert.Equal(Sample.ToString("D"), ObjectId.ToGuidText(fromCanonical));
    }

    [Fact]
    public void AnUpperCaseGuidAndSurroundingWhitespaceAreAccepted()
    {
        Assert.True(ObjectId.TryParse("  " + Sample.ToString("D").ToUpperInvariant() + " ", out var id));
        Assert.Equal(Sample, id.ToGuid());
    }

    [Fact]
    public void APrefixedCanonicalIdKeepsItsPrefix()
    {
        var prefixed = CanonicalId.FromGuid(Sample, "op-");

        Assert.True(ObjectId.TryParse(prefixed.Value, out var id));
        Assert.Equal("op-", id.Prefix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-id")]
    [InlineData("short")]
    [InlineData("3f2a8c10-5b7e-4d21-9a63-0c1e2f3a4b5c!!")]
    public void TextThatIsNeitherSpellingIsRefusedWithAReason(string text)
    {
        Assert.False(ObjectId.TryParse(text, out _, out var error));
        Assert.Contains("neither a canonical id", error);
    }

    [Fact]
    public void NullIsRefused() => Assert.False(ObjectId.TryParse(null, out _));

    [Theory]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    public void EveryFormGuidParseReadsIsAccepted(string format)
    {
        Assert.True(ObjectId.TryParse(Sample.ToString(format), out var id));
        Assert.Equal(Sample, id.ToGuid());
    }

    [Fact]
    public void TheBracedHexFormIsAccepted()
    {
        const string braced = "{0x3f2a8c10,0x5b7e,0x4d21,{0x9a,0x63,0x0c,0x1e,0x2f,0x3a,0x4b,0x5c}}";

        Assert.True(ObjectId.TryParse(braced, out var id));
        Assert.Equal(Sample, id.ToGuid());
    }

    [Fact]
    public void AMixedCaseCanonicalIdKeepsItsCaseAndItsIdentity()
    {
        var canonical = CanonicalId.FromGuid(Sample).Value;
        var swapped = new string(canonical.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());

        // Base64url is case-sensitive: the swapped spelling is a different, still well-formed id, never the original.
        Assert.True(ObjectId.TryParse(canonical, out var original));
        Assert.True(ObjectId.TryParse(swapped, out var other));
        Assert.Equal(canonical, original.Value);
        Assert.Equal(swapped, other.Value);
        Assert.NotEqual(original.ToGuid(), other.ToGuid());
    }

    [Fact]
    public void AMalformedBracedStringOf38CharactersIsRefused()
    {
        var malformed = "{" + Sample.ToString("D").Replace('a', 'z') + "}";
        Assert.Equal(38, malformed.Length);

        Assert.False(ObjectId.TryParse(malformed, out _, out var error));
        Assert.Contains("neither a canonical id", error);
    }

    [Fact]
    public void TheRefusalCatalogCarriesTheIdConvertCodes()
    {
        Assert.True(SIL.Motif.Contract.Commands.RefusalCatalog.Specs.ContainsKey("id.invalid"));
        Assert.True(SIL.Motif.Contract.Commands.RefusalCatalog.Specs.ContainsKey("id-convert.invalid-request"));
    }
}
