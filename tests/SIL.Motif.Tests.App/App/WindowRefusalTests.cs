using System.Reflection;
using System.Text.RegularExpressions;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
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

    private static readonly string[] BannedOnScreen = ["Proposal", "Draft", "Preflight", "motif "];

    public static TheoryData<string> CatalogueCodes()
    {
        var data = new TheoryData<string>();
        foreach (var code in AllCatalogueCodes()) data.Add(code);
        return data;
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
        Assert.Contains(projectPath, shown.Details);
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
    public void AMissingParserSaysSoAndTheDetailsNameThePlacesMotifLooked(string code)
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
        Assert.Contains(ParserPath, shown.Details);
        Assert.DoesNotContain("cargo", shown.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(RefusalFactNames.ParserNotFound, shown.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoreFromAnotherVersionNamesTheFileAndKeepsAStableCode()
    {
        var refusal = new Refusal(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
            $"The Motif project database at '{StorePath}' is schema 3, but this build requires exactly schema 4.",
            new Dictionary<string, string>
            {
                ["fwDataPath"] = @"C:\projects\one.fwdata",
                ["storePath"] = StorePath,
            });

        var shown = WindowRefusal.From(refusal);

        Assert.Equal(
            $"Motif's file for this project, {StorePath}, was made by a different version of Motif. Close Motif, " +
            "delete that file, and open the project again. Changes not applied yet are lost; your FieldWorks " +
            "project is not touched.",
            shown.Sentence);
        Assert.Equal(RefusalCodes.StoreOtherVersion, shown.Code);
        Assert.Equal(StorePath, shown.Facts["storePath"]);
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
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in new[] { "SIL.Motif.Commands", "SIL.Motif.App" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs",
                     SearchOption.AllDirectories))
        foreach (Match match in codePattern.Matches(File.ReadAllText(file)))
            declared.Add(match.Groups[1].Value);

        Assert.DoesNotContain(AllCatalogueCodes(), code => !declared.Contains(code));
    }

    private static Refusal Refusal(string code) => new(code, FailureReason.Refused, "The command's own sentence.",
        new Dictionary<string, string> { ["storePath"] = StorePath });

    private static IEnumerable<string> AllCatalogueCodes() => typeof(RefusalCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!);
}
