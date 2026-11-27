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
}
