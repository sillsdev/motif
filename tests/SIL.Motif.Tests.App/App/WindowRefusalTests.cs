using System.Reflection;
using System.Text.RegularExpressions;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the one way a command's refusal reaches the window: a sentence in the window's words, looked up by
/// code, with the command's own message and facts folded away under Details.
/// </summary>
public sealed class WindowRefusalTests
{
    private static readonly string StorePath = Path.Combine(
        Path.GetTempPath(), "SIL", "Motif", "projects", "one", "Project.motif.db");
    private static readonly string ParserPath = Path.Combine(Path.GetTempPath(), "Motif",
        OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss");

    // The checkbox label ends with a bracketed note; the window text quotes the part before it.
    private static readonly string IncludeLocalDetailsLabel =
        MotifAppStrings.IncludeLocalDetails.Split(" (")[0];

    private static readonly string[] BannedOnScreen = ["Proposal", "Draft", "Preflight", "motif "];

    public static TheoryData<string> CatalogueCodes()
    {
        var data = new TheoryData<string>();
        foreach (var code in AllCatalogueCodes()) data.Add(code);
        return data;
    }

    [Fact]
    public void BudgetStopShowsWordCountsInTheWindow()
    {
        var refusal = new Refusal(RefusalCodes.AssessmentStoppedByBudget, FailureReason.StoppedByBudget,
            "Parsing stopped.", new Dictionary<string, string> { ["wordsDone"] = "3", ["wordsNotDone"] = "5" });
        var shown = WindowRefusal.From(refusal);
        Assert.Contains("3 words done", shown.Sentence);
        Assert.Contains("5 not done", shown.Sentence);
        Assert.False(shown.OffersProblemReport);
    }

    [Theory]
    [MemberData(nameof(CatalogueCodes))]
    public void EveryCodeTheWindowReachesHasItsOwnSentence(string code)
    {
        var shown = WindowRefusal.From(Refusal(code));

        Assert.NotEqual(WindowRefusal.GenericSentence, shown.Sentence);
        Assert.Equal(code, shown.Code);
    }

    [Theory]
    [MemberData(nameof(CatalogueCodes))]
    public void NoSentenceUsesTheCliWords(string code)
    {
        var sentence = WindowRefusal.From(Refusal(code)).Sentence;

        foreach (var banned in BannedOnScreen)
            Assert.DoesNotContain(banned, sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGenericSentenceUsesNoCliWordsEither()
    {
        foreach (var banned in BannedOnScreen)
            Assert.DoesNotContain(banned, WindowRefusal.GenericSentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCodeKeepsItsMessageAndFactsInDetails()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "projects", "one.fwdata");
        var refusal = new Refusal("future.never-mapped", FailureReason.Refused,
            "Something new went wrong in the command.",
            new Dictionary<string, string>
            {
                ["projectPath"] = projectPath,
            });

        var shown = WindowRefusal.From(refusal);

        Assert.Equal(WindowRefusal.GenericSentence, shown.Sentence);
        Assert.True(shown.HasDetails);
        Assert.Contains("Something new went wrong in the command.", shown.Details);
        Assert.Contains("projectPath: one.fwdata", shown.Details);
    }

    [Fact]
    public void APathFactShowsItsFileNameInTheWindowAndItsFullPathInTheReport()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "projects", "one", "one.fwdata");
        var refusal = new Refusal("future.never-mapped", FailureReason.Refused,
            "Something new went wrong in the command.",
            new Dictionary<string, string> { ["projectPath"] = projectPath });

        var shown = WindowRefusal.From(refusal);

        Assert.Contains("projectPath: one.fwdata", shown.Details);
        Assert.DoesNotContain(projectPath, shown.Details);
        Assert.Equal(projectPath, shown.Facts["projectPath"]);
        Assert.Contains(projectPath, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
        Assert.DoesNotContain(projectPath, ProblemReport.FromRefusal(shown).ToText());
    }

    [Fact]
    public void RefusalMessageReplacesKnownPathAndDirectoryBeforeWindowFiltering()
    {
        const string path = "/private/Secret Folder/project.fwdata";
        const string message = "Cannot read '/private/Secret Folder/project.fwdata'. Check '/private/Secret Folder'.";
        var shown = WindowRefusal.From(new Refusal("future.never-mapped", FailureReason.Refused, message,
            new Dictionary<string, string> { ["projectPath"] = path }));

        Assert.Contains("Cannot read 'project.fwdata'. Check 'project.fwdata'.", shown.Details);
        Assert.Contains(message, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void MissingParserNamesTheFileButNoFolderAndPointsAtTheReport()
    {
        const string message = "Could not find PanGloss. Looked for it at: /opt/motif/pangloss.";
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused,
            message, new Dictionary<string, string>
            {
                [RefusalFactNames.ParserNotFound] = "true", ["projectPath"] = "/opt/motif/project.fwdata",
            }));

        Assert.Contains(PanGlossExecutable.ExecutableFileName, shown.Details);
        Assert.Contains(MotifAppStrings.ReportProblem, shown.Details);
        Assert.Contains(IncludeLocalDetailsLabel, shown.Details);
        Assert.DoesNotContain("Looked for it at", shown.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("/opt/motif", shown.Details);
        Assert.Contains("projectPath: project.fwdata", shown.Details);
        Assert.Contains(message, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void TheCopiedReportOfAMissingParserHoldsEverySearchedPlaceOnceLocalDetailsAreIncluded()
    {
        var first = Path.Combine(Path.GetTempPath(), "PanGloss", "dist", "<version>", PanGlossExecutable.ExecutableFileName);
        var second = Path.Combine(Path.GetTempPath(), "Motif", PanGlossExecutable.ExecutableFileName);
        var refusal = new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused,
            PanGlossExecutable.NotFoundMessageFor([first, second]),
            new Dictionary<string, string> { [RefusalFactNames.ParserNotFound] = "true" });
        var report = ProblemReport.FromRefusal(WindowRefusal.From(refusal));

        var withDetails = report.ToText(includeLocalDetails: true);
        Assert.Contains($"Looked for it at: {first}; {second}.", withDetails, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), report.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownDriveRootDoesNotReplaceAnUnrelatedDriveReference()
    {
        const string message = "The C: drive is unavailable.";
        var shown = WindowRefusal.From(new Refusal("future.never-mapped", FailureReason.Refused, message,
            new Dictionary<string, string> { ["projectPath"] = @"C:\project.fwdata" }));

        Assert.Contains(message, shown.Details);
        Assert.Contains(@"C:\project.fwdata", ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Theory]
    [InlineData(@"private\alice\project.fwdata")]
    [InlineData(@"C:private\alice\project.fwdata")]
    public void BackslashPathFactsShowOnlyTheFileNameOnEveryPlatform(string path)
    {
        var shown = WindowRefusal.From(new Refusal("future.never-mapped", FailureReason.Refused,
            "The project could not be read.", new Dictionary<string, string> { ["projectPath"] = path }));

        Assert.Contains("projectPath: project.fwdata", shown.Details);
        Assert.DoesNotContain(path, shown.Details);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void DetailsDropTheCommandsCliRemedyButKeepItsReason()
    {
        var refusal = new Refusal(RefusalCodes.SelectionDefaultMissing, FailureReason.Refused,
            "No default Selection is saved for this project. Save one with `motif selection set-default` first.");

        var shown = WindowRefusal.From(refusal);

        Assert.Contains("No default Selection is saved for this project.", shown.Details);
        Assert.DoesNotContain("motif ", shown.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("set-default", shown.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailsDropAQuotedCommandLineWithOptions()
    {
        var refusal = new Refusal("future.never-mapped", FailureReason.Refused,
            "The job is still running. Check again with 'jobs show 7 --project <fwdata>'.");

        var shown = WindowRefusal.From(refusal);

        Assert.Equal("The job is still running.", shown.Details);
    }

    [Fact]
    public void ARefusalWithNothingLeftToShowHasNoDetails()
    {
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.SelectionDefaultMissing, FailureReason.Refused,
            "Save one with `motif selection set-default` first."));

        Assert.False(shown.HasDetails);
        Assert.Null(shown.Details);
    }

    [Theory]
    [InlineData(RefusalCodes.AssessParserUnavailable)]
    [InlineData(RefusalCodes.StatsParserUnavailable)]
    [InlineData(RefusalCodes.GrammarCheckParserUnavailable)]
    [InlineData(RefusalCodes.WordTraceParserUnavailable)]
    [InlineData(RefusalCodes.HandoffParserUnavailable)]
    public void AMissingParserSaysSoAndTheDetailsNameTheFileAndPointAtTheReport(string code)
    {
        var refusal = new Refusal(code, FailureReason.Refused,
            $"Could not find the pangloss executable. Looked for it at: {ParserPath}. " +
            "Build it with `cargo build --release -p pg-cli` in the PanGloss checkout, or set " +
            "MOTIF_PANGLOSS_EXE to its path.",
            new Dictionary<string, string> { [RefusalFactNames.ParserNotFound] = "true" });

        var shown = WindowRefusal.From(refusal);

        Assert.Equal(
            "Motif could not find PanGloss, so it cannot measure words. Install PanGloss beside Motif, then try again.",
            shown.Sentence);
        Assert.Contains(Path.GetFileName(ParserPath), shown.Details);
        Assert.True(shown.OffersProblemReport);
        Assert.Contains(MotifAppStrings.ReportProblem, shown.Details);
        Assert.Contains(IncludeLocalDetailsLabel, shown.Details);
        Assert.DoesNotContain(ParserPath, shown.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetDirectoryName(ParserPath)!, shown.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Looked for it at", shown.Details, StringComparison.Ordinal);
        Assert.Contains(ParserPath, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
        Assert.DoesNotContain("cargo", shown.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(RefusalFactNames.ParserNotFound, shown.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoreFromAnotherVersionNamesTheFileAndKeepsAStableCode()
    {
        var refusal = new Refusal(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
            $"The Motif project database at '{Path.GetFileName(StorePath)}' is schema 3, but this build requires exactly schema 4.",
            new Dictionary<string, string>
            {
                ["fwDataPath"] = @"C:\projects\one.fwdata",
                ["storePath"] = StorePath,
            });

        var shown = WindowRefusal.From(refusal);

        Assert.Equal("This Motif Store Is from Another Version", shown.Heading);
        Assert.Equal("Motif cannot open this store. Delete it and reopen the project to continue. " +
            "Changes not applied yet are lost; your FieldWorks project is not touched.", shown.Sentence);
        Assert.Contains(Path.GetFileName(StorePath), shown.Details);
        Assert.DoesNotContain(StorePath, shown.Details);
        Assert.Contains(StorePath, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
        Assert.Equal(RefusalCodes.StoreOtherVersion, shown.Code);
        Assert.Equal(StorePath, shown.Facts["storePath"]);
    }

    [Fact]
    public void MissingProjectInitializationShowsTheNameAndReportsThePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "private \"folder", "language's file.fwdata");
        var result = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(path, ProjectInitializationCommand.ConfirmationPhrase), Path.GetTempPath());
        var refusal = Assert.IsType<Refusal>(result.Refusal);
        var shown = WindowRefusal.From(refusal);

        Assert.Contains("language's file.fwdata", shown.Details);
        Assert.DoesNotContain(path, shown.Details);
        Assert.Equal(path, refusal.Facts["fwDataPath"]);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Theory]
    [InlineData("words")]
    [InlineData("trace")]
    public void UnreadableFileRefusalShowsTheNameAndReportsThePath(string kind)
    {
        var path = Path.Combine(Path.GetTempPath(), "private \"folder", "language's file.txt");
        var refusal = kind == "words"
            ? WordListFile.Resolve(null, path).Refusal
            : WordTraceQuery.Load(new WordTraceLoadRequest(path)).Refusal;
        var shown = WindowRefusal.From(Assert.IsType<Refusal>(refusal));

        Assert.Contains("language's file.txt", shown.Details);
        Assert.DoesNotContain(path, shown.Details);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Theory]
    [InlineData("/private/a\"b/sub/language's file.fwdata")]
    [InlineData(@"C:\private user's folder\file.txt")]
    [InlineData(@"\\server\private folder\file.txt")]
    [InlineData("relative folder/a'b/file.txt")]
    public void UnstructuredExceptionTextStaysInTheProblemReport(string path)
    {
        var exception = new IOException($"Could not read '{path}'.");
        var shown = WindowRefusal.Failure(WindowRefusal.OpenFailedCode, "Motif could not open this project.", exception);

        Assert.DoesNotContain(path, shown.Details ?? string.Empty);
        Assert.DoesNotContain("private", shown.Details ?? string.Empty);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void ALongUnterminatedExceptionMessageIsNotParsedIntoWindowDetails()
    {
        var message = "Could not read '/private/" + new string('a', 100000);
        var shown = WindowRefusal.Failure(WindowRefusal.OpenFailedCode, "Motif could not open this project.",
            new IOException(message));

        Assert.DoesNotContain("/private/", shown.Details ?? string.Empty);
        Assert.Contains(message, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void FileNotFoundExceptionKeepsThePathAsAReportFact()
    {
        var path = Path.Combine(Path.GetTempPath(), "private \"folder", "language's file.txt");
        var shown = WindowRefusal.Failure(WindowRefusal.OpenFailedCode, "Motif could not open this project.",
            new FileNotFoundException("File not found.", path));

        Assert.Contains("language's file.txt", shown.Details);
        Assert.DoesNotContain(path, shown.Details);
        Assert.Equal(path, shown.Facts["filePath"]);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Theory]
    [InlineData("/private/a\"b/sub/language's file.fwdata")]
    [InlineData(@"C:\private user's folder\file.txt")]
    [InlineData(@"\\server\private folder\file.txt")]
    [InlineData("relative folder/a'b/file.txt")]
    public void UnstructuredRefusalTextWithAPathIsOnlyInTheProblemReport(string path)
    {
        var refusal = new Refusal("future.failure", FailureReason.Refused, $"Could not read '{path}'.");
        var shown = WindowRefusal.From(refusal);

        Assert.Null(shown.Details);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void UnterminatedRefusalPathIsNotParsedIntoWindowDetails()
    {
        var message = "Could not read '/private/" + new string('a', 100000);
        var shown = WindowRefusal.From(new Refusal("future.failure", FailureReason.Refused, message));

        Assert.Null(shown.Details);
        Assert.Contains(message, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("activity")]
    [InlineData("create")]
    public void OtherPathRefusalsShowTheNameAndReportThePath(string kind)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "language's file");
        var refusal = kind switch
        {
            "tag" => ProjectTagCommands.Read(new ReadProjectTagRequest(path), Path.GetTempPath()).Refusal,
            "activity" => ActivityQuery.Read(new ActivityRequest(path)).Refusal,
            _ => ProjectCreateCommand.Create(new ProjectCreateRequest(path, "New", "swh")).Refusal,
        };
        var shown = WindowRefusal.From(Assert.IsType<Refusal>(refusal));

        Assert.Contains("language's file", shown.Details);
        Assert.DoesNotContain(path, shown.Details);
        Assert.Contains(path, ProblemReport.FromRefusal(shown).ToText(includeLocalDetails: true));
    }

    [Fact]
    public void ACorruptProjectStoreHasManualRecoveryWithoutOfferingUnverifiedDeletion()
    {
        var shown = WindowRefusal.From(new Refusal(RefusalCodes.StoreInconsistent,
            FailureReason.StoreInconsistent, "The project store is corrupt."));

        Assert.Contains("Close every Motif", shown.Sentence, StringComparison.Ordinal);
        Assert.Contains("keep a copy", shown.Sentence, StringComparison.Ordinal);
        Assert.Contains("beside the project's .fwdata", shown.Sentence, StringComparison.Ordinal);
        Assert.Contains("Changes not applied yet are lost", shown.Sentence, StringComparison.Ordinal);
        Assert.Contains("FieldWorks project is not touched", shown.Sentence, StringComparison.Ordinal);
        Assert.False(shown.OffersStoreDeletion);
    }

    [Fact]
    public void AWindowCheckIsASentenceWithNoDetails()
    {
        var shown = WindowRefusal.Plain("Choose at least one text or add a word before continuing.");

        Assert.Equal("Choose at least one text or add a word before continuing.", shown.Sentence);
        Assert.False(shown.HasDetails);
    }

    [Fact]
    public void ReconfirmUnneededHasItsOwnSentence()
    {
        var shown = WindowRefusal.From(Refusal(RefusalCodes.ChangeReconfirmUnneeded));

        Assert.Equal("This change does not need another check.", shown.Sentence);
    }

    [Fact]
    public void AnalysisMarkingRefusalsAskForAParseAndUseSelectionTerms()
    {
        var parse = WindowRefusal.From(Refusal(RefusalCodes.ChangeAssessmentKind)).Sentence;
        var scope = WindowRefusal.From(Refusal(RefusalCodes.ChangeScopeInvalid)).Sentence;
        var missingWord = WindowRefusal.From(Refusal(RefusalCodes.ChangeWordformMissing)).Sentence;

        Assert.Equal("Accept the new set needs every word parsed. Parse all words first.", parse);
        Assert.Equal("Choose one wordform, one Selection, or one Text.", scope);
        Assert.DoesNotContain("Assessment selection", scope, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("That word isn't in the FieldWorks project. Refresh and try again.", missingWord);
    }

    [Fact]
    public void EveryCatalogueCodeIsOneACommandOrTheWindowDeclares()
    {
        var root = RepoPaths.FindRepoRoot();
        var codePattern = new Regex(@"""([a-z][a-z]*(?:\.[a-z][a-z-]*)+)""");
        var referencePattern = new Regex(@"RefusalCodes\.(\w+)");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in new[] { "SIL.Motif.Commands", "SIL.Motif.Runner", "SIL.Motif.App" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs",
                     SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in codePattern.Matches(source)) declared.Add(match.Groups[1].Value);
            foreach (Match match in referencePattern.Matches(source))
                if (typeof(RefusalCodes).GetField(match.Groups[1].Value)?.GetRawConstantValue() is string code)
                    declared.Add(code);
        }

        Assert.DoesNotContain(AllCatalogueCodes(), code => !declared.Contains(code));
    }

    private static Refusal Refusal(string code) => new(code, FailureReason.Refused, "The command's own sentence.",
        new Dictionary<string, string> { ["storePath"] = StorePath });

    private static IEnumerable<string> AllCatalogueCodes() => RefusalCatalog.Specs.Values
        .Where(spec => spec.ShownInWindow)
        .Select(spec => spec.Code)
        .Order(StringComparer.Ordinal);
}
