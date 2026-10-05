using System.Diagnostics;
using System.Text;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

public sealed class PanGlossStreamEncodingTests
{
    [Fact]
    public void ParserDecodersExplicitlyUseUtf8RegardlessOfTheConsoleEncoding()
    {
        var start = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.ASCII,
            StandardErrorEncoding = Encoding.ASCII,
        };

        PanGlossProcessEnvironment.Configure(start);

        Assert.Equal(Encoding.UTF8.CodePage, start.StandardOutputEncoding!.CodePage);
        Assert.Equal(Encoding.UTF8.CodePage, start.StandardErrorEncoding!.CodePage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothParserStreamsPreserveNonLatinFormsFromALegacyConsole(bool legacyConsole)
    {
        const string text = "Allomorph 'ŋ(k)' — العربية 中文 e\u0301 𐐀";
        var root = Path.Combine(Path.GetTempPath(), "motif-stream-encoding-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executable = legacyConsole
                ? FakeParser.CopyWithSentinel(root, "_fake-pangloss-legacy-console")
                : FakeParser.Copy(root);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            PanGlossProcessEnvironment.Configure(start);
            start.ArgumentList.Add("--probe-stream-encoding");
            start.ArgumentList.Add(text);
            using var containment = PanGlossContainment.CreateJob();
            using var child = containment.Start(start);
            var stdout = child.ReadStandardOutputAsync();
            var stderr = child.ReadStandardErrorAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await child.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                containment.Terminate(child);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                throw;
            }

            var error = await stderr;
            Assert.True(child.ExitCode == 0, $"Encoding probe exited {child.ExitCode}: {error}");
            Assert.Equal(text, await stdout);
            Assert.Equal(text, error);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
