using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class QueryAsOfArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-query-asof-argv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<CliProcessResult> Run(params string[] options)
    {
        Directory.CreateDirectory(_root);
        return await CliProcess.RunAsync(_root, null, false, ["query", "forms", "--project", GroupCorpusFixture.Create(pristine), .. options]);
    }

    [Fact]
    public async Task AsOfBaselineIsRefusedWithItsCodeWhenTheProjectHasNoBaseline()
    {
        var result = await Run("--as-of", "baseline", "--sort", "form:desc", "--json");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("query.as-of-unavailable", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SidesAndSortWithoutAsOfAreRefusedAsInvalid()
    {
        var result = await Run("--sides", "both", "--json");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("query.invalid", result.Output + result.Error, StringComparison.Ordinal);
    }
}
