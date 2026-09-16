using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughStoreAssertions
{
    internal static IReadOnlyList<RetainedInvocationRecord> ListInvocations(string fwDataPath)
    {
        var locator = Locator(fwDataPath);
        using var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        return new RetainedInvocationRepository(database).List(ProjectWorkspaceKey.Compute(locator));
    }

    internal static void AssertSingleInvocationForBaseline(string fwDataPath, BaselineToken token)
    {
        var invocation = Assert.Single(ListInvocations(fwDataPath));
        Assert.Equal(token, invocation.BaselineToken);
    }

    private static ProjectLocator Locator(string fwDataPath) => new(
        Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
}
