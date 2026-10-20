using System.Text.RegularExpressions;

return LocalizationHygiene.Run(args);

/// <summary>
/// Reports how many literal strings remain in the App's .axaml files, as a ratchet baseline for the move to .resx.
/// Run it as <c>dotnet run --file tools/LocalizationHygiene/localization-hygiene.cs</c>. It is report-only and
/// always exits zero: extraction lanes lower the number, and a reviewer compares it with the last one printed.
/// </summary>
internal static class LocalizationHygiene
{
    /// <summary>The attributes that carry window text.</summary>
    private static readonly string[] Attributes = ["Text", "Content", "ToolTip.Tip", "AutomationProperties.Name"];

    internal static int Run(string[] args)
    {
        var root = FindRoot(Directory.GetCurrentDirectory());
        var views = Path.Combine(root, "src", "SIL.Motif.App");
        var counts = Attributes.ToDictionary(a => a, _ => 0, StringComparer.Ordinal);
        var perFile = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Tokens{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (var attribute in Attributes)
            {
                var n = Literals(text, attribute);
                counts[attribute] += n;
                if (n > 0)
                    perFile[Path.GetRelativePath(root, file).Replace('\\', '/')] = perFile.GetValueOrDefault(Path.GetRelativePath(root, file).Replace('\\', '/')) + n;
            }
        }

        if (args.Contains("-List"))
            foreach (var (file, n) in perFile)
                Console.WriteLine($"{n,5}  {file}");
        foreach (var attribute in Attributes)
            Console.WriteLine($"localization hygiene (report only): {attribute,-27} {counts[attribute],5} literal");
        Console.WriteLine($"localization hygiene (report only): ratchet baseline {counts.Values.Sum()} literal strings in .axaml");
        return 0;
    }

    /// <summary>Counts attribute values that are neither a binding, a static reference nor empty.</summary>
    internal static int Literals(string axaml, string attribute)
    {
        var pattern = $@"(?<![\w.:]){Regex.Escape(attribute)}\s*=\s*""(?<value>[^""]*)""";
        return Regex.Matches(axaml, pattern)
            .Count(m => m.Groups["value"].Value.Trim() is { Length: > 0 } v && !v.StartsWith('{'));
    }

    private static string FindRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Motif.sln")))
                return dir.FullName;
        return start;
    }
}
