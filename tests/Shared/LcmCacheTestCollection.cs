using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Isolates tests that inspect process-wide writing-system state from the parallel cache collections.
/// </summary>
/// <remarks>
/// xUnit runs distinct test classes (each its own implicit collection) in parallel by default.
/// The loader serializes cache startup. This collection also prevents a process-wide repository snapshot
/// from racing with another class's cache disposal.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class LcmCacheTestCollection : ICollectionFixture<PristineProjectFixture>
{
    public const string Name = "LcmCache process-wide state tests";
}

/// <summary>Names four collections whose cache-using classes may run alongside each other.</summary>
public static class LcmCacheParallelCollections
{
    public const string Group0 = "LcmCache parallel 0";
    public const string Group1 = "LcmCache parallel 1";
    public const string Group2 = "LcmCache parallel 2";
    public const string Group3 = "LcmCache parallel 3";
}

[CollectionDefinition(LcmCacheParallelCollections.Group0)]
public sealed class LcmCacheParallelCollection0 : ICollectionFixture<PristineProjectFixture> { }

[CollectionDefinition(LcmCacheParallelCollections.Group1)]
public sealed class LcmCacheParallelCollection1 : ICollectionFixture<PristineProjectFixture> { }

[CollectionDefinition(LcmCacheParallelCollections.Group2)]
public sealed class LcmCacheParallelCollection2 : ICollectionFixture<PristineProjectFixture> { }

[CollectionDefinition(LcmCacheParallelCollections.Group3)]
public sealed class LcmCacheParallelCollection3 : ICollectionFixture<PristineProjectFixture> { }
