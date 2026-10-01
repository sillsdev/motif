using System.Reflection;
using SIL.Motif.Tests.Parser;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Resolves a test class's level and checks it against the selected levels.</summary>
internal static class TestLevelClassifier
{
    internal const string EnvironmentVariable = "MOTIF_TEST_LEVELS";

    private static readonly HashSet<string> ValidLevels = ["Unit", "Integration", "System"];

    /// <summary>Reads a comma-separated level selection, with <see langword="null"/> meaning all levels.</summary>
    internal static IReadOnlySet<string>? ParseSelection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "All", StringComparison.OrdinalIgnoreCase))
            return null;

        var levels = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (levels.Length == 0 || levels.Any(level => !ValidLevels.Contains(level)) ||
            levels.Distinct(StringComparer.Ordinal).Count() != levels.Length)
            throw new InvalidOperationException(
                $"{EnvironmentVariable} must be All or a comma-separated list of Unit, Integration, and System.");
        return levels.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Returns the effective level, promoting parser and walkthrough classes to System.</summary>
    internal static string ResolveLevel(
        string className, string defaultLevel, IEnumerable<string?> declarations, bool hasRealParserFact)
    {
        var declared = declarations.ToArray();
        if (declared.Length > 1)
            throw new InvalidOperationException($"{className} declares more than one MotifTestLevel.");
        if (!ValidLevels.Contains(defaultLevel))
            throw new InvalidOperationException($"{defaultLevel} is not a valid default test level for {className}.");

        var requiresSystem = hasRealParserFact || IsWalkthroughOrSmoke(className);
        var level = declared.SingleOrDefault() ?? (requiresSystem ? "System" : defaultLevel);
        if (level is null || !ValidLevels.Contains(level))
            throw new InvalidOperationException($"{level} is not a valid test level for {className}.");
        if (requiresSystem && level != "System")
            throw new InvalidOperationException($"{className} requires the System test level.");
        return level;
    }

    /// <summary>Checks whether an xUnit case belongs to the selected levels.</summary>
    internal static bool IsSelected(
        IXunitTestCase testCase, string defaultLevel, IReadOnlySet<string>? selectedLevels)
    {
        var className = testCase.TestMethod.TestClass.Class.Name;
        var declarations = testCase.Traits.TryGetValue("MotifTestLevel", out var values)
            ? values.Cast<string?>()
            : [];
        var realParserFact = testCase.TestMethod.Method
            .GetCustomAttributes(typeof(RealParserFactAttribute).AssemblyQualifiedName!)
            .Any();
        var level = ResolveLevel(className, defaultLevel, declarations, realParserFact);
        return selectedLevels is null || selectedLevels.Contains(level);
    }

    /// <summary>Reads the one project default embedded in a test assembly.</summary>
    internal static string ReadAssemblyDefault(Assembly assembly)
    {
        var values = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == "MotifTestDefaultLevel")
            .Select(attribute => attribute.Value)
            .ToArray();
        var value = values.SingleOrDefault();
        if (values.Length != 1 || value is null || !ValidLevels.Contains(value))
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} must have one valid MotifTestDefaultLevel assembly value.");
        return value;
    }

    private static bool IsWalkthroughOrSmoke(string className) =>
        className.Split('.').Any(segment => segment is "Walkthrough" or "Smoke");
}
