using System.Reflection;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>
/// Pins the Handoff's one surviving static asset — the pasted-header template — now that
/// <c>reference/</c> and its embedded copies of <c>grammar-format.md</c>, <c>flextext-json-format.md</c>
/// and <c>hc-mechanics.md</c> are gone (ADR 0045 decision 6): it is embedded in
/// <see cref="SIL.Motif.Commands"/>, its links resolve as raw Markdown rather than a rendered GitHub page,
/// and no asset's prose leaks a path local to whichever machine wrote it.
/// </summary>
public sealed class HandoffAssetsTests
{
    private static readonly Assembly CommandsAssembly = typeof(CommandCatalog).Assembly;

    private const string StarterPromptResource = "SIL.Motif.Commands.Handoff.Assets.starter-prompt.md";

    [Fact]
    public void TheStarterPromptTemplateIsEmbeddedInCommands()
    {
        Assert.Contains(StarterPromptResource, CommandsAssembly.GetManifestResourceNames());
    }

    [Fact]
    public void TheStarterPromptTemplateNamesTheTwoStarterQuestionsAndItsPlaceholders()
    {
        var template = ReadEmbeddedText(StarterPromptResource);

        Assert.Contains("Why didn't this word parse?", template, StringComparison.Ordinal);
        Assert.Contains("Why is parsing this so slow, and how do I fix it?", template, StringComparison.Ordinal);
        Assert.Contains("{{LANGUAGE_NAME}}", template, StringComparison.Ordinal);
        Assert.Contains("{{PROJECT_NAME}}", template, StringComparison.Ordinal);
    }

    // A `blob` URL serves rendered HTML, not the Markdown the header promises (ADR 0045's own defect note).
    [Fact]
    public void TheStarterPromptTemplateLinksRawGitHubRatherThanBlobUrls()
    {
        var template = ReadEmbeddedText(StarterPromptResource);

        Assert.Contains("raw.githubusercontent.com/sillsdev/motif/", template, StringComparison.Ordinal);
        Assert.Contains("raw.githubusercontent.com/sillsdev/PanGloss/", template, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com/sillsdev/motif/blob", template, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com/sillsdev/PanGloss/blob", template, StringComparison.Ordinal);
    }

    [Fact]
    public void NoAssetProseLeaksARepoLocalPath()
    {
        var text = ReadEmbeddedText(StarterPromptResource);

        // A local path only resolves on the machine that wrote it, never on whatever reads the Handoff.
        Assert.False(WindowsAbsolutePath.IsMatch(text), $"{StarterPromptResource} contains a Windows absolute path.");
        Assert.False(text.Contains("/Users/", StringComparison.Ordinal), $"{StarterPromptResource} contains a /Users/ path.");
        Assert.False(text.Contains("/home/", StringComparison.Ordinal), $"{StarterPromptResource} contains a /home/ path.");
    }

    // A URL scheme like "https:" also matches letter-colon-slash; exclude a colon preceded by a letter.
    private static readonly Regex WindowsAbsolutePath = new(@"(?<![A-Za-z])[A-Za-z]:[\\/]", RegexOptions.Compiled);

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = CommandsAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
