using System.Text.Json;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class MatrixCaptureCheckpointTests
{
    [Fact]
    public void CheckpointReplacementPreservesAnOpenSnapshotAndFinalAssertions()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-matrix-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = new MatrixRun(root);
            var size = new MatrixSize("thin", 1, 1);
            var selection = new MatrixCaptureSelection(new HashSet<string>(StringComparer.Ordinal) { "scene" },
                [new("thin", 100, "light"), new("thin", 150, "light")]);
            var finding = new LayoutFinding("scene", "thin", 100, "light", "sample",
                new ControlDescription("Button", "sample", null, null, string.Empty),
                new LayoutBounds(0.125, 0, 1, 1), "sample", "Unexpected checkpoint finding");
            run.AddCapture("scene-thin-100-light.png", "scene", size, 100, "light", [finding]);
            run.WriteArtifacts();
            Assert.ThrowsAny<Exception>(() => run.AssertComplete(selection));

            var csvPath = Path.Combine(run.MatrixFolder, "index.csv");
            var jsonPath = Path.Combine(run.MatrixFolder, "findings.json");
            using var csvSnapshot = new FileStream(csvPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var jsonSnapshot = new FileStream(jsonPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            run.AddCapture("scene-thin-150-light.png", "scene", size, 150, "light", []);
            run.WriteArtifacts();

            using var csvReader = new StreamReader(csvSnapshot);
            Assert.DoesNotContain("scene-thin-150-light.png", csvReader.ReadToEnd());
            Assert.Contains("scene-thin-150-light.png", File.ReadAllText(csvPath));
            using var previous = JsonDocument.Parse(jsonSnapshot);
            using var current = JsonDocument.Parse(File.ReadAllText(jsonPath));
            Assert.Equal(previous.RootElement.GetRawText(), current.RootElement.GetRawText());
            var savedFinding = Assert.Single(current.RootElement.EnumerateArray());
            Assert.False(savedFinding.GetProperty("expected").GetBoolean());
            Assert.Equal(0.125, savedFinding.GetProperty("bounds").GetProperty("x").GetDouble());
            Assert.Equal(1, savedFinding.GetProperty("count").GetInt32());
            run.AssertComplete(selection);
            Assert.ThrowsAny<Exception>(() => ExpectedMatrixFindings.AssertNoUnexpected(run.Findings,
                requireAllExpected: true, selection));
            Assert.Empty(Directory.GetFiles(run.MatrixFolder, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
