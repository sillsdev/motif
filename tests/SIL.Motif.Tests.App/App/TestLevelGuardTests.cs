using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Sdk;

namespace SIL.Motif.Tests.App.App;

[CollectionDefinition("Test level guard", DisableParallelization = true)]
public sealed class TestLevelGuardCollection
{
    public const string Name = "Test level guard";
}

[Collection(TestLevelGuardCollection.Name)]
public sealed class TestLevelGuardTests
{
    private static readonly HashSet<string> ValidLevels = ["Unit", "Integration", "System"];

    [Fact]
    public void EveryTestProjectDeclaresOneDefaultLevel()
    {
        var root = FindRepositoryRoot();
        var projects = Directory.GetFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => (Path: path, Document: XDocument.Load(path)))
            .Where(project => string.Equals(
                project.Document.Descendants().FirstOrDefault(element => element.Name.LocalName == "IsTestProject")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var levels = project.Document.Descendants()
                .Where(element => element.Name.LocalName == "MotifTestDefaultLevel")
                .Select(element => element.Value.Trim())
                .ToArray();

            Assert.True(levels.Length == 1 && ValidLevels.Contains(levels[0]),
                $"{Path.GetRelativePath(root, project.Path)} must declare exactly one valid MotifTestDefaultLevel.");
        }
    }

    [Fact]
    public void EveryTestClassResolvesToExactlyOneLevel()
    {
        var processEnvironment = Environment.GetEnvironmentVariables();
        try
        {
            var root = FindRepositoryRoot();
            var testOutput = AppContext.BaseDirectory;
            // Loading test assemblies executes module initializers, so preserve this host's isolated environment.
            var assemblies = Directory.GetFiles(testOutput, "SIL.Motif.Tests.*.dll")
                .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Support", StringComparison.Ordinal))
                .Select(Assembly.LoadFrom)
                .ToArray();

            Assert.NotEmpty(assemblies);
            foreach (var assembly in assemblies)
            {
                var defaultLevel = TestLevelClassifier.ReadAssemblyDefault(assembly);

                foreach (var testClass in assembly.GetTypes().Where(HasTests))
                {
                    var declarations = testClass.GetCustomAttributesData()
                        .Where(attribute => attribute.AttributeType == typeof(TraitAttribute))
                        .Select(attribute => attribute.ConstructorArguments)
                        .Where(arguments => arguments.Count == 2 && arguments[0].Value as string == "MotifTestLevel")
                        .Select(arguments => arguments[1].Value as string)
                        .ToArray();
                    Assert.True(declarations.Length <= 1,
                        $"{testClass.FullName} declares more than one MotifTestLevel.");

                    var parserClass = testClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                           BindingFlags.Instance | BindingFlags.Static)
                        .SelectMany(method => method.GetCustomAttributes(inherit: true))
                        .Any(attribute => attribute.GetType().Name == "RealParserFactAttribute");
                    var walkthroughNamespace = testClass.Namespace?.Split('.')
                        .Any(segment => segment is "Walkthrough" or "Smoke") == true;
                    var namedSystemClass = testClass.Name is "PortableWorkerPackageTests" or
                        "ExplainedWordCardWalkthroughReplayTests" or
                        "ExternalApplyActivationRealClientTests" or
                        "RunnerSpineTests";
                    var resolvedLevel = TestLevelClassifier.ResolveLevel(
                        testClass.FullName!, defaultLevel, declarations, parserClass);
                    foreach (var method in testClass.GetMethods().Where(method =>
                                 method.GetCustomAttributes(inherit: true).Any(attribute => attribute is FactAttribute)))
                    {
                        using var testCase = CaseFor(testClass, method);
                        foreach (var level in ValidLevels)
                            Assert.Equal(resolvedLevel == level, TestLevelClassifier.IsSelected(
                                testCase, defaultLevel, new HashSet<string> { level }));
                    }
                    if (parserClass || walkthroughNamespace || namedSystemClass || defaultLevel == "System")
                        Assert.Equal("System", resolvedLevel);
                }
            }
        }
        finally
        {
            RestoreProcessEnvironment(processEnvironment);
        }
    }

    [Fact]
    public void DefaultSelectionIncludesUnitAndIntegrationAndAllIncludesSystem()
    {
        var defaultLevels = TestLevelClassifier.ParseSelection("Unit,Integration")!;

        Assert.Contains("Unit", defaultLevels);
        Assert.Contains("Integration", defaultLevels);
        Assert.DoesNotContain("System", defaultLevels);
        Assert.Null(TestLevelClassifier.ParseSelection("All"));
    }

    [Fact]
    public void MixedParserCasesAreRejectedForEitherMethod()
    {
        foreach (var method in typeof(MixedParserFixture).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public |
                                                                   BindingFlags.Instance))
        {
            using var testCase = CaseFor(typeof(MixedParserFixture), method);
            var failure = Assert.Throws<InvalidOperationException>(() =>
                TestLevelClassifier.IsSelected(testCase, "Integration", new HashSet<string> { "Integration" }));
            Assert.Contains("mixes RealParserFact", failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnExplicitSystemClassSelectsEveryMethodTogether()
    {
        foreach (var method in typeof(MixedSystemFixture).GetMethods().Where(method =>
                     method.GetCustomAttributes(inherit: true).Any(attribute => attribute is FactAttribute)))
        {
            using var testCase = CaseFor(typeof(MixedSystemFixture), method);
            Assert.False(TestLevelClassifier.IsSelected(testCase, "Integration", new HashSet<string> { "Integration" }));
            Assert.True(TestLevelClassifier.IsSelected(testCase, "Integration", new HashSet<string> { "System" }));
            Assert.True(TestLevelClassifier.IsSelected(testCase, "Integration", null));
        }
    }

    [Fact]
    public void MethodLevelDeclarationsAreRejected()
    {
        using var testCase = CaseFor(typeof(MethodLevelFixture), typeof(MethodLevelFixture).GetMethod("Test")!);
        var failure = Assert.Throws<InvalidOperationException>(() =>
            TestLevelClassifier.IsSelected(testCase, "Unit", new HashSet<string> { "Unit" }));
        Assert.Contains("class", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(RealClient.OverviewRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.WarningsRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.TimingRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.TryWordRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.FieldWorksAnalysisDriftRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.WindowRefusalRealClientTests), "Integration")]
    [InlineData(typeof(ReviewUndoRealClientTests), "Integration")]
    [InlineData(typeof(InspectorIdentityBoundaryTests), "Integration")]
    [InlineData(typeof(CompareOverviewParityTests), "Integration")]
    [InlineData(typeof(WalkthroughHelpers.WalkthroughScriptLoaderTests), "Unit")]
    [InlineData(typeof(WalkthroughHelpers.WalkthroughArtifactTests), "Unit")]
    [InlineData(typeof(WalkthroughHelpers.FakeChatReceiverTests), "Unit")]
    [InlineData(typeof(WalkthroughHelpers.HeadlessClickTests), "Unit")]
    [InlineData(typeof(WalkthroughHelpers.WalkthroughViewportTests), "Unit")]
    public void CheapHelpersAndRealClientSeamsSelectTheirIntendedLevel(Type type, string level)
    {
        foreach (var method in type.GetMethods().Where(method =>
                     method.GetCustomAttributes(inherit: true).Any(attribute => attribute is FactAttribute)))
        {
            using var testCase = CaseFor(type, method);
            Assert.True(TestLevelClassifier.IsSelected(testCase, "Unit", new HashSet<string> { level }),
                $"{type.Name}.{method.Name} must select {level}.");
        }
    }

    private static XunitTestCase CaseFor(Type type, MethodInfo method)
    {
        var assembly = new TestAssembly(Reflector.Wrap(type.Assembly));
        var collection = new TestCollection(assembly, null, "level selection");
        var testClass = new TestClass(collection, Reflector.Wrap(type));
        return new XunitTestCase(new NullMessageSink(), TestMethodDisplay.ClassAndMethod, TestMethodDisplayOptions.None,
            new TestMethod(testClass, Reflector.Wrap(method)));
    }

    public abstract class MixedParserFixture
    {
        [Fact]
        public void Deterministic() { }

        [SIL.Motif.Tests.Parser.RealParserFact]
        public void Parser() { }
    }

    [Trait("MotifTestLevel", "System")]
    public abstract class MixedSystemFixture : MixedParserFixture;

    public abstract class MethodLevelFixture
    {
        [Fact]
        [Trait("MotifTestLevel", "Integration")]
        public void Test() { }
    }

    private static bool HasTests(Type type) => !type.IsAbstract && type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                               BindingFlags.Instance | BindingFlags.Static)
        .Any(method => method.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute is FactAttribute));

    private static void RestoreProcessEnvironment(IDictionary originalEnvironment)
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (!originalEnvironment.Contains(entry.Key))
                Environment.SetEnvironmentVariable((string)entry.Key, null);

        foreach (DictionaryEntry entry in originalEnvironment)
            Environment.SetEnvironmentVariable((string)entry.Key, (string?)entry.Value);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;

        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }

}
