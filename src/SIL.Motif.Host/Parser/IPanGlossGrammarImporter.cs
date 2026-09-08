namespace SIL.Motif.Host.Parser;

/// <summary>
/// Produces PanGloss's grammar snapshot from a saved <c>.fwdata</c> file.
/// </summary>
/// <remarks>
/// This is the whole of Motif's dependency on <c>pangloss import</c>: hand it a project file and an output
/// path, wait, and trust only the bytes it wrote — not its exit code — because a snapshot's absence is a
/// failure regardless of what the process reported. The interface exists so a caller composing a Baseline
/// or a Handoff can be exercised without launching anything; <see cref="PanGlossGrammarImportProcess"/> is
/// the implementation that really launches the parser.
/// </remarks>
public interface IPanGlossGrammarImporter
{
    /// <summary>
    /// Imports the grammar found in <paramref name="fwDataPath"/> and writes it to
    /// <paramref name="grammarJsonPath"/>. When <paramref name="governor"/> is supplied, the launched
    /// process is contained by it immediately after starting; a caller with no governor passes null and the
    /// process runs uncontained.
    /// </summary>
    Task ImportAsync(
        string fwDataPath, string grammarJsonPath, CancellationToken cancellationToken,
        IParserProcessGovernor? governor = null);
}
