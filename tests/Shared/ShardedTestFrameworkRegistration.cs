using Xunit;

// Runs this assembly under the framework that lets the test gate split it across processes.
[assembly: TestFramework("SIL.Motif.Tests.TestFixtures.ShardedTestFramework", "SIL.Motif.Tests.Support")]
