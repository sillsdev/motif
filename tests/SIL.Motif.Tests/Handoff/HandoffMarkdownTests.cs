using SIL.Motif.Commands.Handoff;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>
/// Pins <c>handoff.md</c>'s own contract (ADR 0045 decision 4): capped at 100 lines — the mechanism that
/// keeps it from becoming the seventeen-file explainer it replaced — and the pasted header's per-run
/// substitution (decision 5).
/// </summary>
public sealed class HandoffMarkdownTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HandoffMarkdownNeverExceedsTheHundredLineCap(bool hasAssessment)
    {
        var markdown = HandoffWriter.BuildHandoffMarkdown(hasAssessment, "example-11111111111111111111111111111111", "mirusi");

        var lineCount = markdown.Split('\n').Length;
        Assert.True(lineCount <= 100, $"handoff.md has {lineCount} lines; the cap is 100.");
    }

    [Fact]
    public void HandoffMarkdownNamesEveryFileItActuallyWrites()
    {
        var markdown = HandoffWriter.BuildHandoffMarkdown(true, "example-key", "mirusi");

        Assert.Contains("`grammar.json`", markdown, StringComparison.Ordinal);
        Assert.Contains("`texts.json`", markdown, StringComparison.Ordinal);
        Assert.Contains("`assessment.json`", markdown, StringComparison.Ordinal);
        Assert.Contains("`handoff.md`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void HandoffMarkdownSaysSoWhenThereIsNoAssessment()
    {
        var markdown = HandoffWriter.BuildHandoffMarkdown(false, "example-key", "mirusi");

        Assert.Contains("No Assessment was run", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("grep '\"mirusi\"' assessment.json", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void HandoffMarkdownsGrepExamplesNameTheSampleKeyAndWord()
    {
        var markdown = HandoffWriter.BuildHandoffMarkdown(true, "example-key", "mirusi");

        Assert.Contains("grep '\"example-key\"' texts.json", markdown, StringComparison.Ordinal);
        Assert.Contains("grep '\"mirusi\"' assessment.json", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPastedHeaderSubstitutesTheLanguageAndProjectNameAndNamesTheStarterQuestions()
    {
        var header = HandoffWriter.BuildPastedHeader("Sena", "Sena Dictionary");

        Assert.Contains("Sena", header, StringComparison.Ordinal);
        Assert.Contains("Sena Dictionary", header, StringComparison.Ordinal);
        Assert.DoesNotContain("{{LANGUAGE_NAME}}", header, StringComparison.Ordinal);
        Assert.DoesNotContain("{{PROJECT_NAME}}", header, StringComparison.Ordinal);
        Assert.DoesNotContain("{{REF}}", header, StringComparison.Ordinal);
        Assert.Contains("Why didn't this word parse?", header, StringComparison.Ordinal);
        Assert.Contains("Why is parsing this so slow, and how do I fix it?", header, StringComparison.Ordinal);
    }
}
