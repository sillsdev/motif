using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

#if !TOKEN_HYGIENE_UNDER_TEST
return TokenHygiene.Run(args);
#endif

/// <summary>
/// Fails the build when the App's views or token files style anything with a raw value instead of a design token.
/// Run it as <c>dotnet run --file tools/TokenHygiene/token-hygiene.cs -- [-Advisory]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The App's look is built in three layers. <c>Tokens/Primitives.axaml</c> holds raw values named by value and is
/// the only file allowed to. <c>Tokens/Intent.axaml</c> names what a value is for, and refers only to a Primitive
/// or to a Semi.Avalonia role. <c>Tokens/Components/*.axaml</c> style one component each: colours from Intent keys
/// only, sizes from Intent, Component or Primitive keys. A view names Intent or Component keys, never a Primitive
/// key, a Semi key, or a literal colour, gap, radius, stroke, type size or fixed size.
/// </para>
/// <para>
/// ZERO TOLERANCE, like the comment gate: there is no baseline, and the only exceptions are the named entries in
/// <see cref="Allowlist"/>, each with a reason. An allowlist entry that no longer matches a violation is itself a
/// violation, so the list cannot outlive what it excuses.
/// </para>
/// <para>
/// What is not styling is allowed: geometry path data, a zero, <c>Auto</c> and star grid sizes, bindings, and
/// resource references on properties that are not colours or sizes (a control theme, a converter).
/// </para>
/// <para>
/// <c>-Advisory</c> reports every violation and exits zero. It exists for staging a sweep, never as a standing
/// setting: an advisory gate is a suggestion.
/// </para>
/// </remarks>
internal static class TokenHygiene
{
    /// <summary>Styling properties whose value is a colour or brush.</summary>
    internal static readonly HashSet<string> ColourProperties = new(StringComparer.Ordinal)
    {
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color", "SelectionBrush",
        "SelectionForegroundBrush", "CaretBrush", "PlaceholderForeground", "OpacityMask",
    };

    /// <summary>Styling properties whose value is a gap, a type size, a radius, a stroke or a fixed size.</summary>
    internal static readonly HashSet<string> SizeProperties = new(StringComparer.Ordinal)
    {
        "Margin", "Padding", "Spacing", "RowSpacing", "ColumnSpacing", "FontSize", "CornerRadius", "BorderThickness",
        "StrokeThickness", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "RowDefinitions", "ColumnDefinitions",
    };

    /// <summary>
    /// The named exceptions: a file, a fragment of the offending source line, and why the value is not a design
    /// decision. Each entry must still match a violation, or the run fails.
    /// </summary>
    internal static readonly Allowance[] Allowlist =
    [
        new("src/SIL.Motif.App/Views/MainWindow.axaml", "<Canvas Width=\"24\" Height=\"24\">",
            "the page icons' path data is drawn in a 24-unit square; the Viewbox around it sets the on-screen size"),
    ];

    private const string AppRoot = "src/SIL.Motif.App/";
    private const string ViewsRoot = AppRoot + "Views/";
    private const string ComponentsRoot = AppRoot + "Tokens/Components/";
    private const string IntentFile = AppRoot + "Tokens/Intent.axaml";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Elements whose text content is a value: a raw one is allowed only in the primitive file.
    private static readonly HashSet<string> ValueElements = new(StringComparer.Ordinal)
    {
        "Thickness", "CornerRadius", "Double", "Single", "Int32", "GridLength", "Color", "SolidColorBrush",
    };

    private static readonly HashSet<string> BindingExtensions = new(StringComparer.Ordinal)
    {
        "Binding", "CompiledBinding", "ReflectionBinding", "TemplateBinding", "x:Null",
    };

    private static readonly Regex HexColour = new(@"^#(?:[0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");
    private static readonly Regex Markup = new(@"^\{\s*([\w:]+)\s*(.*?)\s*\}$", RegexOptions.Singleline);
    private static readonly Regex AnyPrimitiveReference = new(@"Resource\s+(?:ResourceKey\s*=\s*)?Primitive\.");
    private static readonly Regex AllowedGridPart = new(@"^(?:Auto|\*|\d*\.?\d+\*|-?0*\.?0+)$", RegexOptions.IgnoreCase);

    private const string Number = @"-?\d+(?:\.\d+)?";
    private static readonly Regex CodeColour = new(string.Join('|',
        @"\bnew\s+SolidColorBrush\s*\(", @"\bnew\s+ImmutableSolidColorBrush\s*\(", @"\bColor\.FromRgb\s*\(",
        @"\bColor\.FromArgb\s*\(", @"\bColor\.FromUInt32\s*\(", @"\bColor\.Parse\s*\(", @"\bSolidColorBrush\.Parse\s*\(",
        @"\bBrush\.Parse\s*\(", @"(?<![\w.])Brushes\.[A-Za-z]+\b", @"(?<![\w.])Colors\.[A-Za-z]+\b",
        "\"#[0-9A-Fa-f]{3,8}\""));
    // Any literal among the arguments counts, so a ternary such as (last ? 0 : 12) cannot hide one.
    private static readonly Regex CodeSizeConstructor = new(@"\bnew\s+(Thickness|CornerRadius|GridLength)\s*\(([^()]*)\)");
    private static readonly Regex NumericLiteral = new(@"(?<![\w.])\d+(?:\.\d+)?(?![\w.])");
    private static readonly Regex CodeAssignment = new(
        @"\b(?:Margin|Padding|Spacing|RowSpacing|ColumnSpacing|FontSize|StrokeThickness|Width|Height|MinWidth|MinHeight" +
        $@"|MaxWidth|MaxHeight)\s*(?<![=!<>+\-*/])=(?!=)\s*(?:\((?:double|float|int)\)\s*)?({Number})[dfmDFM]?(?=\s*[;,)}}]|\s*$)");
    // A ternary between sizes, such as (ShowLegend ? 14 : 10), hides its literals from the plain assignment.
    private static readonly Regex CodeConditionalAssignment = new(
        @"\b(?:Margin|Padding|Spacing|RowSpacing|ColumnSpacing|FontSize|StrokeThickness|Width|Height|MinWidth|MinHeight" +
        @"|MaxWidth|MaxHeight)\s*(?<![=!<>+\-*/])=(?!=)[^;,{}=?]*\?([^;,{}]*)");
    private static readonly Regex CodeSetter = new($@"\bnew\s+Setter\s*\(\s*[^,()]+,\s*({Number})\s*\)");
    private static readonly Regex CodePrimitiveKey = new("\"Primitive\\.");
    private static readonly Regex CodeResourceLookup = new(
        @"\b(?:TryGetResource|TryFindResource|FindResource|GetResourceObservable|DynamicResourceExtension|StaticResourceExtension)" +
        "\\s*\\(\\s*\"([^\"]*)\"");

    /// <summary>Scans the repository this command runs in and returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            Console.Error.WriteLine("token-hygiene: no Motif.sln above the current directory.");
            return 2;
        }
        return Run(args, root, Allowlist, Console.Out);
    }

    /// <summary>Scans <paramref name="repoRoot"/> against <paramref name="allowlist"/> and returns the exit code.</summary>
    internal static int Run(string[] args, string repoRoot, IReadOnlyList<Allowance> allowlist, TextWriter output)
    {
        var advisory = false;
        foreach (var arg in args)
        {
            if (arg.Equals("-Advisory", StringComparison.OrdinalIgnoreCase)) advisory = true;
            else if (!arg.Equals("-List", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine($"Unknown argument '{arg}'. Usage: [-Advisory]");
                return 2;
            }
        }

        var files = CollectScopedFiles(repoRoot);
        if (!files.Any(file => file.Path.StartsWith(ViewsRoot, StringComparison.Ordinal)))
        {
            output.WriteLine($"token-hygiene: no views found under '{repoRoot}'. Refusing to report a clean tree from an empty scan.");
            return 2;
        }

        var violations = Check(files, allowlist);
        output.WriteLine($"token-hygiene: scanned {files.Count} file(s).");
        if (violations.Count == 0)
        {
            output.WriteLine("token-hygiene: clean.");
            return 0;
        }

        foreach (var group in violations.GroupBy(v => v.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"  {group.Key,-22} {group.Count()}");
        output.WriteLine();
        foreach (var v in violations)
            output.WriteLine($"  {v.File}:{v.Line} [{v.Category}] {v.Detail}");
        output.WriteLine();
        output.WriteLine(advisory
            ? $"token-hygiene: {violations.Count} violation(s), reported only (-Advisory)."
            : $"token-hygiene: {violations.Count} violation(s). Name an Intent or Component key; raw values live in Tokens/Primitives.axaml.");
        return advisory ? 0 : 1;
    }

    /// <summary>
    /// Scans <paramref name="files"/>, each named by its repository-relative path, drops violations the allowlist
    /// excuses, and adds one for every allowlist entry that excused nothing.
    /// </summary>
    internal static List<Violation> Check(IEnumerable<SourceFile> files, IReadOnlyList<Allowance> allowlist)
    {
        var used = new bool[allowlist.Count];
        var kept = new List<Violation>();
        foreach (var file in files)
        {
            var lines = file.Text.Split('\n');
            foreach (var violation in ScanFile(file.Path, file.Text))
            {
                var source = violation.Line >= 1 && violation.Line <= lines.Length ? lines[violation.Line - 1] : "";
                var excused = false;
                for (var i = 0; i < allowlist.Count; i++)
                {
                    if (allowlist[i].File != file.Path || !source.Contains(allowlist[i].Snippet, StringComparison.Ordinal)) continue;
                    used[i] = true;
                    excused = true;
                }
                if (!excused) kept.Add(violation);
            }
        }
        for (var i = 0; i < allowlist.Count; i++)
        {
            if (!used[i])
                kept.Add(new(allowlist[i].File, 0, "stale-allowlist", $"nothing matches '{allowlist[i].Snippet}'; remove the entry"));
        }
        return kept;
    }

    /// <summary>Returns the violations in one file, chosen by the layer its path puts it in; unscoped paths have none.</summary>
    internal static List<Violation> ScanFile(string path, string text)
    {
        var layer = LayerOf(path);
        if (layer is null) return [];
        if (layer == Layer.ViewCode) return ScanCode(path, text);

        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            return [new(path, ex.LineNumber, "unreadable-xaml", ex.Message)];
        }
        return ScanXaml(path, layer.Value, document);
    }

    private enum Layer { View, ViewCode, Component, Intent }

    private static Layer? LayerOf(string path)
    {
        if (path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal)) return null;
        if (path.StartsWith(ViewsRoot, StringComparison.Ordinal))
        {
            if (path.EndsWith(".axaml", StringComparison.Ordinal)) return Layer.View;
            if (path.EndsWith(".cs", StringComparison.Ordinal)) return Layer.ViewCode;
            return null;
        }
        if (path.StartsWith(ComponentsRoot, StringComparison.Ordinal) && path.EndsWith(".axaml", StringComparison.Ordinal)
            && !path[ComponentsRoot.Length..].Contains('/')) return Layer.Component;
        return path == IntentFile ? Layer.Intent : null;
    }

    private static List<Violation> ScanXaml(string path, Layer layer, XDocument document)
    {
        var found = new List<Violation>();
        foreach (var element in document.Descendants())
        {
            var elementLine = ((IXmlLineInfo)element).LineNumber;
            var name = element.Name.LocalName;

            if (name == "Setter" && element.Attribute("Property") is { } property && element.Attribute("Value") is { } value)
            {
                var propertyName = LastSegment(property.Value.Trim('(', ')'));
                CheckValue(found, path, layer, propertyName, value.Value, LineOf(value));
            }

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace == Xaml) continue;
                if (name == "Setter" && attribute.Name.LocalName is "Property" or "Value") continue;
                var line = LineOf(attribute);
                if (layer == Layer.Intent)
                {
                    CheckIntentAttribute(found, path, attribute, line);
                    continue;
                }
                if (attribute.Name.LocalName == "ResourceKey")
                {
                    CheckReference(found, path, layer, colour: attribute.Value.StartsWith("Primitive.Color.", StringComparison.Ordinal),
                        "ResourceKey", "StaticResource", attribute.Value, line);
                    continue;
                }
                CheckValue(found, path, layer, LastSegment(attribute.Name.LocalName), attribute.Value, line);
            }

            var text = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim();
            if (text.Length == 0) continue;
            if (layer == Layer.Intent)
            {
                found.Add(new(path, elementLine, "literal-value", $"<{name}>{text}</{name}>: Intent refers to a Primitive or a Semi role"));
                continue;
            }
            var shown = $"<{name}>{text}</{name}>";
            if (ValueElements.Contains(name)) CheckValue(found, path, layer, name is "Color" or "SolidColorBrush" ? "Color" : "Width", text, elementLine, shown);
            else if (name.Contains('.')) CheckValue(found, path, layer, LastSegment(name), text, elementLine, shown);
        }
        return found;
    }

    private static void CheckIntentAttribute(List<Violation> found, string path, XAttribute attribute, int line)
    {
        var value = attribute.Value.Trim();
        if (attribute.Name.LocalName == "ResourceKey")
        {
            CheckIntentKey(found, path, value, line);
            return;
        }
        var markup = Markup.Match(value);
        if (!markup.Success)
        {
            found.Add(new(path, line, "literal-value", $"{attribute.Name.LocalName}=\"{value}\": Intent refers to a Primitive or a Semi role"));
            return;
        }
        if (markup.Groups[1].Value is "DynamicResource" or "StaticResource")
            CheckIntentKey(found, path, KeyOf(markup.Groups[2].Value), line);
        else
            found.Add(new(path, line, "not-a-token", $"{attribute.Name.LocalName}=\"{value}\": Intent refers to a Primitive or a Semi role"));
    }

    private static void CheckIntentKey(List<Violation> found, string path, string key, int line)
    {
        if (key.StartsWith("Primitive.", StringComparison.Ordinal) || key.StartsWith("Semi", StringComparison.Ordinal)) return;
        found.Add(new(path, line, "wrong-layer", $"Intent names '{key}'; it may name only a Primitive or a Semi role"));
    }

    private static void CheckValue(List<Violation> found, string path, Layer layer, string property, string rawValue, int line,
        string? shown = null)
    {
        var value = rawValue.Trim();
        var literal = shown ?? $"{property}=\"{value}\"";
        var colour = ColourProperties.Contains(property);
        var size = SizeProperties.Contains(property);
        var markup = Markup.Match(value);

        if (markup.Success)
        {
            var extension = markup.Groups[1].Value;
            if (extension is "DynamicResource" or "StaticResource")
            {
                var key = KeyOf(markup.Groups[2].Value);
                if (colour || size) CheckReference(found, path, layer, colour, property, extension, key, line);
                else if (layer == Layer.View && key.StartsWith("Primitive.", StringComparison.Ordinal))
                    found.Add(new(path, line, "primitive-in-view", $"{property} names '{key}'; a view names Intent or Component keys"));
                return;
            }
            if ((colour || size) && !BindingExtensions.Contains(extension))
                found.Add(new(path, line, "not-a-token", $"{property}=\"{value}\": name an Intent or Component key"));
            else if (layer == Layer.View && AnyPrimitiveReference.IsMatch(value))
                found.Add(new(path, line, "primitive-in-view", $"{property}=\"{value}\": a view names Intent or Component keys"));
            return;
        }

        if (colour)
        {
            if (value.Length > 0) found.Add(new(path, line, "literal-colour", literal));
            return;
        }
        if (size)
        {
            if (!IsNeutralSize(value)) found.Add(new(path, line, "literal-size", literal));
            return;
        }
        if (HexColour.IsMatch(value)) found.Add(new(path, line, "literal-colour", $"{property}=\"{value}\""));
    }

    private static void CheckReference(List<Violation> found, string path, Layer layer, bool colour, string property,
        string extension, string key, int line)
    {
        switch (layer)
        {
            case Layer.View when key.StartsWith("Primitive.", StringComparison.Ordinal):
                found.Add(new(path, line, "primitive-in-view", $"{property} names '{key}'; a view names Intent or Component keys"));
                break;
            case Layer.View when !key.StartsWith("Intent.", StringComparison.Ordinal) && !key.StartsWith("Component.", StringComparison.Ordinal):
                found.Add(new(path, line, "wrong-layer", $"{property} names '{key}'; a view names Intent or Component keys"));
                break;
            case Layer.Component when colour && !(key.StartsWith("Intent.", StringComparison.Ordinal) && extension == "DynamicResource"):
                found.Add(new(path, line, "wrong-layer",
                    $"{property} names {{{extension} {key}}}; a component's colours are {{DynamicResource Intent.*}}"));
                break;
            case Layer.Component when !colour && !key.StartsWith("Intent.", StringComparison.Ordinal)
                && !key.StartsWith("Component.", StringComparison.Ordinal) && !key.StartsWith("Primitive.", StringComparison.Ordinal):
                found.Add(new(path, line, "wrong-layer", $"{property} names '{key}'; a component's sizes are Intent, Component or Primitive keys"));
                break;
        }
    }

    // A value with no design decision in it: nothing, a zero on every side, Auto, or a star share.
    private static bool IsNeutralSize(string value) =>
        value.Length == 0 || value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).All(part => AllowedGridPart.IsMatch(part));

    private static string KeyOf(string arguments)
    {
        var key = arguments.Trim();
        if (key.StartsWith("ResourceKey", StringComparison.Ordinal)) key = key[(key.IndexOf('=') + 1)..].Trim();
        var comma = key.IndexOf(',');
        return comma < 0 ? key : key[..comma].Trim();
    }

    private static string LastSegment(string name) => name[(name.LastIndexOf('.') + 1)..];

    private static int LineOf(XObject node) => ((IXmlLineInfo)node).LineNumber;

    private static List<Violation> ScanCode(string path, string text)
    {
        var found = new List<Violation>();
        var lines = StripComments(text).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var code = lines[i];
            var line = i + 1;
            foreach (Match m in CodeColour.Matches(code)) found.Add(new(path, line, "literal-colour", m.Value));
            foreach (Match m in CodeSizeConstructor.Matches(code))
            {
                var arguments = m.Groups[2].Value;
                if (m.Groups[1].Value == "GridLength" && arguments.Contains("GridUnitType.Star", StringComparison.Ordinal)) continue;
                if (NumericLiteral.Matches(arguments).Any(literal => !IsNeutralSize(literal.Value)))
                    found.Add(new(path, line, "literal-size", m.Value));
            }
            foreach (Match m in CodeConditionalAssignment.Matches(code))
            {
                if (NumericLiteral.Matches(m.Groups[1].Value).Any(literal => !IsNeutralSize(literal.Value)))
                    found.Add(new(path, line, "literal-size", m.Value.Trim()));
            }
            foreach (var pattern in (Regex[])[CodeAssignment, CodeSetter])
            {
                foreach (Match m in pattern.Matches(code))
                {
                    if (!IsNeutralSize(m.Groups[1].Value.Replace(" ", ""))) found.Add(new(path, line, "literal-size", m.Value.Trim()));
                }
            }
            foreach (Match m in CodeResourceLookup.Matches(code))
            {
                var key = m.Groups[1].Value;
                if (!key.StartsWith("Intent.", StringComparison.Ordinal) && !key.StartsWith("Component.", StringComparison.Ordinal)
                    && !key.StartsWith("Primitive.", StringComparison.Ordinal))
                    found.Add(new(path, line, "wrong-layer", $"looks up '{key}'; a view names Intent or Component keys"));
            }
            if (CodePrimitiveKey.IsMatch(code)) found.Add(new(path, line, "primitive-in-view", "a view names Intent or Component keys"));
        }
        return found;
    }

    // Blanks every comment and keeps the line count, so a comment that quotes a banned form is never scanned.
    internal static string StripComments(string text)
    {
        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    if (text[i] == '\n') result.Append('\n');
                    i++;
                }
                i += 2;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                var verbatim = i > 0 && (text[i - 1] == '@' || (text[i - 1] == '$' && i > 1 && text[i - 2] == '@'));
                result.Append(c);
                i++;
                while (i < text.Length && text[i] != c)
                {
                    if (!verbatim && text[i] == '\\' && i + 1 < text.Length)
                    {
                        result.Append(text[i]).Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (!verbatim && text[i] == '\n') break;
                    result.Append(text[i]);
                    i++;
                }
                if (i < text.Length && text[i] == c)
                {
                    result.Append(c);
                    i++;
                }
                continue;
            }
            result.Append(c);
            i++;
        }
        return result.ToString();
    }

    private static List<SourceFile> CollectScopedFiles(string repoRoot)
    {
        var app = Path.Combine(repoRoot, "src", "SIL.Motif.App");
        if (!Directory.Exists(app)) return [];
        return Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Select(full => Path.GetRelativePath(repoRoot, full).Replace('\\', '/'))
            .Where(relative => LayerOf(relative) is not null)
            .Order(StringComparer.Ordinal)
            .Select(relative => new SourceFile(relative, File.ReadAllText(Path.Combine(repoRoot, relative)).Replace("\r\n", "\n")))
            .ToList();
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Motif.sln"))) return dir.FullName;
        }
        return null;
    }
}

/// <summary>One file to scan: its repository-relative path with forward slashes, and its text.</summary>
internal sealed record SourceFile(string Path, string Text);

/// <summary>One finding: where it is, what kind it is, and what was found.</summary>
internal sealed record Violation(string File, int Line, string Category, string Detail);

/// <summary>A named exception: the file, a fragment of the source line it excuses, and why the value is not styling.</summary>
internal sealed record Allowance(string File, string Snippet, string Reason);
