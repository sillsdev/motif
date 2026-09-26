using Avalonia.Automation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what a screen reader hears for a verdict chip that shows only its glyph: the legend's words, which for
/// something the project does not hold are the glossary's "not present".
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class VerdictChipNameTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public VerdictChipNameTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void TheLegendCallsWhatTheProjectLacksNotPresent() =>
        Assert.Equal("not present", Verdicts.LegendOf(Verdict.New));

    [Fact]
    public void AChipWithoutWordsIsNamedByItsLegend() =>
        _avalonia.Invoke(() =>
            Assert.Equal("not present", AutomationProperties.GetName(new VerdictChip { Verdict = Verdict.New })));
}
