using Xunit;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.Tests.App;

/// <summary>Query answers in the shape of the query command's sides, version 1, and a Guide section's blocks.</summary>
internal static class GuideQueryTestSupport
{
    internal static QuerySideRow Word(string id, string form, string state, string cut = "", string gloss = "", bool approved = true) =>
        new(new QueryForm(id, form, "", "alternate", "e1", "", "", [], ["n"], 3),
            new QueryWordParse(state, 2, cut.Length == 0 ? [] :
                [new QueryReading("n", approved, cut.Split('-').Zip(gloss.Split('-'))
                    .Select(pair => new QueryMorph(pair.First, pair.Second, "n", "e1")).ToArray())]));

    internal static QueryPairedRow Row(string id, QuerySideRow? baseline, QuerySideRow? proposal) => new(id, baseline, proposal);

    internal static FormsQueryResponse Sides(string mode, string baselineStatus, string proposalStatus, string proposalReason,
        params QueryPairedRow[] rows)
    {
        var states = new List<QuerySideState> { new(QuerySideNames.Baseline, baselineStatus, "baseline", null, null) };
        if (mode != "baseline")
            states.Add(new QuerySideState(QuerySideNames.Proposal, proposalStatus, "prop-1", null, proposalReason.Length > 0 ? proposalReason : null));
        return Response(new QuerySidesResult(QuerySidesResult.CurrentVersion, mode, states, rows), []);
    }

    internal static FormsQueryResponse Response(QuerySidesResult? sides, IReadOnlyList<QueryGroup> groups) =>
        new("", "", "", false, 0, new QueryResult([], groups, [])) { Sides = sides };

    /// <summary>A Baseline-only answer of three words.</summary>
    internal static FormsQueryResponse BaselineAnswer() => Sides("baseline", "ready", "", "",
        Row("kaonan", Word("kaonan", "kaonan", "approved", "ka-onan", "NOM-eat"), null),
        Row("baha", Word("baha", "baha", "unparsed"), null),
        Row("alikula", Word("alikula", "alikula", "parsed", "a-li-kula", "3S-PST-eat"), null));

    /// <summary>A Proposal answer that read both sides.</summary>
    internal static FormsQueryResponse BothAnswer() => Sides("both", "ready", "ready", "",
        Row("kaonan", Word("kaonan", "kaonan", "approved", "ka-onan", "NOM-eat"), Word("kaonan", "kaonan", "approved", "ka-on-an", "NOM-eat-IPFV")),
        Row("baha", Word("baha", "baha", "unparsed"), Word("baha", "baha", "parsed", "ba-ha", "x-y")),
        Row("zuri", Word("zuri", "zuri", "parsed", "zuri", "good"), null));

    internal static FormsQueryResponse PendingAnswer() => Sides("both", "ready", "pending", "no-trial",
        Row("kaonan", Word("kaonan", "kaonan", "approved", "ka-onan", "NOM-eat"), null),
        Row("baha", Word("baha", "baha", "unparsed"), null));

    internal static GuideQueryRun Read(FormsQueryResponse response)
    {
        var (run, problem) = GuideQueryRun.From(response);
        Assert.True(run is not null, problem);
        return run;
    }

    internal static CommandOutcome<FormsQueryResponse> Answer(FormsQueryResponse response) => CommandOutcome<FormsQueryResponse>.Success(response);

    internal static readonly string[] SectionLines =
    [
        "How it works",
        "```motif-query",
        "id: alternates",
        "title: Alternate forms",
        "run: motif query forms --where kind=alternate",
        "```",
        "```motif-show",
        "query: alternates",
        "show: table",
        "```",
        "After",
    ];
}

/// <summary>A shown block for views that need a model to build.</summary>
internal static class GuideQueryFixture
{
    internal static GuideQueryBlockViewModel Block()
    {
        var tables = new GuideQueryTablesViewModel(GuideQueryContext.Baseline, () => "/p",
            (_, _) => Task.FromResult(GuideQueryTestSupport.Answer(GuideQueryTestSupport.BaselineAnswer())), null);
        tables.Show(GuideQueryDocument.From(GuideQueryTestSupport.SectionLines));
        return tables.Blocks[0];
    }
}
