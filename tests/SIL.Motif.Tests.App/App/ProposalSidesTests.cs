using System.Collections;
using System.Text.RegularExpressions;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that in Proposal mode every number a page draws belongs to the side it is drawn for. One sample gives today and
/// the Proposal no value in common; Overview, Texts, Timing, Warnings and Review are read in the Today, Today to
/// Proposal and Proposal views, and a number from the wrong side, a pair drawn the wrong way round, or a flip that reads
/// anything fails it.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalSidesTests
{
    private const string ProposalId = "proposal/one";

    // Every ordered pair of views is visited once, and each view is drawn again later.
    private static readonly MarkupView[] Flips =
    [
        MarkupView.Today, MarkupView.Proposal, MarkupView.TodayToProposal, MarkupView.Proposal, MarkupView.Today,
        MarkupView.TodayToProposal,
    ];

    // Which side's numbers a text may carry: the view's own, one side whatever the view, or both in a pair.
    private enum Follows { View, TodayAlways, ProposalAlways, PairedAlways, NamedWithProposal, ListChipName }

    private sealed record Drawn(string Name, string Text, Follows Rule = Follows.View);

    private sealed record Numbers(string[] Today, string[] Proposal, string[] Neither)
    {
        public bool IsToday(string number) => Today.Contains(number);

        public bool IsProposal(string number) => Proposal.Contains(number);

        public bool IsNeither(string number) => Neither.Contains(number);
    }

    // Both sides hold the same twelve words; every count, place count and work total differs between them.
    private static AssessmentWordResult Word(int index, string outcome) => new("w" + index, outcome,
        outcome == "timed-out", outcome == "timed-out" ? "TimedOut" : "Complete", 1, null);

    private static readonly string[] TodayOutcomes =
    [
        .. Enumerable.Repeat("no-analysis", 6), "timed-out", .. Enumerable.Repeat("unassessed", 5),
    ];

    private static readonly string[] ProposalOutcomes =
    [
        .. Enumerable.Repeat("no-analysis", 3), .. Enumerable.Repeat("timed-out", 7), .. Enumerable.Repeat("unassessed", 2),
    ];

    private static readonly int[] Places = [3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41];

    private static AssessmentWordResult[] Words(string[] outcomes) => [.. outcomes.Select((outcome, index) => Word(index, outcome))];

    private static readonly TimingProposalViewTests.Word[] TodayTiming =
        [new("a", 900), new("b", 1_900, true), new("c", 2_500, true), new("d", 4_700)];

    private static readonly TimingProposalViewTests.Word[] ProposalTiming =
        [new("a", 1_100), new("b", 2_300), new("c", 3_700), new("d", 5_900, true)];

    private static GrammarCheckResponse Check(string prefix, int errors, int warnings, int information) => new(
    [
        .. Enumerable.Range(0, errors).Select(index => WarningsProposalViewTests.Finding($"{prefix}e{index}", GrammarDiagnosticLevel.Error)),
        .. Enumerable.Range(0, warnings).Select(index => WarningsProposalViewTests.Finding($"{prefix}w{index}", GrammarDiagnosticLevel.Warning)),
        .. Enumerable.Range(0, information).Select(index => WarningsProposalViewTests.Finding($"{prefix}i{index}", GrammarDiagnosticLevel.Information)),
    ], true);

    private static OverviewResponse TodayOverview()
    {
        var today = OverviewPageWordsTests.Populated();
        return today with
        {
            Warnings = today.Warnings! with { Count = 27, ErrorCount = 2, WarningCount = 19, InformationCount = 6 },
        };
    }

    private static OverviewResponse ProposalOverview()
    {
        var today = OverviewPageWordsTests.Populated();
        return today with
        {
            SelectionWordCount = 150,
            AssessmentElapsedSeconds = 44,
            WordCoveragePercent = 87.3,
            TextCoverage = today.TextCoverage with { ParsedWords = 131, TotalOccurrences = 640, OccurrenceCoveragePercent = 91.7 },
            Accuracy = today.Accuracy with { ApprovedWordsKept = 66, ApprovedWordCount = 91 },
            Timing = today.Timing with { Work = new WordWorkSummary(150, 479_000, 77, 505) },
            Warnings = today.Warnings! with
            {
                Count = 18, ErrorCount = 1, WarningCount = 13, InformationCount = 4,
                YourWords = new WarningWordsTouched(9, 5, []) { BySpellingOnly = 1 },
            },
        };
    }

    private static TrialRecord Trial() => new("job/trial", ProposalId, "sha256:intent", OverviewProposalViewTests.Token, null,
        new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false, null,
        JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current)
    {
        Work = new TrialWork(150, 380_000, 479_000, 26.0, [], 0, 0),
        WordResults = Words(ProposalOutcomes),
    };

    private static GrammarChangeEntry Rule(string key, int ordinal, string before, string after) =>
        new(key, ordinal, "Title " + key, ["op-" + key], [], "digest-" + key, false, "Composer", "Adds a rule",
            [new EffectView("id-" + key, "Allomorphs", [new EffectChange("swh", before, after)])], [], []);

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, OverviewPageModel Overview,
        TextsPageModel Texts, TimingPageModel Timing, WarningsPageModel Warnings, ReviewPageModel Review)> OpenedAsync()
    {
        var fake = new FakeCommandClient();
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? TodayOverview() : ProposalOverview())));
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            request.ProposalId is null
                ? TimingProposalViewTests.Respond(request, TodayTiming, ("morph_rule", 3), ("affix", 2))
                : TimingProposalViewTests.Respond(request, ProposalTiming, ("morph_rule", 1), ("synth_rule", 1)))));
        fake.StoredGrammarCheckHandler = (request, _) => Task.FromResult(CommandOutcome<StoredGrammarCheckResponse>.Success(
            new StoredGrammarCheckResponse(request.ProposalId is null ? Check("t", 2, 19, 6) : Check("p", 1, 13, 4))));
        fake.OnReadTrial((_, _) => Task.FromResult(CommandOutcome<TrialRecord?>.Success(Trial())));
        fake.PendingLoadHandler = (_, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
            new PendingChangesSnapshot(ProposalId, "revision/one", [], [])));
        fake.Review = new ProposalReviewResponse(ProposalId, "sha256:intent", "sha256:revision",
            [Rule("a", 0, "17", "23"), Rule("b", 1, "31", "29")], null, null, [], 0)
        { EvidenceDigest = "sha256:evidence", Label = "Sound change" };

        var context = WorkspaceContextTests.NewContext(fake);
        var overview = new OverviewPageModel(context);
        var texts = new TextsPageModel(context);
        var timing = new TimingPageModel(context);
        var warnings = new WarningsPageModel(context);
        var review = new ReviewPageModel(context);
        await context.OpenProjectAsync(TimingProposalViewTests.ProjectPath);
        var evidence = new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = Words(TodayOutcomes) },
            DateTimeOffset.UtcNow, false);
        context.Assess.Restore(evidence);
        context.PublishEvidence(evidence);
        await context.EvidencePublication;
        texts.Assess.Compare.UpdateOccurrences(word => Places[int.Parse(word[1..])]);
        await context.OpenProposalAsync(ProposalId, CancellationToken.None, "Sound change");
        return (fake, context, overview, texts, timing, warnings, review);
    }

    // The pair format's separator, so a pair is split where the window joins it.
    private static readonly string Arrow = string.Format(MotifAppStrings.OverviewPairFormat, "|", "|").Replace("|", "");

    private const string NamedMarker = "With this Proposal";

    private static string Pair(string today, string proposal) => string.Format(MotifAppStrings.OverviewPairFormat, today, proposal);

    private static IEnumerable<string> NumbersIn(string text) => Regex.Matches(text, @"\d[\d,]*(?:\.\d+)?")
        .Select(match => match.Value.TrimEnd(',').Replace(",", string.Empty));

    [Fact]
    public async Task EveryNumberOnEveryProposalPageBelongsToTheSideItIsDrawnFor()
    {
        var (fake, context, overview, texts, timing, warnings, review) = await OpenedAsync();
        var faults = new List<string>();

        var overviewNumbers = new Numbers(
            Today: ["118", "142", "71", "84", "380000", "64", "482", "19", "2", "83", "88", "611", "14", "3", "38", "38.0"],
            Proposal: ["131", "150", "66", "91", "479000", "77", "505", "13", "1", "87", "92", "640", "9", "44", "44.0"],
            Neither: ["0", "95"]);
        var textsNumbers = new Numbers(
            Today: ["6", "1", "5", "56", "19", "161"],
            Proposal: ["3", "7", "2", "15", "143", "78"],
            Neither: ["0", "12", "256"]);
        var timingNumbers = new Numbers(
            Today: ["10000", "2200", "2", "60", "40", "6000", "4000"],
            Proposal: ["13000", "3000", "1", "50", "6500"],
            Neither: ["0", "4"]);
        var warningsNumbers = new Numbers(
            Today: ["27", "2", "19", "6"],
            Proposal: ["18", "1", "13", "4"],
            Neither: ["0"]);
        var reviewNumbers = new Numbers(Today: ["17", "31"], Proposal: ["23", "29", "26"], Neither: ["2", "0"]);

        // The sample must give the sides nothing in common, or a swap could not be seen.
        foreach (var (page, numbers) in new[] { ("Overview", overviewNumbers), ("Texts", textsNumbers), ("Timing", timingNumbers),
                     ("Warnings", warningsNumbers), ("Review", reviewNumbers) })
            foreach (var shared in numbers.Today.Intersect(numbers.Proposal).Concat(numbers.Today.Intersect(numbers.Neither))
                         .Concat(numbers.Proposal.Intersect(numbers.Neither)))
                faults.Add($"{page}: the sample gives both sides the value {shared}");

        context.OpenPage(WorkspacePage.Overview);
        await VisitAsync("Overview", overviewNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await TextsProposalMatrixTests.UntilAsync(() => overview.TextCoverageMain.Contains(Arrow)),
            view => DrawOverview(overview, view), context, fake, faults);

        context.OpenPage(WorkspacePage.Texts);
        texts.Tab = TextsTab.Lists;
        await VisitAsync("Texts", textsNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await TextsProposalMatrixTests.UntilAsync(() => texts.Assess.Compare.ProposalWordsMatch is not null),
            view => DrawTexts(texts, view), context, fake, faults);

        context.OpenPage(WorkspacePage.Timing);
        await VisitAsync("Timing", timingNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await TextsProposalMatrixTests.UntilAsync(() => timing.HeadlineTotal.Contains(Arrow)),
            view => DrawTiming(timing, view, faults), context, fake, faults);
        Expect(faults, "Timing delta", "+30%", timing.DeltaBadge);

        context.OpenPage(WorkspacePage.Warnings);
        await VisitAsync("Warnings", warningsNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await TextsProposalMatrixTests.UntilAsync(() => warnings.Grammar.Warnings.Pairs),
            view => DrawWarnings(warnings, view), context, fake, faults);
        Expect(faults, "Warnings delta", "−9", warnings.DeltaBadge);

        context.OpenPage(WorkspacePage.Review);
        await VisitAsync("Review", reviewNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await review.LoadReviewAsync(), view => DrawReview(review), context, fake, faults);

        Assert.True(faults.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, faults));
    }

    private static void Expect(List<string> faults, string what, string expected, string actual)
    {
        if (expected != actual) faults.Add($"{what}: expected '{expected}' but drew '{actual}'");
    }

    // Flips through every view and back, checking what each draws and that no flip reads or reloads anything.
    private static async Task VisitAsync(string page, Numbers numbers, Action toPair, Func<Task> settled,
        Func<MarkupView, IReadOnlyList<Drawn>> draw, WorkspaceContext context, FakeCommandClient fake, List<string> faults)
    {
        toPair();
        await settled();
        var reads = ReadCounts(fake);
        var lens = context.EvidenceLens;
        var version = context.PageRequestVersion;
        var first = new Dictionary<MarkupView, string>();
        foreach (var view in Flips)
        {
            context.ShowProposalView(view);
            var drawn = draw(view);
            var all = string.Join(" | ", drawn.Select(item => item.Name + "=" + item.Text));
            if (first.TryGetValue(view, out var before))
            {
                if (before != all) faults.Add($"{page} {view}: drew different text the second time\n  was {before}\n  now {all}");
            }
            else first[view] = all;
            if (drawn.Count == 0) faults.Add($"{page} {view}: drew nothing, so nothing was checked");
            foreach (var item in drawn) CheckNumbers(page, view, item, numbers, faults);
        }

        foreach (var name in reads.Keys.Where(name => reads[name] != ReadCounts(fake)[name]))
            faults.Add($"{page}: flipping the view changed {name} from {reads[name]} to {ReadCounts(fake)[name]}");
        if (!ReferenceEquals(lens, context.EvidenceLens)) faults.Add($"{page}: flipping the view replaced the evidence lens");
        if (version != context.PageRequestVersion) faults.Add($"{page}: flipping the view reloaded the page");
    }

    // Counts every list the fake keeps of what the window asked, so any read a flip made shows.
    private static Dictionary<string, int> ReadCounts(FakeCommandClient fake) => typeof(FakeCommandClient).GetProperties()
        .Where(property => property.Name != nameof(FakeCommandClient.UsageEntries) && property.GetIndexParameters().Length == 0 &&
            property.GetValue(fake) is ICollection)
        .ToDictionary(property => property.Name, property => ((ICollection)property.GetValue(fake)!).Count);

    private static void CheckNumbers(string page, MarkupView view, Drawn item, Numbers numbers, List<string> faults)
    {
        var where = $"{page} {view} {item.Name} '{item.Text}'";
        // A spoken name gives the cell's usual (today's) number, then the Proposal's after its label.
        if (item.Rule == Follows.NamedWithProposal && item.Text.Contains(NamedMarker))
        {
            var parts = item.Text.Split(NamedMarker, 2);
            foreach (var number in NumbersIn(parts[0]).Where(number => !numbers.IsToday(number) && !numbers.IsNeither(number)))
                faults.Add($"{where}: before the Proposal's label is {number}, which is not today's");
            var read = parts[1].Contains(Arrow) ? parts[1].Split(Arrow, 2) : ["", parts[1]];
            foreach (var number in NumbersIn(read[0]).Where(number => !numbers.IsToday(number) && !numbers.IsNeither(number)))
                faults.Add($"{where}: the left of the label's pair has {number}, which is not today's");
            foreach (var number in NumbersIn(read[1]).Where(number => !numbers.IsProposal(number) && !numbers.IsNeither(number)))
                faults.Add($"{where}: after the Proposal's label is {number}, which is not the Proposal's");
            return;
        }

        // A list's name says today's count and then the Proposal's, each with its label.
        if (item.Rule == Follows.ListChipName && Regex.Match(item.Text, @"(\d[\d,]*) words? today, (\d[\d,]*) words? with this Proposal") is { Success: true } named)
        {
            if (!numbers.IsToday(named.Groups[1].Value.Replace(",", ""))) faults.Add($"{where}: {named.Groups[1].Value} is labelled today's but is not");
            if (!numbers.IsProposal(named.Groups[2].Value.Replace(",", ""))) faults.Add($"{where}: {named.Groups[2].Value} is labelled the Proposal's but is not");
            return;
        }

        var paired = item.Rule == Follows.PairedAlways || item.Rule == Follows.View && view == MarkupView.TodayToProposal;
        if (paired && item.Text.Contains(Arrow))
        {
            var parts = item.Text.Split(Arrow, 2);
            foreach (var number in NumbersIn(parts[0]).Where(number => !numbers.IsToday(number) && !numbers.IsNeither(number)))
                faults.Add($"{where}: the left of the pair has {number}, which is not today's");
            foreach (var number in NumbersIn(parts[1]).Where(number => !numbers.IsProposal(number) && !numbers.IsNeither(number)))
                faults.Add($"{where}: the right of the pair has {number}, which is not the Proposal's");
            return;
        }

        var wantsToday = item.Rule switch
        {
            Follows.TodayAlways => true,
            Follows.ProposalAlways => false,
            _ => view != MarkupView.Proposal,
        };
        var wantsBoth = item.Rule == Follows.View && view == MarkupView.TodayToProposal;
        foreach (var number in NumbersIn(item.Text))
        {
            if (numbers.IsNeither(number)) continue;
            if (wantsBoth && (numbers.IsToday(number) || numbers.IsProposal(number))) continue;
            if (wantsToday && numbers.IsToday(number)) continue;
            if (!wantsToday && !wantsBoth && numbers.IsProposal(number)) continue;
            faults.Add($"{where}: {number} is " + (numbers.IsToday(number) ? "today's" : numbers.IsProposal(number) ? "the Proposal's" : "from neither side"));
        }
    }

    private static IReadOnlyList<Drawn> DrawOverview(OverviewPageModel page, MarkupView view)
    {
        var drawn = new List<Drawn>
        {
            new("Text Coverage", page.TextCoverageMain),
            new("Accuracy", page.AccuracyMain),
            new("Speed", page.SpeedMain),
            new("Warnings", page.WarningsCount),
        };
        // Today's detail sits under each headline and is not drawn in the Proposal view.
        if (page.ShowTodayDetail)
        {
            drawn.Add(new("Speed median", page.SpeedMedian, Follows.TodayAlways));
            drawn.Add(new("Speed elapsed", page.SpeedElapsed, Follows.TodayAlways));
            drawn.Add(new("Coverage words", page.TextCoverageWords, Follows.TodayAlways));
            drawn.Add(new("Warning words", page.WarningsYourWordsText, Follows.TodayAlways));
            drawn.Add(new("Spelling candidates", page.WarningsSpellingCandidatesText, Follows.TodayAlways));
        }
        return drawn;
    }

    private static IReadOnlyList<Drawn> DrawTexts(TextsPageModel page, MarkupView view)
    {
        var compare = page.Assess.Compare;
        var drawn = new List<Drawn>();
        foreach (var cell in compare.Cells.Where(cell => cell.Row == WordProjectStatus.NotPresent))
        {
            drawn.Add(new($"{cell.Column} count", cell.CountNumberText));
            drawn.Add(new($"{cell.Column} places", cell.PlacesText));
            drawn.Add(new($"{cell.Column} name", cell.AccessibleName, Follows.NamedWithProposal));
        }
        drawn.AddRange(compare.Columns.Select(column => new Drawn($"{column.Column} column", column.CountText)));
        drawn.AddRange(compare.Rows.Select(row => new Drawn($"{row.Row} row", row.CountText)));
        foreach (var list in page.TextsLists.Lists.Where(list => list.ShowsChip))
        {
            drawn.Add(new($"list {list.Name} chip", list.ChipCount));
            drawn.Add(new($"list {list.Name} count", list.CountText));
            drawn.Add(new($"list {list.Name} name", list.AccessibleName, Follows.ListChipName));
        }
        return drawn;
    }

    private static IReadOnlyList<Drawn> DrawTiming(TimingPageModel page, MarkupView view, List<string> faults)
    {
        var drawn = new List<Drawn>
        {
            new("Total", page.HeadlineTotal),
            new("Median", page.HeadlineMedian),
            new("Stopped", page.HeadlineStopped),
            new("Caption", page.HeadlineTotalCaption),
        };
        foreach (var share in page.KindShares)
        {
            drawn.Add(new($"{share.Kind} share", share.ShownShareText));
            drawn.Add(new($"{share.Kind} work", share.ShownWorkText));
            drawn.Add(new($"{share.Kind} note", share.ShownShareNote ?? string.Empty));
        }

        // A kind one side lacks reads as none on that side, never as zero.
        var kinds = page.KindShares.Select(share => share.Kind).Where(kind => kind.Length > 0).ToArray();
        string[] expected = view switch
        {
            MarkupView.Today => ["morph_rule", "affix"],
            MarkupView.Proposal => ["morph_rule", "synth_rule"],
            _ => ["morph_rule", "affix", "synth_rule"],
        };
        if (!expected.Order().SequenceEqual(kinds.Order()))
            faults.Add($"Timing {view}: the kinds drawn are {string.Join(", ", kinds)}, not {string.Join(", ", expected)}");
        if (view == MarkupView.TodayToProposal)
        {
            var affix = page.KindShares.Single(share => share.Kind == "affix");
            var synth = page.KindShares.Single(share => share.Kind == "synth_rule");
            Expect(faults, "Timing affix share", Pair(TimingShare.FormatPercent(0.4), MotifAppStrings.ProposalPageNone), affix.ShownShareText);
            Expect(faults, "Timing synth_rule share", Pair(MotifAppStrings.TimingNoneToday, TimingShare.FormatPercent(0.5)), synth.ShownShareText);
        }
        return drawn;
    }

    private static IReadOnlyList<Drawn> DrawWarnings(WarningsPageModel page, MarkupView view)
    {
        var warnings = page.Grammar.Warnings;
        return
        [
            new("All", warnings.AllCountText),
            new("Errors", warnings.ErrorCountText),
            new("Warnings", warnings.WarningCountText),
            new("Information", warnings.InformationCountText),
            new("Summary", warnings.CountSummary),
            new("Breakdown", warnings.BreakdownText),
            new("Today's breakdown", warnings.TodayBreakdownText, Follows.TodayAlways),
            new("Today's total", warnings.TodayTotalCount.ToString(), Follows.TodayAlways),
            new("Badge", page.Badge, Follows.TodayAlways),
            new("Gone", warnings.GoneCount.ToString(), Follows.TodayAlways),
            new("New", warnings.NewCount.ToString(), Follows.ProposalAlways),
        ];
    }

    // Review Changes follows the open Proposal whatever the bar shows, so it must draw the same in all three views.
    private static IReadOnlyList<Drawn> DrawReview(ReviewPageModel page)
    {
        var drawn = new List<Drawn>
        {
            new("Count", page.CountText),
            new("Looked at", page.LookedAtText),
            new("Keep all", page.KeepAllText),
            new("Undo all", page.UndoAllText),
            new("Work notice", page.WorkNotice ?? string.Empty, Follows.ProposalAlways),
        };
        foreach (var entry in page.Entries)
            drawn.Add(new($"{entry.Key} effect", entry.EffectText, Follows.PairedAlways));
        return drawn;
    }
}
