using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Pins <c>motif inspect</c> on the command line: arguments that name no subject or two, and the text and JSON it
/// prints, where every section says whether it was read and, if not, why.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class InspectArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _managedRoot = Path.Combine(
        Path.GetTempPath(), "motif-inspect-argv-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Plural")]
    [InlineData("phon_rule:")]
    [InlineData(":rule-id")]
    public async Task TimingRequiresAnExplicitKindAndKey(string address)
    {
        var result = await CliProcess.RunAsync(_managedRoot, null, true,
            "timing", "--project", "x.fwdata", "--by", "rule", "--rule", address);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[--rule <kind>:<key>]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("opaque-rule-key", false, "unknown")]
    [InlineData("6f1d2c3b-0000-4000-8000-000000000001", false, "authored")]
    [InlineData("6f1d2c3b-0000-4000-8000-000000000001", true, "structural")]
    public async Task RuleIdentityQualityUsesAGuidOrTheExplicitStructuralFlag(string key, bool structural, string expected)
    {
        var project = pristine.CopyProjectFile();
        var arguments = new List<string> { "inspect", "--project", project, "--rule", "phon_rule:" + key, "--json" };
        if (structural) arguments.Add("--structural");

        var result = await CliProcess.RunAsync(_managedRoot, null, true, arguments.ToArray());

        Assert.True(result.ExitCode == 0, result.FailureDetails);
        Assert.Equal(expected, ProjectionJson.Deserialize<InspectResponse>(result.Output)!.Subject.IdentityQuality);
    }

    [Theory]
    [InlineData("inspect", "--allomorph", "6f1d2c3b-0000-4000-8000-000000000001")]
    [InlineData("inspect", "--project", "x.fwdata")]
    [InlineData("inspect", "--project", "x.fwdata", "--rule", "phon_rule")]
    [InlineData("inspect", "--project", "x.fwdata", "--allomorph", "a", "--slot", "b")]
    [InlineData("inspect", "--project", "x.fwdata", "--slot")]
    [InlineData("inspect", "--project", "x.fwdata", "--slot", "b", "extra")]
    public async Task InvalidInspectArgumentsPrintTheUsageLine(params string[] arguments)
    {
        var result = await CliProcess.RunAsync(_managedRoot, null, true, arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif inspect", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectTextNamesEachSectionAndWhyOneIsMissing()
    {
        var response = Mixed() with
        {
            Uses = InspectorSection<ObjectUseWords>.Of(new ObjectUseWords([], []) { NotCountingDisapproved = 1 }),
            Warnings = InspectorSection<IReadOnlyList<GrammarWarning>>.Of(
            [
                new GrammarWarning(GrammarDiagnosticLevel.Warning, "Unsegmentable form", [], [], "Unsegmentable form")
                    { Group = "Allomorph can't be split into phonemes", Description = "Its first letter matches no phoneme." },
            ]),
        };

        var text = SIL.Motif.Cli.Rendering.CommandTextRenderer.Render(CommandOutcome<InspectResponse>.Success(response),
            asJson: false).Output;

        Assert.Equal(string.Join(Environment.NewLine,
            "Inspect morpheme kat: the names given belong to different FieldWorks objects",
            "  In FieldWorks: absent (The names given belong to different FieldWorks objects, so Motif shows neither.)",
            "  Your words that use it: 0 words",
            "    Not counting 1 disapproved",
            "  Words it ran in: absent (Motif can't tell which object PanGloss times this under.)",
            "  Warnings that name it: 1",
            "    Allomorph can't be split into phonemes: Its first letter matches no phoneme.",
            ""), text);
    }

    [Fact]
    public void InspectJsonSpellsEveryStatusAndKindAsAStableToken()
    {
        var json = SIL.Motif.Cli.Rendering.CommandTextRenderer.Render(CommandOutcome<InspectResponse>.Success(Mixed()),
            asJson: true).Output;

        Assert.Contains("\"kind\": \"morpheme\"", json, StringComparison.Ordinal);
        Assert.Contains("\"resolution\": \"contradictory\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"absent\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"not_requested\"", json, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_managedRoot)) Directory.Delete(_managedRoot, recursive: true);
    }

    private static InspectResponse Mixed() => new(
        InspectorSubject.Morpheme("6f1d2c3b-0000-4000-8000-000000000001", "6f1d2c3b-0000-4000-8000-000000000002", "kat")!,
        InspectorResolution.Contradictory)
    {
        Facts = InspectorSection<ObjectFacts>.Not(InspectorSectionStatus.Absent,
            "The names given belong to different FieldWorks objects, so Motif shows neither."),
        RanIn = InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Absent,
            "Motif can't tell which object PanGloss times this under."),
    };
}
