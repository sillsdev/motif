using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A folder name is private data, so no window text may carry one. Path shapes are crossed with sentence contexts
/// by a seeded generator rather than listed, because each hand-written list missed the next shape.
/// </summary>
public sealed class WindowPathLeakTests
{
    private const int CasesPerSeed = 1500;

    // Nonsense words in several scripts: none is prose, a file name, or a substring of another.
    private static readonly string[] Words =
    [
        "Qzorbax", "Wuntelk", "Pyfdrom", "Zoë-Müllqx", "Línguasqx", "שלוםעולם", "תיקפרטי", "日本語フォルダ", "秘密資料",
        "Приветмир", "секретпапка", "مجلدسري", "مشروعخاص", "Ελληνικάφακ", "한국어폴더", "Alice's-qx", "Say-\"qx\"",
        "[draftqx]", "(oldqx)", "a,bqx", "x&yqx", "100%qx", "-dashqx", "_underqx", "~tilqx", "qx#hash", "2026", "314159",
        "v1.2qx", "my.folderqx", ".hiddenqx",
    ];

    private static readonly string[] FileNames =
    [
        "project.fwdata", "gramática.fwdata", "תיק.fwdata", "文件.fwdata", "проект.fwdata", "My project.fwdata",
        "rules.txt", "language's.fwdata", "a\"b.fwdata",
    ];

    // Each context holds one {0}; the others show where punctuation and brackets sit around a path.
    private static readonly string[] Contexts =
    [
        "Cannot read {0} before the next step.", "Cannot read {0}.", "{0}", "See '{0}'.", "See \"{0}\".", "See [{0}].",
        "See ({0}).", "See ({0}), then retry.", "Open {0}, then retry.", "Open {0}; then retry.", "Open {0}: not found.",
        "Open {0}! Then retry.", "Is {0}? Try again.", "File path='{0}'.", "Path:'{0}'.", "Path:\"{0}\".", "path={0} failed.",
        "Don't change Alice's word. Path:'{0}'.", "Both {0} and {0} failed.", "Could not read\t{0}\nbefore the end.",
        "Rule /p/ -> [b] is odd; 1/2 forms, see {0}.", "<{0}> was refused.", "{{{0}}} was refused.",
        "The form 'ana is used, see {0} here.", "Glottal 'a and 'u forms cannot read {0}.", "Read {0} for 'ana.",
        "Say 'ana, then read {0}.", "He wrote 'ana' and {0} failed.",
    ];

    public static TheoryData<int> Seeds() => new(Enumerable.Range(1, 12));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void NoFolderNameReachesWindowText(int seed)
    {
        var random = new Random(seed);
        for (var index = 0; index < CasesPerSeed; index++)
        {
            var (input, folders, expected) = Generate(random);
            AssertNoFolderShown(seed, index, input, folders, expected);
        }
    }

    private static void AssertNoFolderShown(int seed, int index, string input, List<string> folders, string expected)
    {
        var diagnostic = new ParserCompileDiagnostic(input,
            [new ParserCompileIssue("Invalid", "Grammar", null, null, input, input)], input);
        var fromDiagnostic = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, input, parserDiagnostic: diagnostic));
        var fromMessage = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable,
            FailureReason.Refused, input));
        var issue = Assert.Single(fromDiagnostic.CompileIssues);
        var shown = new[]
        {
            fromDiagnostic.Sentence, fromDiagnostic.Details ?? string.Empty, issue.Text, issue.Advice,
            fromMessage.Sentence, fromMessage.Details ?? string.Empty,
        };
        var where = $"seed {seed} case {index}, input: {input}";
        foreach (var text in shown)
            foreach (var folder in folders)
                Assert.False(text.Contains(folder, StringComparison.Ordinal),
                    $"{where}\nfolder '{folder}' appears in: {text}");
        Assert.True(expected == issue.Text, $"{where}\nexpected: {expected}\nactual:   {issue.Text}");
    }

    private static (string Input, List<string> Folders, string Expected) Generate(Random random)
    {
        var rooted = random.Next(2) == 0;
        var context = Contexts[random.Next(Contexts.Length)];
        // An unquoted relative path cannot hold spaces in its first folder: prose would be mistaken for it.
        var quoted = context.Contains("'{0}'", StringComparison.Ordinal) || context.Contains("\"{0}\"", StringComparison.Ordinal);
        var fileName = FileNames[random.Next(FileNames.Length)];
        if (fileName.Contains(' ') && !quoted) fileName = "project.fwdata";
        var folders = new List<string>();
        var count = 1 + random.Next(4);
        var segments = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var folder = Words[random.Next(Words.Length)];
            // A leading bracket of a bare relative path is cut as punctuation, not as part of the folder.
            while (!rooted && index == 0 && folder[0] is '[' or '(') folder = Words[random.Next(Words.Length)];
            var second = Words[random.Next(Words.Length)];
            // A space after a dotted word or a closing quote reads as the end of a name, so it cannot be inside one.
            var canHaveSpace = (quoted || !folder.Contains('.')) && !folder.EndsWith('"');
            if (random.Next(3) == 0 && canHaveSpace && (rooted || quoted)) folder += " " + second;
            if (random.Next(5) == 0 && !rooted && !quoted && index > 0 && canHaveSpace) folder += " " + second;
            segments.Add(folder);
        }
        var windows = random.Next(2) == 0;
        var (root, separator) = random.Next(rooted ? 6 : 3) switch
        {
            0 when rooted => (windows ? @"C:\" : "C:/", windows ? "\\" : "/"),
            1 when rooted => (@"\\", "\\"),
            2 when rooted => ("/", "/"),
            3 when rooted => ("~/", "/"),
            4 when rooted => (random.Next(2) == 0 ? "./" : "../", "/"),
            5 when rooted => ("D:", windows ? "\\" : "/"),
            _ => (string.Empty, windows ? "\\" : "/"),
        };
        var path = root + string.Join(separator, segments) + separator + fileName;
        foreach (var segment in segments)
        {
            folders.Add(segment);
            folders.AddRange(segment.Split(' '));
        }
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return (string.Format(culture, context, path), folders.Distinct().ToList(),
            string.Format(culture, context, fileName));
    }
}
