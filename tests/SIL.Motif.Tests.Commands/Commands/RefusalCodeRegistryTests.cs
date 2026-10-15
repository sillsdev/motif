using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Generator;
using SIL.Motif.Contract.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <see cref="RefusalCodes"/>, <see cref="HostRefusalCodes"/> and <see cref="ParsimonyRefusalCodes"/> together are the closed registry of every
/// refusal code: each constant is well formed and unique, and no code appears in the source that the registry
/// does not name.
/// </summary>
public sealed class RefusalCodeRegistryTests
{
    private static readonly Regex WellFormed = new("^[a-z]+(?:-[a-z]+)*(?:\\.[a-z0-9]+(?:-[a-z0-9]+)*)+$", RegexOptions.None);

    // A filename is shaped like a refusal code, so the scan would report "instructions.md" as one.
    private static readonly HashSet<string> FileExtensions = new(StringComparer.Ordinal)
    {
        "bak", "db", "dll", "dylib", "exe", "fwdata", "json", "jsonl", "ldml", "lock", "md", "png", "py", "sqlite",
        "tsv", "txt", "xml", "zip",
    };

    // Dotted literals that are not refusal codes: classification and finding codes, cgroup files, SQL tables.
    private static readonly Regex NotRefusalCodes = new(
        "^(?:delta\\.|cgroup\\.|cpu\\.|memory\\.|facts\\.|sil\\.lcmodel$|parse\\.allomorph\\.|" +
        "grammar\\.(?:empty-lexicon|required-slot-empty|stored-analysis\\.))", RegexOptions.None);

    private static IReadOnlyList<(string Name, string Value)> Registry() =>
        new[] { typeof(RefusalCodes), typeof(HostRefusalCodes), typeof(ParsimonyRefusalCodes) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (type.Name + "." + field.Name, (string)field.GetRawConstantValue()!)))
            .ToList();

    [Fact]
    public void RegistryConstantsAreWellFormedAndUnique()
    {
        var registry = Registry();
        Assert.NotEmpty(registry);
        Assert.Empty(registry.Where(entry => !WellFormed.IsMatch(entry.Value)).Select(entry => entry.Name + " = " + entry.Value));
        Assert.Empty(registry.GroupBy(entry => entry.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key + " is declared by " + string.Join(", ", group.Select(entry => entry.Name))));
    }

    [Fact]
    public void RegistryCoversEveryCodeTheSourceUses()
    {
        var registered = Registry().Select(entry => entry.Value).ToHashSet(StringComparer.Ordinal);
        var used = CodeLiteralsInSource();

        Assert.Empty(used.Where(code => !registered.Contains(code)).Order(StringComparer.Ordinal));
    }

    // Dotted lower-case literals in every product project's source, minus those that end in a file extension.
    private static HashSet<string> CodeLiteralsInSource()
    {
        var codePattern = new Regex("\"((?:[a-z][a-z-]*\\.)+[a-z][a-z-]*)\"", RegexOptions.None);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var src = Path.Combine(RepoPaths.FindRepoRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            foreach (Match match in codePattern.Matches(File.ReadAllText(file)))
            {
                var literal = match.Groups[1].Value;
                if (FileExtensions.Contains(literal[(literal.LastIndexOf('.') + 1)..])) continue;
                if (!NotRefusalCodes.IsMatch(literal)) codes.Add(literal);
            }
        }
        return codes;
    }
}
