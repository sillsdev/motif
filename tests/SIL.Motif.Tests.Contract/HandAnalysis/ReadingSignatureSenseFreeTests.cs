using System.Collections.Immutable;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using Xunit;

namespace SIL.Motif.Tests.Contract.HandAnalysis;

public sealed class ReadingSignatureSenseFreeTests
{
    private static string Id(int n) => CanonicalId.FromGuid(new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1)).Value;

    private static ReadingMorpheme Morph(int entry, bool stem, int? sense, params int[] candidates) => new(
        Id(entry), sense is { } s ? Id(s) : null, [.. candidates.Select(Id)],
        sense is not null ? ReadingSenseStatus.Resolved : candidates.Length > 1 ? ReadingSenseStatus.Ambiguous : ReadingSenseStatus.Unresolved,
        Id(entry + 100), Id(entry + 200), null, stem, null, []);

    private static ReadingSignature Sig(params ReadingMorpheme[] morphemes) => new([.. morphemes]);

    [Fact]
    public void SenseFreeDropsTheSenseOnEveryMorphemeAndKeepsTheRest()
    {
        var resolved = Sig(Morph(1, true, 11), Morph(2, false, 21));
        var ambiguous = Sig(Morph(1, true, null, 11, 12), Morph(2, false, null, 21, 22));
        Assert.NotEqual(resolved, ambiguous);
        Assert.Equal(resolved.SenseFree(), ambiguous.SenseFree());
        Assert.All(resolved.SenseFree().Morphemes, m => Assert.Equal(ReadingSenseStatus.Unresolved, m.SenseStatus));
        Assert.Equal(resolved.SenseFree(), resolved.SenseFree().SenseFree());
        Assert.NotEqual(resolved.SenseFree(), Sig(Morph(1, true, 11), Morph(3, false, 21)).SenseFree());
    }

    [Fact]
    public void AnAmbiguousParserSenseThatIncludesTheApprovedSenseIsKept() =>
        Assert.True(ReadingSignature.ApprovalKept(Sig(Morph(1, true, 11)), Sig(Morph(1, true, null, 11, 12))));

    [Fact]
    public void AResolvedParserSenseEqualToTheApprovedSenseIsKept() =>
        Assert.True(ReadingSignature.ApprovalKept(Sig(Morph(1, true, 11)), Sig(Morph(1, true, 11))));

    [Fact]
    public void AParserThatResolvesADifferentSenseLosesTheApproval() =>
        Assert.False(ReadingSignature.ApprovalKept(Sig(Morph(1, true, 11)), Sig(Morph(1, true, 12))));

    [Fact]
    public void AnAmbiguousParserSenseThatExcludesTheApprovedSenseLosesTheApproval() =>
        Assert.False(ReadingSignature.ApprovalKept(Sig(Morph(1, true, 11)), Sig(Morph(1, true, null, 12, 13))));

    [Fact]
    public void ANonHeadMorphemeWithADifferentSenseFollowsTheSameRule()
    {
        var approved = Sig(Morph(1, true, 11), Morph(2, false, 21));
        Assert.True(ReadingSignature.ApprovalKept(approved, Sig(Morph(1, true, 11), Morph(2, false, null, 21, 22))));
        Assert.False(ReadingSignature.ApprovalKept(approved, Sig(Morph(1, true, 11), Morph(2, false, 22))));
    }

    [Fact]
    public void DifferentMorphemesAreLostAndAnApprovalWithoutASenseConstrainsNothing()
    {
        Assert.False(ReadingSignature.ApprovalKept(Sig(Morph(1, true, 11)), Sig(Morph(9, true, 11))));
        Assert.False(ReadingSignature.ApprovalKept(Sig(Morph(1, true, 11), Morph(2, false, 21)), Sig(Morph(1, true, 11))));
        Assert.True(ReadingSignature.ApprovalKept(Sig(Morph(1, true, null)), Sig(Morph(1, true, 12))));
    }
}
