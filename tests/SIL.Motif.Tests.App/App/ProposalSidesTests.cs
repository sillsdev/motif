using System.Collections;
using System.Text.RegularExpressions;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
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

    // Which side's numbers a text may carry: the view's own, one side always, or none and the same everywhere.
    private enum Follows { View, TodayAlways, ProposalAlways, ProposalInPair, SameEverywhere, PairedAlways, NamedWithProposal, ListChipName }

    private enum Side { Today, Proposal, Neither }

    private sealed record Drawn(string Name, string Text, Follows Rule = Follows.View);

    private sealed record Numbers(string[] Today, string[] Proposal, string[] Neither)
    {
        public bool IsToday(string number) => Today.Contains(number);

        public bool IsProposal(string number) => Proposal.Contains(number);

        public bool IsNeither(string number) => Neither.Contains(number);
    }

    // One letter per word (analysed, no-analysis, timed-out, unassessed); both sides hold the same twenty.
    private const string TodayLayout = "unuuuauaunuuuunntnun";

    private const string ProposalLayout = "aatatnuuntuttuatantt";

    private static string OutcomeOf(char letter) => letter switch
    {
        'a' => "analysed", 'n' => "no-analysis", 't' => "timed-out", _ => "unassessed",
    };

    private static AssessmentWordResult Word(int index, char letter)
    {
        var form = "w" + index;
        var outcome = OutcomeOf(letter);
        return letter == 'a'
            ? new(form, outcome, false, "Search completed", 10, null)
            {
                ProjectStanding = ProjectStanding.Approved,
                ReadingGrades = ["approved"],
                Readings = [new ParserReading([new ParserReadingMorph(form, "gloss", "n", null, false, null)])],
                Morphology = new ParseWordEvidence("v1", 0, form, 10, false, false, false,
                    [new ParseAnalysis([new ParseMorph(form + "-0", "n", null, null)])], []),
            }
            : new(form, outcome, letter == 't', letter == 't' ? "TimedOut" : "Complete", 1, null);
    }

    // Each word has its own prime places, so every cell's places differ from the other side's.
    private static readonly int[] Places = [3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73];

    private static AssessmentWordResult[] Words(string layout) => [.. layout.Select((letter, index) => Word(index, letter))];

    private static int CountOf(string layout, char letter) => layout.Count(item => item == letter);

    private static int PlacesOf(string layout, char letter) => layout.Select((item, index) => item == letter ? Places[index] : 0).Sum();

    // Every number the Texts matrix draws for one side: each outcome's words and places, and a row total.
    private static string[] TextsSide(string layout) =>
    [
        .. "antu".SelectMany(letter => new[] { CountOf(layout, letter), PlacesOf(layout, letter) }).Select(number => number.ToString()),
        (layout.Length - CountOf(layout, 'a')).ToString(),
    ];

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
        WordResults = Words(ProposalLayout),
    };

    private static GrammarChangeEntry Rule(string key, int ordinal, string before, string after) =>
        new(key, ordinal, "Title " + key, ["op-" + key], [], "digest-" + key, false, "Composer", "Adds a rule",
            [new EffectView("id-" + key, "Allomorphs", [new EffectChange("swh", before, after)])], [], []);

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, OverviewPageModel Overview,
        TextsPageModel Texts, TimingPageModel Timing, WarningsPageModel Warnings, ReviewPageModel Review)> OpenedAsync(
        TimingProposalViewTests.Word[]? proposalTiming = null)
    {
        var fake = new FakeCommandClient();
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? TodayOverview() : ProposalOverview())));
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            request.ProposalId is null
                ? TimingProposalViewTests.Respond(request, TodayTiming, ("morph_rule", 3), ("affix", 2))
                : TimingProposalViewTests.Respond(request, proposalTiming ?? ProposalTiming, ("morph_rule", 1), ("synth_rule", 1)))));
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
        var evidence = new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = Words(TodayLayout) },
            DateTimeOffset.UtcNow, false);
        context.Assess.Restore(evidence);
        context.PublishEvidence(evidence);
        await context.EvidencePublication;
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
        var textsNumbers = new Numbers(Today: TextsSide(TodayLayout), Proposal: TextsSide(ProposalLayout), Neither: ["0", "20"]);
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

        var rails = new Dictionary<WorkspacePage, PageViewModel>
        {
            [WorkspacePage.Overview] = new(WorkspacePage.Overview, "Overview", "overview", overview),
            [WorkspacePage.Texts] = new(WorkspacePage.Texts, "Texts", "texts", texts),
            [WorkspacePage.Timing] = new(WorkspacePage.Timing, "Timing", "timing", timing),
            [WorkspacePage.Warnings] = new(WorkspacePage.Warnings, "Warnings", "warnings", warnings),
            [WorkspacePage.Review] = new(WorkspacePage.Review, "Review", "review", review),
        };
        Action<MarkupView> RailOf(WorkspacePage page, string badge, string name) =>
            view => CheckRail(rails[page], page.ToString(), view, badge, name, faults);

        context.OpenPage(WorkspacePage.Overview);
        await VisitAsync("Overview", overviewNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await Wait.UntilAsync(() => overview.TextCoverageMain.Contains(Arrow)),
            view => DrawOverview(overview, view), context, fake, faults, RailOf(WorkspacePage.Overview, "", ""));

        context.OpenPage(WorkspacePage.Texts);
        texts.Tab = TextsTab.Lists;
        await VisitAsync("Texts", textsNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () =>
            {
                await Wait.UntilAsync(() => texts.Assess.Compare.ProposalWordsMatch is not null);
                texts.Assess.Compare.UpdateOccurrences(word => Places[int.Parse(word[1..])]);
                context.ShowProposalView(MarkupView.TodayToProposal);
            },
            view => DrawTexts(texts, view), context, fake, faults,
            RailOf(WorkspacePage.Texts, "+3", "3 more words are Same with this Proposal than today"));

        context.OpenPage(WorkspacePage.Timing);
        await VisitAsync("Timing", timingNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await Wait.UntilAsync(() => timing.HeadlineTotal.Contains(Arrow)),
            view => DrawTiming(timing, view, faults), context, fake, faults,
            RailOf(WorkspacePage.Timing, "+30%", "30% more parser work with this Proposal than today"));

        context.OpenPage(WorkspacePage.Warnings);
        await VisitAsync("Warnings", warningsNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await Wait.UntilAsync(() => warnings.Grammar.Warnings.Pairs),
            view => DrawWarnings(warnings, view), context, fake, faults,
            RailOf(WorkspacePage.Warnings, "−9", "9 fewer warnings with this Proposal than today"));

        context.OpenPage(WorkspacePage.Review);
        await VisitAsync("Review", reviewNumbers, () => context.ShowProposalView(MarkupView.TodayToProposal),
            async () => await review.LoadReviewAsync(), view => DrawReview(review), context, fake, faults,
            RailOf(WorkspacePage.Review, "", ""));

        // A share with no text today beside a Proposal value reads "Proposal x" with the number.
        var proposalOnly = new TimingShare("Kind", "kind", null, null, 0, null) { ProposalShareText = TimingShare.FormatPercent(0.6) };
        Expect(faults, "A Proposal-only share", string.Format(MotifAppStrings.PairedProposalOnly, TimingShare.FormatPercent(0.6)),
            proposalOnly.ShownShareText);

        Assert.True(faults.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, faults));
    }

    // The pair needs both sides to cover the same words, so only the Proposal view can draw a count of its own.
    [Fact]
    public async Task TheTimingCaptionInTheProposalViewCountsTheProposalsWordsNotToday()
    {
        TimingProposalViewTests.Word[] fiveWords = [.. ProposalTiming, new("e", 700)];
        var (_, context, _, _, timing, _, _) = await OpenedAsync(fiveWords);
        context.OpenPage(WorkspacePage.Timing);
        context.ShowProposalView(MarkupView.Proposal);
        await Wait.UntilAsync(() => timing.HeadlineTotal == TimingShare.FormatParserWork(13_700));

        var faults = new List<string>();
        Expect(faults, "Proposal caption", string.Format(MotifAppStrings.TimingCaptionAllWordsFormat, "5"), timing.HeadlineTotalCaption);
        context.ShowProposalView(MarkupView.Today);
        Expect(faults, "Today caption", string.Format(MotifAppStrings.TimingCaptionAllWordsFormat, "4"), timing.HeadlineTotalCaption);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Expect(faults, "Pair caption", string.Format(MotifAppStrings.TimingCaptionAllWordsFormat, "4"), timing.HeadlineTotalCaption);
        Assert.True(faults.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, faults));
    }

    private static void Expect(List<string> faults, string what, string expected, string actual)
    {
        if (expected != actual) faults.Add($"{what}: expected '{expected}' but drew '{actual}'");
    }

    // Flips through every view, checking what each draws and that no flip reads or reloads anything.
    private static async Task VisitAsync(string page, Numbers numbers, Action toPair, Func<Task> settled,
        Func<MarkupView, IReadOnlyList<Drawn>> draw, WorkspaceContext context, FakeCommandClient fake, List<string> faults,
        Action<MarkupView>? rail = null)
    {
        toPair();
        await settled();
        var reads = ReadCounts(fake);
        var lens = context.EvidenceLens;
        var version = context.PageRequestVersion;
        var first = new Dictionary<MarkupView, string>();
        var seen = new Dictionary<(MarkupView, string), Drawn>();
        foreach (var view in Flips)
        {
            context.ShowProposalView(view);
            rail?.Invoke(view);
            var drawn = draw(view);
            var all = string.Join(" | ", drawn.Select(item => item.Name + "=" + item.Text));
            if (first.TryGetValue(view, out var before))
            {
                if (before != all) faults.Add($"{page} {view}: drew different text the second time\n  was {before}\n  now {all}");
            }
            else first[view] = all;
            if (drawn.Count == 0) faults.Add($"{page} {view}: drew nothing, so nothing was checked");
            foreach (var item in drawn)
            {
                CheckNumbers(page, view, item, numbers, faults);
                seen[(view, item.Name)] = item;
            }
        }

        // A text with the same numbers on both sides could not show a swap, so it must say it is the same.
        foreach (var ((view, name), item) in seen.Where(entry => entry.Key.Item1 == MarkupView.Today))
        {
            if (!seen.TryGetValue((MarkupView.Proposal, name), out var proposal)) continue;
            var today = NumbersIn(item.Text).Where(number => number != "0").ToArray();
            var sameInEveryView = item.Rule == Follows.SameEverywhere;
            var oneSide = item.Rule is Follows.TodayAlways or Follows.ProposalAlways or Follows.PairedAlways;
            if (today.Length > 0 && !sameInEveryView && !oneSide && today.SequenceEqual(NumbersIn(proposal.Text).Where(number => number != "0")))
                faults.Add($"{page} {name}: reads '{item.Text}' in both the Today and Proposal views, so a swap would not show");
            if (sameInEveryView && seen.Where(entry => entry.Key.Item2 == name).Select(entry => entry.Value.Text).Distinct().Count() > 1)
                faults.Add($"{page} {name}: is meant to read the same in every view but does not");
        }

        foreach (var name in reads.Keys.Where(name => reads[name] != ReadCounts(fake)[name]))
            faults.Add($"{page}: flipping the view changed {name} from {reads[name]} to {ReadCounts(fake)[name]}");
        if (!ReferenceEquals(lens, context.EvidenceLens)) faults.Add($"{page}: flipping the view replaced the evidence lens");
        if (version != context.PageRequestVersion) faults.Add($"{page}: flipping the view reloaded the page");
    }

    // The sidebar entry of a page shows the delta and says whose number it is, the same in every view.
    private static void CheckRail(PageViewModel entry, string page, MarkupView view, string badge, string name, List<string> faults)
    {
        var where = $"{page} rail {view}";
        Expect(faults, where + " model badge", badge, entry.Model.DeltaBadge);
        Expect(faults, where + " badge", badge, entry.DeltaBadge);
        Expect(faults, where + " badge name", name, entry.DeltaBadgeName);
        Expect(faults, where + " has badge", (badge.Length > 0).ToString(), entry.HasDeltaBadge.ToString());
        Expect(faults, where + " tip", name.Length == 0 ? entry.Title : entry.Title + ": " + name, entry.RailTip);
        Expect(faults, where + " automation name", name.Length == 0 ? entry.Title + " page" : entry.Title + " page: " + name,
            entry.AutomationName);
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
            if (parts[1].Contains(Arrow)) CheckPair(where, parts[1], numbers, faults);
            else
                foreach (var number in NumbersIn(parts[1]).Where(number => !numbers.IsProposal(number) && !numbers.IsNeither(number)))
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
            CheckPair(where, item.Text, numbers, faults);
            return;
        }

        // Without an arrow the pair view draws one side's number, so each text says which side.
        var side = item.Rule switch
        {
            Follows.TodayAlways => Side.Today,
            Follows.ProposalAlways => Side.Proposal,
            Follows.SameEverywhere => Side.Neither,
            Follows.ProposalInPair => view == MarkupView.Today ? Side.Today : Side.Proposal,
            _ => view == MarkupView.Today ? Side.Today : view == MarkupView.Proposal ? Side.Proposal : Side.Neither,
        };
        foreach (var number in NumbersIn(item.Text))
        {
            if (numbers.IsNeither(number) || side == Side.Today && numbers.IsToday(number) ||
                side == Side.Proposal && numbers.IsProposal(number)) continue;
            faults.Add($"{where}: {number} is " + (numbers.IsToday(number) ? "today's" : numbers.IsProposal(number) ? "the Proposal's" : "from neither side") +
                $", but this text belongs to {side}");
        }
    }

    // Today's number is before an arrow and the Proposal's after it, as in "6 → 3 words · 288 → 113 places".
    private static void CheckPair(string where, string text, Numbers numbers, List<string> faults)
    {
        var chunks = text.Split(Arrow);
        for (var index = 0; index < chunks.Length; index++)
        {
            var found = NumbersIn(chunks[index]).ToArray();
            for (var at = 0; at < found.Length; at++)
            {
                var number = found[at];
                var middle = index > 0 && index < chunks.Length - 1;
                var today = index == 0 || middle && at == found.Length - 1;
                var proposal = index == chunks.Length - 1 || middle && at == 0;
                if (numbers.IsNeither(number) || today && numbers.IsToday(number) || proposal && numbers.IsProposal(number) ||
                    middle && at > 0 && at < found.Length - 1 && (numbers.IsToday(number) || numbers.IsProposal(number))) continue;
                faults.Add($"{where}: {number} is " + (numbers.IsToday(number) ? "today's" : numbers.IsProposal(number) ? "the Proposal's" : "from neither side") +
                    (today ? ", where today's belongs" : ", where the Proposal's belongs"));
            }
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
        drawn.Add(new("Note", page.ProposalNote ?? string.Empty));
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
        foreach (var cell in compare.Cells)
        {
            drawn.Add(new($"{cell.Row} {cell.Column} count", cell.CountNumberText));
            drawn.Add(new($"{cell.Row} {cell.Column} places", cell.PlacesText));
            drawn.Add(new($"{cell.Row} {cell.Column} name", cell.AccessibleName, Follows.NamedWithProposal));
        }
        drawn.Add(new("Note", page.ProposalNote ?? string.Empty));
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
            new("Caption", page.HeadlineTotalCaption, Follows.SameEverywhere),
            new("Note", page.ProposalNote ?? string.Empty),
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
            Expect(faults, "Timing affix work", Pair(TimingShare.FormatParserWork(4_000), MotifAppStrings.ProposalPageNone), affix.ShownWorkText);
            Expect(faults, "Timing synth_rule share", Pair(MotifAppStrings.TimingNoneToday, TimingShare.FormatPercent(0.5)), synth.ShownShareText);
            Expect(faults, "Timing synth_rule work", Pair(MotifAppStrings.TimingNoneToday, TimingShare.FormatParserWork(6_500)), synth.ShownWorkText);
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
            new("Breakdown", warnings.BreakdownText, Follows.ProposalInPair),
            new("Today's breakdown", warnings.TodayBreakdownText, Follows.TodayAlways),
            new("Today's total", warnings.TodayTotalCount.ToString(), Follows.TodayAlways),
            new("Badge", page.Badge, Follows.TodayAlways),
            new("Gone", warnings.GoneCount.ToString(), Follows.TodayAlways),
            new("New", warnings.NewCount.ToString(), Follows.ProposalAlways),
            new("Note", page.ProposalNote ?? string.Empty),
            .. page.AreaFilters.Select(area => new Drawn($"area {area.Label}", area.Count.ToString(), Follows.ProposalInPair)),
        ];
    }

    // Review Changes follows the open Proposal whatever the bar shows, so it draws the same in all views.
    private static IReadOnlyList<Drawn> DrawReview(ReviewPageModel page)
    {
        var drawn = new List<Drawn>
        {
            new("Count", page.CountText, Follows.SameEverywhere),
            new("Looked at", page.LookedAtText, Follows.SameEverywhere),
            new("Keep all", page.KeepAllText, Follows.SameEverywhere),
            new("Undo all", page.UndoAllText, Follows.SameEverywhere),
            new("Work notice", page.WorkNotice ?? string.Empty, Follows.ProposalAlways),
        };
        foreach (var entry in page.Entries)
            drawn.Add(new($"{entry.Key} effect", entry.EffectText, Follows.PairedAlways));
        return drawn;
    }
}
