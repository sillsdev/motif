namespace SIL.Motif.Tests.TestFixtures;

public enum HandoffFileVariant
{
    /// <summary>The Handoff includes the retained batch Assessment.</summary>
    Assessed,

    /// <summary>The Handoff has a Baseline but no Assessment or one-word diagnostic.</summary>
    BaselineOnly,

    /// <summary>The Handoff carries only one word's retained diagnostic.</summary>
    OneWordTrace,
}

/// <summary>One independently authored file path and explanation in a Handoff variant.</summary>
/// <param name="RelativePath">The slash-separated path relative to the Handoff folder.</param>
/// <param name="Description">The file's purpose as Help describes it.</param>
public sealed record HandoffFileDefinition(string RelativePath, string Description);

/// <summary>Expected Handoff contents for tests that compare the writer, Help, and upload fixtures.</summary>
public static class HandoffFileExpectations
{
    private static readonly HandoffFileDefinition Grammar = new(
        "grammar.json", "The grammar Motif exports for PanGloss to parse.");
    private static readonly HandoffFileDefinition Intro = new(
        "handoff.md", "Explains the included material and gives reading examples.");
    private static readonly HandoffFileDefinition ParseResults = new(
        "parse-results.json", "One result for each selected word, including its parser outcome and timing.");
    private static readonly HandoffFileDefinition Reader = new(
        "read_results.py", "Queries the Handoff's JSON files from a terminal.");
    private static readonly HandoffFileDefinition Texts = new(
        "texts.json", "The selected Texts with the analyses the project stores.");
    private static readonly HandoffFileDefinition TraceFile = new(
        "traces/<word>.trace.json", "The raw diagnostic for the selected word, including parser fields Motif does not interpret.");

    private static readonly IReadOnlyList<HandoffFileDefinition> AssessedFiles = Array.AsReadOnly(
        new[] { Grammar, Intro, ParseResults, Reader, Texts });
    private static readonly IReadOnlyList<HandoffFileDefinition> BaselineOnlyFiles = Array.AsReadOnly(
        new[] { Grammar, Intro, Reader, Texts });
    private static readonly IReadOnlyList<HandoffFileDefinition> OneWordTraceFiles = Array.AsReadOnly(
        new[] { Grammar, Intro, Reader, Texts, TraceFile });

    /// <summary>Files in a Handoff with a retained Assessment, in writer order.</summary>
    public static IReadOnlyList<HandoffFileDefinition> Assessed => AssessedFiles;

    /// <summary>Files in a Baseline-only Handoff, in writer order.</summary>
    public static IReadOnlyList<HandoffFileDefinition> BaselineOnly => BaselineOnlyFiles;

    /// <summary>Files in a one-word diagnostic Handoff, in writer order.</summary>
    public static IReadOnlyList<HandoffFileDefinition> OneWordTrace => OneWordTraceFiles;

    /// <summary>The raw diagnostic file included in a one-word Handoff.</summary>
    public static HandoffFileDefinition TraceDiagnostic => TraceFile;

    /// <summary>Returns the expected file sequence for one Handoff variant.</summary>
    public static IReadOnlyList<HandoffFileDefinition> For(HandoffFileVariant variant) => variant switch
    {
        HandoffFileVariant.Assessed => AssessedFiles,
        HandoffFileVariant.BaselineOnly => BaselineOnlyFiles,
        HandoffFileVariant.OneWordTrace => OneWordTraceFiles,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };
}
