namespace SIL.Motif.App.GuideQueries;

/// <summary>Which grammar a Guide's query tables read: the Baseline alone, or a Proposal's two sides.</summary>
/// <param name="ProposalId">The Proposal the tables belong to, or null in the Grammar Guide, which reads the Baseline.</param>
public sealed record GuideQueryContext(string? ProposalId = null)
{
    /// <summary>The Baseline alone: the Grammar Guide's context.</summary>
    public static GuideQueryContext Baseline { get; } = new();

    /// <summary>Whether the context shows both sides.</summary>
    public bool BothSides => ProposalId is not null;

    /// <summary>What <c>--as-of</c> names: <c>baseline</c>, or the Proposal's id.</summary>
    public string AsOf => ProposalId ?? "baseline";
}

/// <summary>One run of a Guide query: the command line with the side the page chose, and the project it reads.</summary>
/// <param name="ProjectPath">The project the query reads.</param>
/// <param name="Line">The whole <c>motif query ...</c> line, with <c>--as-of</c> and, for a Proposal, <c>--sides both</c>.</param>
public sealed record GuideQueryRunRequest(string ProjectPath, string Line);

/// <summary>Turns a stored run line into the line a run executes for a context, or says why it cannot run.</summary>
public static class GuideQueryRunLines
{
    /// <summary>The line to run for <paramref name="run"/> in <paramref name="context"/>, or a message when it cannot run.</summary>
    public static (string? Line, string? Refusal) Prepare(string run, GuideQueryContext context)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);
        if (!SIL.Motif.Contract.Query.QueryRunLine.TryParseRun(run, out var line, out var refusal)) return (null, refusal.Message);
        if (line.Verb != SIL.Motif.Contract.Query.QueryRunLine.QueryVerb) return (null, MotifAppStrings.GuideQueryGroupLine);
        if (line.AsOf is not null) return (null, MotifAppStrings.GuideQueryOwnAsOf);
        var asOf = context.AsOf.Any(char.IsWhiteSpace) ? "\"" + context.AsOf + "\"" : context.AsOf;
        return (run.Trim() + " --as-of " + asOf + (context.BothSides ? " --sides both" : ""), null);
    }
}
