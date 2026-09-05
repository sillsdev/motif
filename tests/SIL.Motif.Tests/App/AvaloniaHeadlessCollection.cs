using Avalonia;
using Avalonia.Headless;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Groups every test class that needs a running headless Avalonia application into one xUnit
/// collection, so the required process-wide Avalonia platform setup happens exactly once.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaHeadlessCollection : ICollectionFixture<AvaloniaHeadlessFixture>
{
    public const string Name = "Avalonia headless tests (serialized: platform setup runs once per process)";
}

public sealed class AvaloniaHeadlessFixture
{
    public AvaloniaHeadlessFixture() =>
        AppBuilder.Configure<SIL.Motif.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
}
