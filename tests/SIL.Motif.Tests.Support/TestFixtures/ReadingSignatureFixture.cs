using System.Collections.Immutable;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Equivalent authored Reading facts for native bundle and parser-path adapters.</summary>
public static class ReadingSignatureFixture
{
    public static string StemEntryId => Id(1);
    public static string StemSenseId => Id(2);
    public static string OtherStemSenseId => Id(3);
    public static string StemMsaId => Id(4);

    public static ImmutableArray<ReadingMorpheme> NativeFacts(bool explicitSense) =>
    [
        Stem(explicitSense, [StemSenseId, OtherStemSenseId],
            [new(ReadingFeatureRole.Stem, [Id(7), Id(8)], [Id(9)]),
             new(ReadingFeatureRole.Stem, [Id(7), Id(8)], [Id(10)]),
             new(ReadingFeatureRole.Stem, [Id(20), Id(8)], [Id(9)])]),
        Affix([new(ReadingFeatureRole.DerivationInput, [Id(7), Id(8)], [Id(18)]),
               new(ReadingFeatureRole.DerivationOutput, [Id(7), Id(8)], [Id(19)])])
    ];

    public static ImmutableArray<ReadingMorpheme> ParserFacts(bool explicitSense) =>
    [
        Stem(explicitSense, [OtherStemSenseId, StemSenseId],
            [new(ReadingFeatureRole.Stem, [Id(20), Id(8)], [Id(9)]),
             new(ReadingFeatureRole.Stem, [Id(7), Id(8)], [Id(10), Id(9)])]),
        Affix([new(ReadingFeatureRole.DerivationOutput, [Id(7), Id(8)], [Id(19)]),
               new(ReadingFeatureRole.DerivationInput, [Id(7), Id(8)], [Id(18)])])
    ];

    private static ReadingMorpheme Stem(bool explicitSense, ImmutableArray<string> candidates,
        ImmutableArray<ReadingFeatureValue> features) =>
        new(StemEntryId, explicitSense ? StemSenseId : null, explicitSense ? [] : candidates,
            explicitSense ? ReadingSenseStatus.Resolved : ReadingSenseStatus.Ambiguous,
            StemMsaId, Id(5), null, true, null, features);

    private static ReadingMorpheme Affix(ImmutableArray<ReadingFeatureValue> features) =>
        new(Id(11), Id(12), [], ReadingSenseStatus.Resolved, Id(13), Id(14), Id(15), false,
            new(Id(16), Id(17)), features);

    public static string Id(int part) => CanonicalId.FromGuid(new(part, 0, 0, new byte[8])).Value;
}
