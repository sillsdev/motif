using SIL.Motif.Worker;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Serializes the tests that set runner variables, so no concurrently spawned child inherits them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RunnerEnvironmentCollection
{
    public const string Name = "Runner environment (serialized: its tests set process-wide variables)";
}

[Collection(RunnerEnvironmentCollection.Name)]
public sealed class RunnerOptionsTests : IDisposable
{
    private static readonly string[] RunnerVariables =
    [
        RunnerOptions.RootVariable, RunnerOptions.NamespaceVariable, RunnerOptions.IdleVariable,
        RunnerOptions.LeaseVariable,
    ];

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "motif-runner-options-" + Guid.NewGuid().ToString("N"));

    public RunnerOptionsTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExplicitArgumentsSelectTheRootParserNamespaceIdleAndLease()
    {
        var root = Path.Combine(_directory, "runner-root");
        var parser = ExistingParser();

        var options = WithConflictingEnvironment(() => RunnerOptions.Read(
        [
            RunnerOptions.RootArgument, root,
            RunnerOptions.ParserArgument, parser,
            RunnerOptions.NamespaceArgument, "isolated-namespace",
            RunnerOptions.IdleArgument, "1500",
            RunnerOptions.LeaseArgument, "2500",
        ]));

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
            [RunnerOptions.ParserArgument, Path.Combine(_directory, "absent", FakeParser.ExecutableFileName)]).ParserPath);

    [Fact]
    public void ResolveRootAndReadTakeTheRootVariable()
    {
        var environmentRoot = Path.Combine(_directory, "environment-root");

        var (resolved, read) = WithConflictingEnvironment(() => (RunnerOptions.ResolveRoot(), RunnerOptions.Read([])));

        Assert.Equal(environmentRoot, resolved);
        Assert.Equal(environmentRoot, read.Root);
        Assert.Equal("environment-namespace", read.OwnerNamespace);
        Assert.Equal(TimeSpan.FromSeconds(7), read.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(11), read.Lease);
    }

    [Fact]
    public void OptionsBuiltInCodeReadNothingFromTheEnvironment()
    {
        var options = WithConflictingEnvironment(() => new RunnerOptions { Root = _directory });

        Assert.Equal(_directory, options.Root);
        Assert.Null(options.ParserPath);
        Assert.Null(options.OwnerNamespace);
        Assert.Equal(TimeSpan.FromMinutes(5), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Lease);
    }

    private T WithConflictingEnvironment<T>(Func<T> read)
    {
        var previous = RunnerVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, Path.Combine(_directory, "environment-root"));
            Environment.SetEnvironmentVariable(RunnerOptions.NamespaceVariable, "environment-namespace");
            Environment.SetEnvironmentVariable(RunnerOptions.IdleVariable, "7");
            Environment.SetEnvironmentVariable(RunnerOptions.LeaseVariable, "11");
            return read();
        }
        finally
        {
            foreach (var (name, value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    private string ExistingParser()
    {
        var parser = Path.Combine(_directory, FakeParser.ExecutableFileName);
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
