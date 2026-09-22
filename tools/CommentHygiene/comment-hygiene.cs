using System.Text.RegularExpressions;

return CommentHygiene.Run(args);

/// <summary>
/// Counts comment-hygiene violations in this repo's C#, PowerShell and MSBuild files, and fails if any remain.
/// Run it as <c>dotnet run tools/CommentHygiene/comment-hygiene.cs -- [-List] [-Category name]</c>.
/// </summary>
/// <remarks>
/// <para>
/// ZERO TOLERANCE. Every violation is reported and every one is meant to go; there is no accepted count and no
/// baseline file. A baseline records the CURRENT count as acceptable, and re-baselining after a rule change
/// quietly relabels old debt as the new normal.
/// </para>
/// <para>
/// Rules are stated in the code-comments skill. Summary: a comment explains why; code says what, git says when,
/// the plan and the issue register say where the project is. Project state in a source comment is true when
/// written and unchecked forever after.
/// </para>
/// <para>
/// LINE-LEVEL (plan-reference, issue-reference, slice-status, date-in-comment, history-prose, attribution) score
/// a single comment line against a regex. These catch project state: facts that were true when typed and are
/// never checked again.
/// </para>
/// <para>
/// <c>impl-comment-too-long</c> counts a single over-WIDE line as well as a multi-line block. Measuring only line
/// count rewards reflowing a paragraph onto one 400-character line, which reads worse than the block it replaced
/// and satisfies nothing the rule is for. One line means one readable line.
/// </para>
/// <para>
/// BLOCK-LEVEL (impl-comment-too-long, cross-reference-claim, docs-link-broken, dead-citation) score a run of
/// consecutive comment lines. They exist because the line-level family cannot see a comment that is simply FALSE
/// ABOUT BEHAVIOUR, which is a different and more expensive class. Length is not why such a comment rots -- a
/// one-line false claim rots identically. What rots is an unverifiable CLAIM ABOUT ANOTHER CODE ENTITY. A claim
/// is answered by a <c>pinned by</c> test citation, and only by that; where a block quotes a contract, cite the
/// test that settles the claim and quote on.
/// </para>
/// <para>
/// API DOC COMPLETENESS (api-doc-defers-offline) has one rule: an API doc says what it means or cites a URL
/// anyone can open. A repo-relative Markdown path resolves only inside a checkout, so it is unreadable from an
/// IDE tooltip or the compiled XML docs -- the two places an API doc is actually read. Cite an ADR by number,
/// name a contract in prose, and inline the fact. The rule is about the reader's position, not about which top
/// directory the path starts in.
/// </para>
/// <para>
/// STRING LITERALS: an exception message is read further from the code than any comment, so a plan or issue
/// reference rots there at least as badly. Code lines are scanned for the two DISTINCTIVE patterns only, and
/// only in C#: not the bare register ID, because one uppercase letter and a digit matches ordinary identifiers;
/// not in PowerShell or in this checker's own source, because both quote the patterns and would report their
/// own definitions. A repo-relative Markdown path is NOT banned from a message string: its reader is standing in
/// a checkout, so a path helps them.
/// </para>
/// <para>
/// PROJECT FILES: <c>.csproj</c>, <c>.props</c> and <c>.targets</c> are committed source that people read, and an
/// XML comment in one rots exactly like a <c>//</c> block does. Only the LINE-LEVEL family applies there: not the
/// length rule, because MSBuild files legitimately carry longer explanatory blocks; not api-doc-defers-offline,
/// because nobody reads a project file's comment in a tooltip. A project file is scanned whole-file, because an
/// XML comment can span lines and share a line with markup; each physical line inside one is scored on its own.
/// </para>
/// <para>
/// This differs from PanGloss's Rust original in two ways that matter. A cref is resolved by the C# compiler
/// (CS1574), so code-to-code references are NOT banned here; a cref still proves only that the name resolves,
/// never that the sentence is true. And the interface/implementation split is <c>///</c> on a public or internal
/// member (long form allowed) versus <c>//</c> anywhere and <c>///</c> on a private member (one line).
/// </para>
/// <para>
/// Generated <c>*.g.cs</c> files are excluded: the fix for a bad comment in one is a fix to the emitter. The
/// emitters ARE scanned, and a <c>//</c> line inside a raw-string template is scanned by the LINE-LEVEL family,
/// because a plan reference written into a template still ships in a committed file. It is exempt from the
/// BLOCK-LEVEL length rule, because what that block measures is the generated file's auto-generated banner. A
/// <c>///</c> line inside a template is still checked for API-doc completeness on its own, per line.
/// </para>
/// <para>
/// <c>spikes/</c> is scanned on the same terms as <c>src/</c> and <c>tests/</c>: it is committed C# that the test
/// project compiles against. Exempting a directory is a baseline wearing a different hat.
/// </para>
/// <para>
/// <see cref="SplitCSharpLine"/> tracks string, verbatim-string and char-literal state in one pass per line, so
/// a <c>//</c> is known to be a comment rather than the middle of a URL. A TRAILING comment is then scanned by the
/// line-level family and for width, and COMMENT TEXT ASSEMBLED IN A LITERAL, the <c>$"/// ..."</c> an emitter
/// writes, is scanned as what it becomes. What remains uncheckable is the interpolated VALUE, which no static
/// pass will see.
/// </para>
/// </remarks>
internal static class CommentHygiene
{
    private const RegexOptions Blind = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private const RegexOptions Exact = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // Left-boundaried so a legitimate research filename's tail cannot match; one alternative is exact-case.
    private static readonly (string Name, Regex Pattern)[] LineCategories =
    [
        ("plan-reference", new(@"docs/plan-motif|docs/plan-cross-repo|docs/plan-lcmcrdt|plan-motif\.md|(?-i:HANDOFF\.md)|build-stages\.md|implementation-plan\.md|operation-catalog-plan\.md|stage2-change-management\.md", Blind)),
        // The last alternative is a bare register ID, which needs no file path beside it in order to rot.
        ("issue-reference", new(@"(?-i:\bMOT-\d+)|docs/issues|issues\.md|(?-i:\b(?:issue|issues)\s+[A-Z]\d+)|(?-i:(?<![A-Za-z0-9_+#/-])[A-Z]\d{1,2}(?![A-Za-z0-9_.+#-]))", Blind)),
        ("slice-status", new(@"not wired|NOT wired|purely additive|Purely additive|not yet consumed|no slice ships|today exactly|currently names|a later increment|later slice", Blind)),
        // The lookbehind spares research filenames, where the date is part of an anchor, not a claim.
        ("date-in-comment", new(@"(?<!docs/research/)\b20\d\d-\d\d-\d\d\b", Blind)),
        ("history-prose", new(@"used to read|previously read|first shipped|used to be|this used to|renamed from|was stale|corrected in place|the older .* wording|earlier version of this", Blind)),
        ("attribution", new(@"an earlier agent|a subagent|the owner asked|the reviewer asked|another agent|this session", Blind)),
    ];

    private static readonly string[] BlockCategories =
        ["impl-comment-too-long", "unanchored-exception", "cross-reference-claim", "docs-link-broken", "dead-citation", "api-doc-defers-offline"];

    private const string LongBlocksAnchored = "long-blocks-anchored";

    // Present-tense active only, word-boundaried: a claim about a DIFFERENT entity, which nothing checks.
    private static readonly Regex ClaimVerbs = new(string.Join('|',
        @"\brefuses\b", @"\brejects\b", @"\baccepts unconditionally\b", @"\balready refuses\b",
        @"\bonly caller\b", @"\bsole caller\b", @"\bcalled from exactly\b", @"\bzero callers\b",
        @"\bno production caller\b", @"\bunreachable\b", @"\bnever called\b", @"\bnever reached\b",
        @"\bcannot happen\b", @"\bcannot be reached\b", @"\balways returns\b", @"\bnever returns\b",
        @"\bnever fires\b", @"\balways fires\b", @"\bis not wired\b"), Blind);

    // A machine-checked citation to the test that pins a claim; the name shape is C# PascalCase.
    private static readonly Regex CitationPhrase =
        new(@"(?i)(?:pinned by|pins|asserted by|witnessed by|proved by|checked by)\s+`([A-Za-z_][A-Za-z0-9_]*)`", Blind);

    // The only grounds on which an implementation comment may exceed one line, closed so none is invented.
    private static readonly string[] ExceptionTags = ["SAFETY:"];

    // One line means one readable line; reflowing a paragraph onto one 400-character line is worse.
    private const int MaxImplWidth = 110;

    private const string MdPath = @"\b(?:docs|manifest|tools|spikes|tests|src)/[A-Za-z0-9._/-]*\.md\b";

    // Any repo-relative Markdown path: case-blind as a test, case-exact when each one is resolved on disk.
    private static readonly Regex MdPathTest = new(MdPath, Blind);
    private static readonly Regex MdPathFind = new(MdPath, Exact);

    // Scanned on CODE lines in C# only; see the remarks on STRING LITERALS for why both limits are needed.
    private static readonly (string Name, Regex Pattern)[] LiteralCategories =
    [
        LineCategories[0],
        ("issue-reference", new(@"(?-i:\bMOT-\d+)|docs/issues|issues\.md|(?-i:\b(?:issue|issues)\s+[A-Z]\d+)", Blind)),
    ];

    private static readonly string[] ProjectFileExtensions = [".csproj", ".props", ".targets"];

    private static readonly Regex TypeDeclaration = new(@"\b(?:class|record|struct|interface|enum|void|Task)\s+([A-Za-z_][A-Za-z0-9_]*)", Exact);
    private static readonly Regex MethodDeclaration =
        new(@"^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?[A-Za-z_][\w<>,\[\]\. ]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", Exact);
    private static readonly Regex ApiModifier = new(@"^\s*(public|internal|protected)\b", Blind);
    private static readonly Regex PrivateModifier = new(@"^\s*private\b", Blind);
    private static readonly Regex BareTypeDeclaration = new(@"^\s*(sealed\s+|static\s+|abstract\s+|partial\s+)*(class|record|struct|interface|enum)\b", Blind);
    private static readonly Regex EnumMember = new(@"^[A-Za-z_][A-Za-z0-9_]*\s*(=\s*[^;]+?)?,?\s*$", Blind);
    private static readonly Regex InterfaceDeclaration = new(@"\binterface\s+[A-Za-z_]", Blind);
    private static readonly Regex OtherTypeDeclaration = new(@"\b(class|record|struct|enum)\s+[A-Za-z_]", Blind);
    private static readonly Regex XmlComment = new(@"<!--([\s\S]*?)-->", Exact);
    private static readonly Regex XmlCommentLineBreak = new("\r?\n", Exact);
    private static readonly Regex BinOrObj = new(@"\\(bin|obj)\\", Blind);
    private static readonly Regex CommentMarker = new("///?", Exact);
    private static readonly Regex HttpUrl = new("https?://", Blind);
    private static readonly Regex ResearchLink = new(@"docs/research/[A-Za-z0-9._/-]+\.md", Blind);

    private static string _repoRoot = "";
    private static readonly HashSet<string> DeclaredNames = new(StringComparer.Ordinal);

    private sealed record Hit(string Category, string File, int Line, string Text);

    private sealed class FileReport
    {
        public List<Hit> Hits { get; } = [];
        public int AnchoredLongBlocks { get; set; }
        public void Add(string category, string file, int line, string text) => Hits.Add(new(category, file, line, text.Trim()));
    }

    /// <summary>Scans the repository this file sits in, prints the counts, and returns 1 if any violation remains.</summary>
    public static int Run(string[] args)
    {
        var list = false;
        string? onlyCategory = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("-List", StringComparison.OrdinalIgnoreCase)) list = true;
            else if (args[i].Equals("-Category", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) onlyCategory = args[++i];
            else
            {
                Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: [-List] [-Category name]");
                return 2;
            }
        }

        _repoRoot = FindRepoRoot();
        var sourceFiles = CollectSourceFiles();
        if (sourceFiles.Count == 0)
        {
            Console.Error.WriteLine($"No source files found under '{_repoRoot}'. Refusing to report a clean tree from an empty scan.");
            return 2;
        }

        foreach (var file in sourceFiles.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in File.ReadLines(file))
            {
                foreach (Match m in TypeDeclaration.Matches(line)) DeclaredNames.Add(m.Groups[1].Value);
                foreach (Match m in MethodDeclaration.Matches(line)) DeclaredNames.Add(m.Groups[1].Value);
            }
        }

        var reports = new FileReport[sourceFiles.Count];
        Parallel.For(0, sourceFiles.Count, i => reports[i] = ScanFile(sourceFiles[i]));

        var categories = LineCategories.Select(c => c.Name).Concat(BlockCategories).ToList();
        var hits = categories.ToDictionary(c => c, _ => new List<Hit>());
        var anchored = 0;
        foreach (var report in reports)
        {
            anchored += report.AnchoredLongBlocks;
            foreach (var hit in report.Hits) hits[hit.Category].Add(hit);
        }

        var total = hits.Values.Sum(h => h.Count);
        Console.WriteLine();
        Console.WriteLine($"comment hygiene -- {sourceFiles.Count} file(s) scanned");
        Console.WriteLine(new string('-', 60));
        foreach (var cat in categories)
        {
            var n = hits[cat].Count;
            Console.WriteLine(string.Format("  {0} {1,-24} {2,6}", n > 0 ? "X" : ".", cat, n));
        }
        Console.WriteLine(string.Format("    {0,-24} {1,6}   (reported, never gated)", LongBlocksAnchored, anchored));
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"  TOTAL {total}");
        Console.WriteLine();

        if (list)
        {
            var cwd = Environment.CurrentDirectory;
            foreach (var cat in categories)
            {
                if (onlyCategory is not null && !cat.Equals(onlyCategory, StringComparison.OrdinalIgnoreCase)) continue;
                if (hits[cat].Count == 0) continue;
                Console.WriteLine();
                Console.WriteLine($"=== {cat} ({hits[cat].Count}) ===");
                foreach (var hit in hits[cat]) Console.WriteLine($"  {Relative(cwd, hit.File)}:{hit.Line}: {hit.Text}");
            }
        }

        return total > 0 ? 1 : 0;
    }

    // The repository root is the nearest ancestor of the working directory holding the solution file.
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Motif.sln"))) return dir.FullName;
        }
        throw new InvalidOperationException("Run this from inside the Motif repository: no Motif.sln above the working directory.");
    }

    private static string Relative(string cwd, string file)
    {
        var relative = Path.GetRelativePath(cwd, file);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? relative
            : "." + Path.DirectorySeparatorChar + relative;
    }

    private static List<string> CollectSourceFiles()
    {
        var files = new List<string>();
        void Add(string dir, string pattern, bool recurse)
        {
            var full = Path.Combine(_repoRoot, dir);
            if (!Directory.Exists(full)) return;
            var found = Directory.GetFiles(full, pattern, recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Array.Sort(found, StringComparer.OrdinalIgnoreCase);
            files.AddRange(found);
        }

        Add("src", "*.cs", true);
        Add("tests", "*.cs", true);
        Add("spikes", "*.cs", true);
        Add("tools", "*.cs", true);
        Add("tools", "*.ps1", true);
        Add("", "*.ps1", false);
        foreach (var dir in new[] { "src", "tests", "spikes", "tools" })
        {
            foreach (var ext in ProjectFileExtensions) Add(dir, "*" + ext, true);
        }
        foreach (var ext in ProjectFileExtensions) Add("", "*" + ext, false);

        return files
            .Where(f => !BinOrObj.IsMatch(f) && !f.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static FileReport ScanFile(string file)
    {
        var report = new FileReport();
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (ProjectFileExtensions.Contains(extension))
        {
            ScanProjectFile(file, report);
            return report;
        }

        var lines = File.ReadAllLines(file);
        var isPowerShell = extension == ".ps1";
        var quotesPatterns = file.StartsWith(Path.Combine(_repoRoot, "tools") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        var blockStart = -1;
        var blockLines = new List<string>();
        var blockIsDoc = false;

        void FlushBlock()
        {
            if (blockStart < 0) return;
            TestBlock(blockLines.ToArray(), blockStart, blockIsDoc, lines, file, report);
            blockStart = -1;
            blockLines.Clear();
        }

        var inRawString = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();

            // Emitter-template lines: scanned per line, exempt from the block length rule.
            var inTemplate = inRawString;
            if (inRawString)
            {
                if (trimmed.StartsWith("\"\"\"", StringComparison.Ordinal)) { inRawString = false; inTemplate = false; }
            }
            else if (trimmed.EndsWith("\"\"\"", StringComparison.Ordinal) && CountOf(trimmed, "\"\"\"") == 1)
            {
                inRawString = true;
            }

            var isDocLine = !isPowerShell && trimmed.StartsWith("///", StringComparison.Ordinal);
            var isImplLine = isPowerShell
                ? trimmed.StartsWith('#') && !trimmed.StartsWith("#>", StringComparison.Ordinal)
                : trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("///", StringComparison.Ordinal);

            if (isDocLine || isImplLine)
            {
                var body = isDocLine ? trimmed[3..] : isPowerShell ? trimmed.TrimStart('#') : trimmed[2..];
                ScoreLine(body, file, i + 1, report);

                if (inTemplate)
                {
                    // Length is exempt inside a template, completeness is not: this `///` ships as a public API doc.
                    if (isDocLine && MdPathTest.IsMatch(body)) report.Add("api-doc-defers-offline", file, i + 1, body);
                    FlushBlock();
                    continue;
                }

                if (blockStart < 0)
                {
                    blockStart = i;
                    blockIsDoc = isDocLine;
                    blockLines.Clear();
                }
                else if (blockIsDoc != isDocLine)
                {
                    TestBlock(blockLines.ToArray(), blockStart, blockIsDoc, lines, file, report);
                    blockStart = i;
                    blockIsDoc = isDocLine;
                    blockLines.Clear();
                }

                blockLines.Add(body);
                continue;
            }

            // Template content is generated code scanned above, and its fences would derail a line scanner.
            if (!isPowerShell && !inTemplate) ScanCodeLine(raw, trimmed, file, i + 1, quotesPatterns, report);
            FlushBlock();
        }

        FlushBlock();
        return report;
    }

    private static void ScanCodeLine(string raw, string trimmed, string file, int lineNumber, bool quotesPatterns, FileReport report)
    {
        var (commentStart, literals) = SplitCSharpLine(raw);
        var codePart = commentStart >= 0 ? raw[..commentStart] : raw;

        if (!quotesPatterns)
        {
            foreach (var (name, pattern) in LiteralCategories)
            {
                if (pattern.IsMatch(codePart)) report.Add(name, file, lineNumber, trimmed);
            }

            // Comment text built inside a literal: emitted into a generated file, where it is a comment.
            foreach (var literal in literals)
            {
                var marker = CommentMarker.Match(literal);
                if (!marker.Success) continue;

                var emitted = literal[(marker.Index + marker.Length)..];
                foreach (var (name, pattern) in LineCategories)
                {
                    if (name is "plan-reference" or "issue-reference") continue;
                    if (pattern.IsMatch(emitted)) report.Add(name, file, lineNumber, emitted);
                }

                if (marker.Length == 3 && MdPathTest.IsMatch(emitted)) report.Add("api-doc-defers-offline", file, lineNumber, emitted);
            }
        }

        if (commentStart < 0) return;
        var tail = raw[(commentStart + 2)..];
        ScoreLine(tail, file, lineNumber, report);

        // A trailing comment is one line by construction, so width is all that is left to check.
        if (tail.Length > MaxImplWidth)
        {
            report.Add("impl-comment-too-long", file, lineNumber, $"{tail.Length} chars: {tail[..70]}");
        }
    }

    private static void ScoreLine(string text, string file, int lineNumber, FileReport report)
    {
        foreach (var (name, pattern) in LineCategories)
        {
            if (pattern.IsMatch(text)) report.Add(name, file, lineNumber, text);
        }
    }

    private static void TestBlock(string[] text, int start, bool isDoc, string[] allLines, string file, FileReport report)
    {
        if (text.Length == 0) return;
        var joined = string.Join(' ', text);

        var isApiDoc = isDoc && DeclarationVisibility(allLines, start + text.Length) == "api";
        var anchored = ResearchLink.IsMatch(joined) || HttpUrl.IsMatch(joined) || CitationPhrase.IsMatch(joined);
        var tooWide = text.Length == 1 && text[0].Length > MaxImplWidth;

        if (!isApiDoc && (text.Length > 1 || tooWide))
        {
            var hasException = ExceptionTags.Any(tag => joined.Contains(tag, StringComparison.OrdinalIgnoreCase));
            if (hasException)
            {
                if (text.Length > 3) report.Add("unanchored-exception", file, start + 1, text[0]);
            }
            else if (tooWide)
            {
                report.Add("impl-comment-too-long", file, start + 1, $"{text[0].Length} chars: {text[0][..70]}");
            }
            else
            {
                report.Add("impl-comment-too-long", file, start + 1, $"{text.Length} lines: {text[0]}");
            }
        }
        else if (isApiDoc)
        {
            // An API doc stands alone; an outward pointer must be a URL, not a path in a checkout.
            if (MdPathTest.IsMatch(joined))
            {
                report.Add("api-doc-defers-offline", file, start + 1, $"{text.Length} lines: {text[0]}");
            }
            else if (text.Length > 12 && anchored)
            {
                report.AnchoredLongBlocks++;
            }
        }

        // A claim about another entity, with no citation of the test that pins it.
        if (ClaimVerbs.IsMatch(joined) && !CitationPhrase.IsMatch(joined)) report.Add("cross-reference-claim", file, start + 1, text[0]);

        foreach (Match m in MdPathFind.Matches(joined))
        {
            var target = Path.Combine(_repoRoot, m.Value.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(target) && !Directory.Exists(target)) report.Add("docs-link-broken", file, start + 1, m.Value);
        }

        foreach (Match m in CitationPhrase.Matches(joined))
        {
            var name = m.Groups[1].Value;
            if (!DeclaredNames.Contains(name)) report.Add("dead-citation", file, start + 1, $"pinned by `{name}` -- no such declaration");
        }
    }

    // Visibility of the first non-blank, non-attribute, non-comment line beneath a `///` block: api or private.
    private static string DeclarationVisibility(string[] lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Length && i < startIndex + 12; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (ApiModifier.IsMatch(line)) return "api";
            if (PrivateModifier.IsMatch(line)) return "private";
            // An unmodified top-level type is internal, so it is an API surface within the assembly.
            if (BareTypeDeclaration.IsMatch(line)) return "api";
            // A bare identifier with no type and no semicolon is an enum member: API, at its enum's visibility.
            if (EnumMember.IsMatch(line)) return "api";
            return EnclosingTypeVisibility(lines, startIndex);
        }

        return "private";
    }

    // An interface member carries no modifier because it cannot: it is public at its interface's visibility.
    private static string EnclosingTypeVisibility(string[] lines, int startIndex)
    {
        for (var i = Math.Min(startIndex, lines.Length - 1); i >= 0; i--)
        {
            if (InterfaceDeclaration.IsMatch(lines[i])) return "api";
            if (OtherTypeDeclaration.IsMatch(lines[i])) return "private";
        }

        return "private";
    }

    /// <summary>
    /// One pass over a C# line tracking literal state: which <c>//</c> starts a comment, and what each string
    /// literal on the line holds.
    /// </summary>
    internal static (int CommentStart, List<string> Literals) SplitCSharpLine(string line)
    {
        var literals = new List<string>();
        var i = 0;
        var n = line.Length;

        while (i < n)
        {
            var c = line[i];

            if (c == '/' && i + 1 < n && line[i + 1] == '/') return (i, literals);

            // Verbatim string: no backslash escapes, and a doubled quote is one quote rather than the end.
            if (c == '@' && i + 1 < n && line[i + 1] == '"')
            {
                i += 2;
                var start = i;
                while (i < n)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < n && line[i + 1] == '"') { i += 2; continue; }
                        break;
                    }
                    i++;
                }
                literals.Add(line[start..Math.Min(i, n)]);
                i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                var start = i;
                while (i < n)
                {
                    if (line[i] == '\\') { i += 2; continue; }
                    if (line[i] == '"') break;
                    i++;
                }
                literals.Add(line[start..Math.Min(i, n)]);
                i++;
                continue;
            }

            // A char literal can hold a lone quote or slash, either of which would derail the scan.
            if (c == '\'')
            {
                i++;
                while (i < n)
                {
                    if (line[i] == '\\') { i += 2; continue; }
                    if (line[i] == '\'') break;
                    i++;
                }
                i++;
                continue;
            }

            i++;
        }

        return (-1, literals);
    }

    // XML comments can span lines or share a line with markup; scanned whole-file, line-level families only.
    private static void ScanProjectFile(string file, FileReport report)
    {
        var text = File.ReadAllText(file);
        foreach (Match m in XmlComment.Matches(text))
        {
            var startLine = text.AsSpan(0, m.Index).Count('\n') + 1;
            var bodyLines = XmlCommentLineBreak.Split(m.Groups[1].Value);
            for (var li = 0; li < bodyLines.Length; li++) ScoreLine(bodyLines[li], file, startLine + li, report);
        }
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
