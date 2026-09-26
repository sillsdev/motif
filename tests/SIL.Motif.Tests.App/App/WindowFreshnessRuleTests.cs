using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the window decides whether its numbers are current by the same rule the command line's stored read
/// applies, fed with the Assessment the window shows; the window adds only what it alone knows, that changes were
/// applied since the numbers were measured.
/// </summary>
public sealed class WindowFreshnessRuleTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    public static TheoryData<int, int, int> Saves() => new()
    {
        // Hours after Saved: the Baseline's save, the save the numbers were measured against, the project file.
        { 0, 0, 0 },
        { 0, 0, 1 },
        { 1, 0, 1 },
        { 1, 1, 0 },
    };

    [Theory]
    [MemberData(nameof(Saves))]
    public async Task TheWindowAndTheStoredReadApplyOneRule(int baselineHours, int measuredHours, int fileHours)
    {
        var context = NewContext();
        await context.OpenProjectAsync(ProjectPath);
        var (baseline, measured, file) =
            (Saved.AddHours(baselineHours), Saved.AddHours(measuredHours), Saved.AddHours(fileHours));
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null)
        {
            SourceLastWriteUtc = baseline,
            ProjectLastWriteUtc = file,
        };
        context.PublishEvidence(new WorkspaceEvidence(Run(measured), measured, WasRerun: false));

        var rule = EvidenceFreshnessRule.Of(baseline, measured, EvidenceFreshnessRule.LatestSave(file, baseline));
        Assert.Equal(rule == EvidenceFreshness.Stale, context.Evidence.IsStale);
        Assert.Equal(rule == EvidenceFreshness.Stale ? NumbersFreshness.SavedSince : NumbersFreshness.Current,
            context.Evidence.Freshness);

        context.RecordApplied();
        Assert.Equal(NumbersFreshness.AppliedSince, context.Evidence.Freshness);
    }

    private static AssessCommandResponse Run(DateTimeOffset measured) => new(
        new BaselineCaptureResponse(Token, ProjectPath, measured, false, true), new SelectionProjection([], []), [],
        "summary");

    private static WorkspaceContext NewContext()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake), fake,
            new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(fake));
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(Avalonia.Input.PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, Avalonia.Input.DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
