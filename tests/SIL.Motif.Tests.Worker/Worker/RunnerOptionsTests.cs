using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class RunnerOptionsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "motif-runner-options-" + Guid.NewGuid().ToString("N"));

    public RunnerOptionsTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExplicitArgumentsSelectTheRootParserNamespaceIdleAndLease()
    {
        var root = Path.Combine(_directory, "worker-root");
        var parser = ExistingParser();

        var options = RunnerOptions.Read(
        [
            RunnerOptions.RootArgument, root,
            RunnerOptions.ParserArgument, parser,
            RunnerOptions.NamespaceArgument, "isolated-namespace",
            RunnerOptions.IdleArgument, "1500",
            RunnerOptions.LeaseArgument, "2500",
        ]);

        Assert.Equal(root, options.Root);
        Assert.Equal(parser, options.ParserPath);
        Assert.Equal("isolated-namespace", options.OwnerNamespace);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), options.Lease);
    }

    [Fact]
    public void NoParserArgumentSelectsNoParser() =>
        Assert.Null(RunnerOptions.Read([RunnerOptions.NoParserArgument]).ParserPath);

    [Fact]
    public void ABlankParserArgumentSelectsNoParserRatherThanTheEnvironmentDefault() =>
        Assert.Null(RunnerOptions.Read([RunnerOptions.ParserArgument, ""]).ParserPath);

    [Fact]
    public void AParserArgumentNamingAMissingFileSelectsNoParser() =>
        Assert.Null(RunnerOptions.Read(
            [RunnerOptions.ParserArgument, Path.Combine(_directory, "absent", "pangloss.exe")]).ParserPath);

    [Fact]
    public void ResolveRootAndReadTakeTheRootVariable()
    {
        var previous = Environment.GetEnvironmentVariable(RunnerOptions.RootVariable);
        try
        {
            Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, _directory);

            Assert.Equal(_directory, RunnerOptions.ResolveRoot());
            Assert.Equal(_directory, RunnerOptions.Read([]).Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, previous);
        }
    }

    [Fact]
    public void OptionsBuiltInCodeReadNothingFromTheEnvironment()
    {
        var options = new RunnerOptions { Root = _directory };

        Assert.Equal(_directory, options.Root);
        Assert.Null(options.ParserPath);
        Assert.Null(options.OwnerNamespace);
    }

    private string ExistingParser()
    {
        var parser = Path.Combine(_directory, "pangloss.exe");
        File.WriteAllText(parser, "");
        return parser;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
