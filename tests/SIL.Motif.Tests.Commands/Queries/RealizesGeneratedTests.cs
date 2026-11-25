using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Commands.RecordedMorphemes;

namespace SIL.Motif.Tests.Commands;

/// <summary>The generated tier over generate and match replies recorded from PanGloss v0.33.0.</summary>
public sealed class RealizesGeneratedTests
{
    [Fact]
    public void TheMorphemesPieceIsReadFromMorphSpansAndTheFiredRulesAreKept()
    {
        var read = RealizesGenerated.Read(RecordedRealizes.DaWithTa, TaEntry, TaMsa);

        var piece = Assert.Single(read.Pieces);
        Assert.Equal("morphSpans", piece.PieceSource);
        Assert.NotEqual(piece.Surface, piece.Piece);
        Assert.StartsWith(piece.Piece, piece.Surface);
        Assert.True(read.Complete);
    }

    [Fact]
    public void AStemGeneratedAloneGivesItsWholeSurfaceAndTheRuleThatChangedIt()
    {
        var read = RealizesGenerated.Read(RecordedRealizes.KatAlone, KatEntry, KatMsa);

        var piece = Assert.Single(read.Pieces);
        Assert.Equal(piece.Surface, piece.Piece);
        Assert.Equal("morphSpans", piece.PieceSource);
    }

    [Fact]
    public void AMorphemeThePlannerCannotFindARootForIsNamedAsUncovered()
    {
        var grammar = new ReplayGrammar
        {
            OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.PrefixTa, "ta+", "ta+"),
            OnMatch = (_, words) => RecordedRealizes.FillWords(RecordedRealizes.TadaVoicing, [.. words.Take(1)]),
            OnGenerate = _ => RecordedRealizes.DaWithTa,
        };
        var target = new RealizesTarget(TaEntry, TaMsa, null);
        var variants = RealizesGenerated.Collect(grammar, target, [], [], false, CancellationToken.None);

        Assert.Contains("no_lexicon", variants.Reasons.Select(reason => reason.Code));
        Assert.False(variants.Complete);
    }
}
