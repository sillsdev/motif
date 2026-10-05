using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

#if !TOKEN_HYGIENE_UNDER_TEST
return TokenHygiene.Run(args);
#endif

/// <summary>
/// Fails the build when the App styles with raw values, declares unused tokens, or references a missing token.
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

    private static readonly HashSet<string> LanguageStyleProperties = new(StringComparer.Ordinal)
    {
        "FontFamily", "FontFeatures", "FlowDirection",
    };

    /// <summary>
    /// The named exceptions: a file, a fragment of the offending source line, and why the value is not a design
    /// decision. Each entry must still match a violation, or the run fails.
    /// </summary>
    internal static readonly Allowance[] Allowlist = [];

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

    private static readonly Regex CodeColour = new(string.Join('|',
        @"\bnew\s+(?:Color|HslColor|HsvColor|SolidColorBrush|ImmutableSolidColorBrush)\s*\(",
        @"\b(?:Color|HslColor|HsvColor)\.(?:From\w+|Parse)\s*\(", @"\b(?:SolidColorBrush|Brush)\.Parse\s*\(",
        @"(?<![\w.])Brushes\.[A-Za-z]+\b", @"(?<![\w.])Colors\.[A-Za-z]+\b", "\"#[0-9A-Fa-f]{3,8}\""));
    private static readonly Regex CodeSizeConstructor = new(@"\bnew\s+(Thickness|CornerRadius|GridLength|Setter)\s*\(");
    // One list with the XAML check, so a property added there is policed in code too; grid strings are XAML only.
    private static readonly Regex CodeSizeAssignment = new(
        @"\b(?:" + string.Join('|', SizeProperties.Where(name => !name.EndsWith("Definitions", StringComparison.Ordinal)))
        + @")\s*(?<![=!<>+\-*/])=(?![=>])");
    private static readonly Regex CodeLanguageStyleAssignment = new(
        @"\b(?:" + string.Join('|', LanguageStyleProperties) + @")\s*(?<![=!<>+\-*/])=(?![=>])");
    private static readonly Regex NumericLiteral = new(@"(?<![\w.])\d+(?:\.\d+)?[dfmDFM]?(?![\w.])");
    private static readonly Regex StringLiteral = new(@"""(?:[^""\\]|\\.)*""");
    private static readonly Regex CodePrimitiveKey = new("\"Primitive\\.");
    private static readonly Regex CodeResourceLookup = new(
        @"\b(?:TryGetResource|TryFindResource|FindResource|GetResourceObservable|DynamicResourceExtension|StaticResourceExtension)" +
        "\\s*\\(\\s*\"([^\"]*)\"");
    private static readonly Regex MarkupResourceReference = new(
        @"\{\s*(?:StaticResource|DynamicResource)\s+(?:ResourceKey\s*=\s*)?(?<key>(?:Primitive|Intent|Component)\.[^,\s}]+)");

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

        var files = CollectAppFiles(repoRoot);
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
            : $"token-hygiene: {violations.Count} violation(s). Fix raw styling values, unresolved token references, or unused token declarations.");
        return advisory ? 0 : 1;
    }

    /// <summary>
    /// Scans <paramref name="files"/>, each named by its repository-relative path, drops violations the allowlist
    /// excuses, and adds one for every allowlist entry that excused nothing.
    /// </summary>
    internal static List<Violation> Check(IEnumerable<SourceFile> files, IReadOnlyList<Allowance> allowlist)
    {
        var sourceFiles = files.ToList();
        var used = new bool[allowlist.Count];
        var kept = new List<Violation>();
        foreach (var file in sourceFiles)
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
        kept.AddRange(AuditResources(sourceFiles));
        for (var i = 0; i < allowlist.Count; i++)
        {
            if (!used[i])
                kept.Add(new(allowlist[i].File, 0, "stale-allowlist", $"nothing matches '{allowlist[i].Snippet}'; remove the entry"));
        }
        return kept;
    }

    private static List<Violation> AuditResources(IReadOnlyList<SourceFile> files)
    {
        var declarations = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var references = new List<ResourceReference>();
        var findings = new List<Violation>();

        foreach (var file in files.Where(file => file.Path.EndsWith(".axaml", StringComparison.Ordinal)))
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(file.Text, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                findings.Add(new(file.Path, ex.LineNumber, "unreadable-resource-inventory", ex.Message));
                continue;
            }

            foreach (var element in document.Descendants())
            {
                var key = (string?)element.Attribute(Xaml + "Key");
                if (IsAppTokenKey(key))
                {
                    declarations.TryAdd(key!, (file.Path, LineOf(element.Attribute(Xaml + "Key")!)));
                    dependencies.TryAdd(key!, new HashSet<string>(StringComparer.Ordinal));
                }

                var owner = element.AncestorsAndSelf()
                    .Select(ancestor => (string?)ancestor.Attribute(Xaml + "Key"))
                    .FirstOrDefault(IsAppTokenKey);
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name == Xaml + "Key") continue;
                    if (attribute.Name.LocalName == "ResourceKey" && IsAppTokenKey(attribute.Value))
                        references.Add(new(file.Path, LineOf(attribute), owner, attribute.Value));
                    foreach (Match match in MarkupResourceReference.Matches(attribute.Value))
                        references.Add(new(file.Path, LineOf(attribute), owner, match.Groups["key"].Value));
                }
                if (!element.HasElements)
                {
                    foreach (Match match in MarkupResourceReference.Matches(element.Value))
                        references.Add(new(file.Path, LineOf(element), owner, match.Groups["key"].Value));
                }
            }
        }

        foreach (var file in files.Where(file => file.Path.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var code = StripComments(file.Text);
            foreach (Match match in CodeResourceLookup.Matches(code))
            {
                var key = match.Groups[1].Value;
                if (!IsAppTokenKey(key)) continue;
                var line = 1 + code[..match.Index].Count(character => character == '\n');
                references.Add(new(file.Path, line, null, key));
            }
        }

        var unresolved = new HashSet<(string File, int Line, string Key)>();
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (!declarations.ContainsKey(reference.Key))
            {
                unresolved.Add((reference.File, reference.Line, reference.Key));
                continue;
            }
            if (reference.Owner is null) roots.Add(reference.Key);
            else if (dependencies.TryGetValue(reference.Owner, out var keys)) keys.Add(reference.Key);
        }
        foreach (var reference in unresolved)
            findings.Add(new(reference.File, reference.Line, "unresolved-resource", $"references missing token '{reference.Key}'"));

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(roots);
        while (pending.TryDequeue(out var key))
        {
            if (!reachable.Add(key)) continue;
            if (dependencies.TryGetValue(key, out var keys))
                foreach (var dependency in keys) pending.Enqueue(dependency);
        }

        foreach (var (key, location) in declarations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!reachable.Contains(key))
                findings.Add(new(location.File, location.Line, "unreferenced-resource", $"token '{key}' has no reachable consumer"));
        }
        return findings;
    }

    private static bool IsAppTokenKey(string? key) => key is not null &&
        (key.StartsWith("Primitive.", StringComparison.Ordinal) || key.StartsWith("Intent.", StringComparison.Ordinal) ||
            key.StartsWith("Component.", StringComparison.Ordinal));

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
                if (layer == Layer.View && LanguageStyleProperties.Contains(propertyName))
                    found.Add(new(path, LineOf(value), "linguistic-style", $"{propertyName} is applied through WritingSystemTextStyleResolver"));
                CheckValue(found, path, layer, propertyName, value.Value, LineOf(value));
            }

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace == Xaml) continue;
                if (name == "Setter" && attribute.Name.LocalName is "Property" or "Value") continue;
                var line = LineOf(attribute);
                var propertyName = LastSegment(attribute.Name.LocalName);
                if (layer == Layer.View && LanguageStyleProperties.Contains(propertyName))
                    found.Add(new(path, line, "linguistic-style", $"{propertyName} is applied through WritingSystemTextStyleResolver"));
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
            case Layer.View when colour && extension != "DynamicResource":
                found.Add(new(path, line, "wrong-layer",
                    $"{property} names {{{extension} {key}}}; a colour follows the theme only through DynamicResource"));
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
            foreach (Match m in CodeLanguageStyleAssignment.Matches(code))
                found.Add(new(path, line, "linguistic-style", $"{m.Groups[0].Value.Trim()} is applied through WritingSystemTextStyleResolver"));
            foreach (Match m in CodeColour.Matches(code)) found.Add(new(path, line, "literal-colour", m.Value));
            var sizeLiteral = false;
            foreach (Match m in CodeSizeConstructor.Matches(code))
            {
                var arguments = Enclosed(code, m.Index + m.Length);
                if (m.Groups[1].Value == "GridLength" && arguments.Contains("GridUnitType.Star", StringComparison.Ordinal)) continue;
                if (m.Groups[1].Value == "Setter") arguments = arguments[(TopLevelComma(arguments) + 1)..];
                if (!sizeLiteral && HasSizeLiteral(arguments))
                {
                    found.Add(new(path, line, "literal-size", $"{m.Value}{arguments})"));
                    sizeLiteral = true;
                }
            }
            foreach (Match m in CodeSizeAssignment.Matches(code))
            {
                var value = Enclosed(code, m.Index + m.Length, stopAtComma: true);
                if (!sizeLiteral && HasSizeLiteral(value))
                {
                    found.Add(new(path, line, "literal-size", $"{m.Value}{value}".Trim()));
                    sizeLiteral = true;
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

    // Any nonzero number counts, however it is combined: 12 + 8, Math.Max(4, inset) and last ? 0 : 12 all hide one.
    private static bool HasSizeLiteral(string expression) =>
        NumericLiteral.Matches(StringLiteral.Replace(expression, "\"\""))
            .Any(literal => !IsNeutralSize(literal.Value.TrimEnd('d', 'f', 'm', 'D', 'F', 'M')));

    // The text from start to the bracket that closes the enclosing one, or to a top-level ; , or } when asked.
    private static string Enclosed(string code, int start, bool stopAtComma = false)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) return code[start..i];
                depth--;
            }
            else if (depth == 0 && (c == ';' || (stopAtComma && c == ','))) return code[start..i];
        }
        return code[start..];
    }

    private static int TopLevelComma(string arguments)
    {
        var depth = 0;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is '(' or '[' or '{') depth++;
            else if (arguments[i] is ')' or ']' or '}') depth--;
            else if (arguments[i] == ',' && depth == 0) return i;
        }
        return arguments.Length - 1;
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

    private static List<SourceFile> CollectAppFiles(string repoRoot)
    {
        var app = Path.Combine(repoRoot, "src", "SIL.Motif.App");
        if (!Directory.Exists(app)) return [];
        return Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Select(full => Path.GetRelativePath(repoRoot, full).Replace('\\', '/'))
            .Where(relative => (relative.EndsWith(".axaml", StringComparison.Ordinal) || relative.EndsWith(".cs", StringComparison.Ordinal))
                && !relative.Contains("/bin/", StringComparison.Ordinal) && !relative.Contains("/obj/", StringComparison.Ordinal))
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

/// <summary>A token reference, with the resource that owns it when the reference is an alias.</summary>
internal sealed record ResourceReference(string File, int Line, string? Owner, string Key);

/// <summary>One file to scan: its repository-relative path with forward slashes, and its text.</summary>
internal sealed record SourceFile(string Path, string Text);

/// <summary>One finding: where it is, what kind it is, and what was found.</summary>
internal sealed record Violation(string File, int Line, string Category, string Detail);

/// <summary>A named exception: the file, a fragment of the source line it excuses, and why the value is not styling.</summary>
internal sealed record Allowance(string File, string Snippet, string Reason);
