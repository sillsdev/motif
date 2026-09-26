using System;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins the one rule the CLI's stored read and the window both apply to decide whether numbers are still current:
/// stale when FieldWorks saved after the save the numbers were measured against, otherwise current, and no
/// Baseline before one is captured.
/// </summary>
public sealed class EvidenceFreshnessRuleTests
{
    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    public static TheoryData<DateTimeOffset?, DateTimeOffset?, DateTimeOffset?, EvidenceFreshness> Cases() => new()
    {
        { null, null, null, EvidenceFreshness.NoBaseline },
        { Saved, Saved, Saved, EvidenceFreshness.Current },
        { Saved, Saved, Saved.AddHours(1), EvidenceFreshness.Stale },
        // Numbers measured against an older save stay stale under a newer Baseline.
        { Saved.AddHours(1), Saved, Saved.AddHours(1), EvidenceFreshness.Stale },
        { Saved, Saved, Saved.AddHours(-1), EvidenceFreshness.Current },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void NumbersAreStaleOnlyWhenFieldWorksSavedAfterTheyWereMeasured(
        DateTimeOffset? baselineSave, DateTimeOffset? measuredSave, DateTimeOffset? latestSave,
        EvidenceFreshness expected) =>
        Assert.Equal(expected, EvidenceFreshnessRule.Of(baselineSave, measuredSave, latestSave));

    [Fact]
    public void TheLatestSaveIsTheLaterOfTheProjectFileAndTheBaseline()
    {
        Assert.Equal(Saved.AddHours(1), EvidenceFreshnessRule.LatestSave(Saved.AddHours(1), Saved));
        Assert.Equal(Saved.AddHours(1), EvidenceFreshnessRule.LatestSave(Saved, Saved.AddHours(1)));
        Assert.Equal(Saved, EvidenceFreshnessRule.LatestSave(null, Saved));
        Assert.Null(EvidenceFreshnessRule.LatestSave(null, null));
    }
}
