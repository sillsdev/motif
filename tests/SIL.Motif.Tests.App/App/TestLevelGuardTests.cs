using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
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
    private static readonly BoundaryEntry[] BoundaryInventory =
    [
        new("SIL.Motif.Tests.App", "ReadStateCommandClientTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "AnalysisMarkingCommandClientTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "ReviewCommandClientTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "ProjectSwitchDraftTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "OverviewSpeedRealClientTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "AdapterCancellationTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "PendingChangesWindowActivationTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "PendingChangesCliAppTests", TestBoundary.Process, "Integration"),
        new("SIL.Motif.Tests.App", "SetupRealClientTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "TextsRealClientTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "ExternalApplyActivationRealClientTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "CommandClientCancellationTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "CommandStartGateTests", TestBoundary.RealCommandClient, "Integration"),
        new("SIL.Motif.Tests.App", "PackageReleaseBoundaryIntegrationTests", TestBoundary.Process, "Integration"),
        new("SIL.Motif.Tests.App", "AppStartupSmokeSystemTests", TestBoundary.Process, "System"),
        new("SIL.Motif.Tests.App", "AnalyzeTextsLayoutTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "TimingSharesTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "TooltipPlacementTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "WordRowOpenLayoutSystemTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "ComparePanelSystemTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "ParsimonyDecisionChipTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "ParsimonyTabSelectionTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "TimingSourceAvailabilityTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "SettingsViewTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.App", "EntryPointStartupTests", TestBoundary.Deterministic, "Unit"),
        new("SIL.Motif.Tests.Cli", "RunnerSpineTests", TestBoundary.Process, "Integration"),
        new("SIL.Motif.Tests.Cli", "PortableWorkerPackageTests", TestBoundary.Process, "Integration"),
        new("SIL.Motif.Tests.Cli", "MorphotacticsSampleWorkflowTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.Cli", "RewriteRuleSampleTrialTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.Cli", "ParsimonySampleSurveySystemTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.Cli", "ReviewNumbersTextRenderingTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Cli", "HelpCatalogTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Commands", "PanGlossInvokerTests", TestBoundary.Process, "Integration"),
        new("SIL.Motif.Tests.Commands", "FakePanGlossSurfaceTests", TestBoundary.Process, "Integration"),
        new("SIL.Motif.Tests.Commands", "AssessorCatalogTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Commands", "CurrentEvidenceQueryTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Commands", "ChangeFitReasonsTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Commands", "RetirementRecipeDeclarationTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Commands", "RetirementExpectationTranslationTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.LibLcm", "OrderedGrammarSampleTests", TestBoundary.RealLcmCache, "Integration"),
        new("SIL.Motif.Tests.LibLcm", "AllomorphRetargetSampleTests", TestBoundary.RealLcmCache, "Integration"),
        new("SIL.Motif.Tests.LibLcm", "AllomorphReferenceFootprintSampleSurveyTests", TestBoundary.RealLcmCache, "Integration"),
        new("SIL.Motif.Tests.LibLcm", "EvaluationSetVerificationTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.LibLcm", "AllomorphRuleRetirementAcceptanceTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.LibLcm", "RedundantZeroAffixRecipeAcceptanceTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.LibLcm", "MorphotacticRecipeAcceptanceTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.LibLcm", "ConditionReviewRecipeAcceptanceTests", TestBoundary.RealParser, "System"),
        new("SIL.Motif.Tests.LibLcm", "FakeParserSeamIntegrationTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.LibLcm", "PanGlossParserTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.LibLcm", "ParsimonyFindingIdentityTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.LibLcm", "ParsimonyDispositionQueryTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.LibLcm", "JudgmentLineageResolverTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "AgentPluginSkillTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "CatalogExecutionTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "DifferencePagingTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "EncodingGuidesTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "ParsimonyRunCancellationTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "ParsimonyToolSurfaceTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "ParsimonyViewByteCapTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "ProjectResolverTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "RefusalMappingTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "ResultShaperTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "ToolSurfaceTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Mcp", "TrialSummaryTests", TestBoundary.FakeOnly, "Unit"),
        new("SIL.Motif.Tests.Worker", "TrialGrammarCheckTests", TestBoundary.FakeOnly, "Unit"),
    ];

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
                        .Any(attribute => attribute is RealParserFactAttribute);
                    var walkthroughNamespace = testClass.Namespace?.Split('.')
                        .Any(segment => segment is "Walkthrough" or "Smoke") == true;
                    var namedSystemClass = testClass.Name == "ExplainedWordCardWalkthroughReplayTests";
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

    [Fact]
    public void BoundaryInventoryKeepsRealSeamsOutOfUnitAndFakesInUnit()
    {
        var assemblies = Directory.GetFiles(AppContext.BaseDirectory, "SIL.Motif.Tests.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Support", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .ToDictionary(assembly => assembly.GetName().Name!, StringComparer.Ordinal);

        foreach (var entry in BoundaryInventory)
        {
            Assert.Contains(entry.Assembly, assemblies.Keys);
            var assembly = assemblies[entry.Assembly];
            var testClass = assembly.GetTypes().SingleOrDefault(type => type.Name == entry.ClassName);
            Assert.NotNull(testClass);
            Assert.True(HasTests(testClass!), $"{entry.ClassName} no longer contains tests.");

            var level = ResolveClassLevel(testClass!, assembly);
            Assert.Equal(entry.ExpectedLevel, level);
            switch (entry.Boundary)
            {
                case TestBoundary.RealLcmCache:
                case TestBoundary.RealCommandClient:
                case TestBoundary.Process:
                    Assert.NotEqual("Unit", level);
                    break;
                case TestBoundary.FakeOnly:
                case TestBoundary.Deterministic:
                    Assert.Equal("Unit", level);
                    break;
                case TestBoundary.RealParser:
                    Assert.Equal("System", level);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry.Boundary), entry.Boundary, null);
            }
        }
    }

    [Fact]
    public void DerivedRealParserFactsSelectSystem()
    {
        var method = typeof(DerivedParserFixture).GetMethod(nameof(DerivedParserFixture.Parser))!;
        using var testCase = CaseFor(typeof(DerivedParserFixture), method);

        Assert.True(TestLevelClassifier.IsSelected(testCase, "Integration", new HashSet<string> { "System" }));
        Assert.False(TestLevelClassifier.IsSelected(testCase, "Integration", new HashSet<string> { "Integration" }));
    }

    [Theory]
    [InlineData(typeof(RealClient.OverviewRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.WarningsRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.TimingRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.TryWordRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.FieldWorksAnalysisDriftRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.WindowRefusalRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.ProposalsRealClientTests), "Integration")]
    [InlineData(typeof(RealClient.ParsimonyPageIntegrationTests), "Integration")]
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

    public abstract class DerivedParserFixture
    {
        [DerivedRealParserFact]
        public void Parser() { }
    }

    public sealed class DerivedRealParserFactAttribute : RealParserFactAttribute { }

    private static bool HasTests(Type type) => !type.IsAbstract && type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                               BindingFlags.Instance | BindingFlags.Static)
        .Any(method => method.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute is FactAttribute));

    private static string ResolveClassLevel(Type testClass, Assembly assembly)
    {
        var declarations = testClass.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(TraitAttribute))
            .Select(attribute => attribute.ConstructorArguments)
            .Where(arguments => arguments.Count == 2 && arguments[0].Value as string == "MotifTestLevel")
            .Select(arguments => arguments[1].Value as string);
        var parserClass = testClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.Static)
            .SelectMany(method => method.GetCustomAttributes(inherit: true))
            .Any(attribute => attribute is RealParserFactAttribute);
        return TestLevelClassifier.ResolveLevel(testClass.FullName!,
            TestLevelClassifier.ReadAssemblyDefault(assembly), declarations, parserClass);
    }

    private enum TestBoundary { RealLcmCache, RealCommandClient, Process, FakeOnly, Deterministic, RealParser }

    private sealed record BoundaryEntry(string Assembly, string ClassName, TestBoundary Boundary, string ExpectedLevel);

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
