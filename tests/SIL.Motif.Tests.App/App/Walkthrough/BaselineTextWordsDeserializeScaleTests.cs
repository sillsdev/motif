using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Trait("MotifTestLevel", "System")]
public sealed class BaselineTextWordsDeserializeScaleTests(ITestOutputHelper output)
{
    [Fact]
    public void ThreeThousandWordSelectionReportsFullDeserializeCost()
    {
        Measure(representativeScale: false, 3000, 3000, 40, 9760);
    }

    [Fact]
    public void TwentyOneThousandSixHundredFourRowSelectionReportsFullDeserializeCost()
    {
        Measure(representativeScale: true, 22948, 21604, 129, 76761);
    }

    private void Measure(bool representativeScale, int wordformCount, int selectedWordCount, int textCount,
        int occurrenceCount)
    {
        using var project = new LargeProjectFixture(representativeScale);
        Assert.Equal(wordformCount, project.Words.Count);
        Assert.Equal(selectedWordCount, project.SelectedWordCount);
        Assert.Equal(textCount, project.TextIds.Count);
        Assert.Equal(occurrenceCount, project.TotalOccurrenceCount);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath),
            project.ManagedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);

        var first = ReadOnce(project.FwDataPath, project.TextIds);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var repeated = ReadOnce(project.FwDataPath, project.TextIds);

        Assert.Equal(textCount, first.TextCount);
        Assert.Equal(selectedWordCount, first.WordformCount);
        Assert.Equal(occurrenceCount, first.OccurrenceCount);
        Assert.Equal(first.TextCount, repeated.TextCount);
        Assert.Equal(first.WordformCount, repeated.WordformCount);
        Assert.Equal(first.OccurrenceCount, repeated.OccurrenceCount);
        output.WriteLine($"FULL DESERIALIZE | {selectedWordCount:N0} selected rows | " +
            $"first {first.ElapsedMilliseconds:F3} ms / {first.AllocatedBytes:N0} bytes " +
            $"({first.AllocatedBytes / 1048576d:F1} MiB) | " +
            $"repeat {repeated.ElapsedMilliseconds:F3} ms / {repeated.AllocatedBytes:N0} bytes " +
            $"({repeated.AllocatedBytes / 1048576d:F1} MiB) | " +
            $"{textCount:N0} Texts, {first.WordformCount:N0} wordforms, {occurrenceCount:N0} occurrences");
    }

    private static ReadMeasurement ReadOnce(string projectPath, IReadOnlyList<Guid> textIds)
    {
        var outcome = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var repository = new BaselineRepository(database);
            var projectKey = ProjectWorkspaceKey.Compute(project);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            var current = repository.GetCurrentTextWords(projectKey, textIds)
                ?? throw new InvalidOperationException("The captured Baseline was not found.");
            clock.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var occurrenceCount = 0;
            foreach (var text in current.Projection.Texts)
            foreach (var line in text.Lines)
            foreach (var token in line.Tokens)
                if (token.WordformId is not null) occurrenceCount++;
            return CommandOutcome<ReadMeasurement>.Success(new ReadMeasurement(clock.Elapsed.TotalMilliseconds,
                allocatedBytes, current.Projection.Texts.Count, current.Projection.Wordforms.Count,
                occurrenceCount));
        });
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private sealed record ReadMeasurement(double ElapsedMilliseconds, long AllocatedBytes, int TextCount,
        int WordformCount, int OccurrenceCount);
}
