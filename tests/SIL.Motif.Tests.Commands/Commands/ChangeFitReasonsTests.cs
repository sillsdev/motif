using SIL.Motif.Commands;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins that every reason the fit check writes for a change that no longer fits can be read back as a kind, so the
/// window words the reason by kind and a reworded reason cannot quietly fall back to the generic line.
/// </summary>
public sealed class ChangeFitReasonsTests
{
    public static TheoryData<string, ChangeFitReasonKind> EveryReason() => new()
    {
        { ChangeFitReasons.FingerprintMissing, ChangeFitReasonKind.CannotCheck },
        { ChangeFitReasons.FingerprintMalformed, ChangeFitReasonKind.CannotCheck },
        { ChangeFitReasons.FingerprintIncomplete, ChangeFitReasonKind.CannotCheck },
        { ChangeFitReasons.AnalysisIdentityInvalid, ChangeFitReasonKind.CannotCheck },
        { ChangeFitReasons.SpellingEvidenceMissing, ChangeFitReasonKind.CannotCheck },
        { ChangeFitReasons.MappingMissing, ChangeFitReasonKind.CannotCheck },
        { ChangeFitReasons.WordformDeleted("wordform/x"), ChangeFitReasonKind.WordformDeleted },
        { ChangeFitReasons.WordformChangedForm("wordform/x"), ChangeFitReasonKind.WordformChangedForm },
        { ChangeFitReasons.WordformSpellingChanged("wordform/x"), ChangeFitReasonKind.WordformSpellingChanged },
        { ChangeFitReasons.AnalysisMissing("analysis/x", "wordform/x"), ChangeFitReasonKind.AnalysisMissing },
        { ChangeFitReasons.AnalysisReadingChanged("analysis/x"), ChangeFitReasonKind.AnalysisReadingChanged },
        { ChangeFitReasons.AnalysisOpinionChanged("analysis/x"), ChangeFitReasonKind.AnalysisOpinionChanged },
        { ChangeFitReasons.MorphReferenceMissing("morph/x"), ChangeFitReasonKind.MorphReferenceMissing },
        { ChangeFitReasons.ReadingAlreadyExists("wordform/x"), ChangeFitReasonKind.ReadingAlreadyExists },
        { ChangeFitReasons.BaselineNotCurrent, ChangeFitReasonKind.BaselineNotCurrent },
    };

    [Theory]
    [MemberData(nameof(EveryReason))]
    public void EveryReasonReadsBackAsItsKind(string reason, ChangeFitReasonKind kind) =>
        Assert.Equal(kind, ChangeFitReasons.KindOf(reason));

    [Fact]
    public void EveryKindHasAReason() =>
        Assert.Equal(Enum.GetValues<ChangeFitReasonKind>().Where(kind => kind != ChangeFitReasonKind.Unrecognized)
            .Order(), EveryReason().Select(row => (ChangeFitReasonKind)row[1]).Distinct().Order());

    [Fact]
    public void AnUnknownReasonIsUnrecognized() =>
        Assert.Equal(ChangeFitReasonKind.Unrecognized, ChangeFitReasons.KindOf("Something else happened."));

    [Theory]
    [InlineData("ChangeFitPreflight.cs")]
    [InlineData("PendingChanges.cs")]
    public void TheFitCheckWritesNoLiteralReason(string file)
    {
        var source = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.Commands", file));

        Assert.DoesNotContain("NoFit(\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NoFit($\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("reasons.Add(\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Change mapping or fingerprint is missing.\"", source, StringComparison.Ordinal);
    }
}
