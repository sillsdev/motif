namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// The <c>pangloss.trace-details.v1</c> document <c>pangloss parse --trace-details</c> writes, around a given
/// tree, with field names and category shapes copied from a real run against the golden grammar.
/// </summary>
internal static class TraceEnvelope
{
    /// <summary>The document for <paramref name="tree"/>; a <see langword="null"/> tree is an untraced word.</summary>
    internal static string Of(
        string signature, string? tree, bool capped = false, bool invalidShape = false, bool guessed = false) =>
        "{\"categories\":{\"lexEntry\":{\"attempts\":1,\"noRoot\":0,\"notApplied\":0,\"outputs\":0," +
        "\"selfElapsedNs\":4800,\"surfaceMismatch\":0,\"timingAvailable\":true,\"uses\":1,\"work\":0}," +
        "\"phonRule\":{\"attempts\":0,\"noRoot\":0,\"notApplied\":0,\"outputs\":0,\"selfElapsedNs\":null," +
        "\"surfaceMismatch\":0,\"timingAvailable\":false,\"uses\":0,\"work\":0}," +
        "\"rootIndex\":{\"attempts\":2,\"noRoot\":1,\"notApplied\":0,\"outputs\":0,\"selfElapsedNs\":null," +
        "\"surfaceMismatch\":0,\"timingAvailable\":false,\"uses\":0,\"work\":11}}," +
        "\"result\":{\"analyses\":[],\"guessed\":" + Bool(guessed) + ",\"signature\":\"" + signature + "\"}," +
        "\"schemaVersion\":\"pangloss.trace-details.v1\"," +
        "\"search\":{\"capped\":" + Bool(capped) + ",\"completed\":" + Bool(!capped && !invalidShape) +
        ",\"elapsedNs\":287600,\"invalidShape\":" + Bool(invalidShape) + ",\"steps\":17,\"timedOut\":false}," +
        "\"trace\":" + (tree ?? "null") + ",\"word\":\"sagd\"}";

    private static string Bool(bool value) => value ? "true" : "false";
}
