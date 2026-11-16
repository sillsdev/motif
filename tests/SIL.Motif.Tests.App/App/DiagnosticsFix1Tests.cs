using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Diagnostics fix round 1: what the Warnings, Speed by Rule and Why This Analysis views show a person.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class DiagnosticsFix1Tests
{
    private const string Set = "Phonemes";

    // The raw records the engine stored for the CEB project: three phonemes whose names are punctuation.
    private static GrammarWarningPart Phoneme(string name) => new($"{name} (in {Set})", GrammarWarningPartRole.Object,
        $"table#0:{name}", "PhPhoneme") { Title = name, Subtitle = $"in {Set}", Status = GrammarSubjectStatus.Object };

    private static GrammarWarning DuplicateFeatureBundle() => new(GrammarDiagnosticLevel.Error, "Duplicate feature bundle",
        [new GrammarWarningPart(Set, GrammarWarningPartRole.Object, "be7fe79c", "PhPhonemeSet") { Title = Set, Status = GrammarSubjectStatus.Object },
            Phoneme("("), Phoneme(")"), Phoneme("/")],
        [new GrammarWarningPart($"{Set} (, ), / share the same feature values.", GrammarWarningPartRole.Text)],
        $"error: hc-duplicate-feature-bundle: {Set} (, ), / share the same feature values.")
    {
        Code = "hc-duplicate-feature-bundle",
        Group = "Duplicate segment features",
        Title = "Duplicate segment features",
        Description = $"{Set} (, ), / share the same feature values.",
    };

    private static GrammarWarning StemWithoutCategory(string headword) => new(GrammarDiagnosticLevel.Error,
        "Stem no grammatical category",
        [new GrammarWarningPart(headword, GrammarWarningPartRole.Object, $"id-{headword}", "LexEntry")
        {
            Title = headword, Status = GrammarSubjectStatus.Object, FieldWorksTool = "lexiconEdit",
            FieldWorksLink = $"silfw://localhost/link?database%3dceb%26tool%3dlexiconEdit%26guid%3d{headword}%26tag%3d",
        }],
        [new GrammarWarningPart($"Lexical entry '{headword}' has no grammatical category.", GrammarWarningPartRole.Text)],
        $"error: hc-stem-no-grammatical-category: Lexical entry '{headword}' has no grammatical category.")
    {
        Code = "hc-stem-no-grammatical-category",
        Group = "Stem has no category",
        Title = "Stem has no category",
        Description = $"Lexical entry '{headword}' has no grammatical category.",
    };

    private static IEnumerable<string?> Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text);

    private static async Task<(WorkspaceContext Context, DiagnosticsPageModel Model, FakeCommandClient Fake)> OpenAsync(
        Action<FakeCommandClient>? arrange = null)
    {
        var fake = new FakeCommandClient();
        arrange?.Invoke(fake);
        var context = WorkspaceContextTests.NewContext(fake);
        context.Diagnostics = true;
        var model = new DiagnosticsPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        return (context, model, fake);
    }

    [Fact]
    public void PhonemeNamesShowAndLinkedStemsAreSeparated()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            GrammarWarning[] findings = [DuplicateFeatureBundle(), StemWithoutCategory("hái"), StemWithoutCategory("pitnit"),
                StemWithoutCategory("sagang")];
            var (context, model, _) = await OpenAsync(fake => fake.StoredGrammarCheckIs(new GrammarCheckResponse(findings, HasBaseline: true)));
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var shown = Texts(page).ToArray();
                Assert.Contains("'(' (in Phonemes)", shown);
                Assert.Contains("')' (in Phonemes)", shown);
                Assert.Contains("'/' (in Phonemes)", shown);
                Assert.Contains("Phonemes '(', ')', '/' share the same feature values.", shown);
                Assert.DoesNotContain(shown, text => text is not null && text.Contains("Phonemes (, ), /", StringComparison.Ordinal));
                // Four objects in the phoneme row and three entries in the stem row: three commas plus two.
                Assert.Equal(5, shown.Count(text => text == ","));
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TheWarningsTabCountsTheSameFindingsAsTheRowsAndTheHealthStrip()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var findings = new List<GrammarWarning> { DuplicateFeatureBundle() };
            findings.AddRange(Enumerable.Range(0, 135).Select(index => StemWithoutCategory($"stem{index}")));
            findings.AddRange(Enumerable.Range(0, 15).Select(index => new GrammarWarning(GrammarDiagnosticLevel.Warning, "Other", [], [],
                $"other {index}") { Code = "lexicon.other" }));
            var check = new GrammarCheckResponse(findings, HasBaseline: true);
            var (context, model, _) = await OpenAsync(fake => fake.StoredGrammarCheckIs(check));
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Equal(151, model.Warnings.Rows.Sum(row => row.RepeatCount));
                var summary = Assert.Single(Texts(page), text => text is not null && text.StartsWith("151 findings", StringComparison.Ordinal));
                Assert.Contains("136 errors", summary);
                Assert.Contains("15 warnings", summary);
                // The health strip's number comes from the same stored check.
                var overview = SampleOverview.Populated() with
                {
                    Warnings = new OverviewWarningsSummary(SIL.Motif.Commands.Catalog.WarningsCommand.FromCheck(check).TotalCount, null, null, null),
                };
                var strip = new HealthStripViewModel(context) { Overview = overview };
                Assert.Contains("151", strip.WarningText);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    private static WordTraceResponse TraceWithRule(string ruleText, string? captured, string label)
    {
        var response = WhyThisAnalysisTests.Trace();
        var reference = new TraceRef("morphologicalRule:r16", "morphologicalRule", label) { CapturedFieldWorksLabel = captured,
            FieldWorks = new("lexiconEdit", "Lexicon Edit", "g", "silfw://localhost/link?guid=g") };
        return response with
        {
            Reading = response.Reading with
            {
                Refs = [reference],
                RulesOnBestPath = [new TraceRuleReading(ruleText, null, "Morphological rule", "applied", "", []) { RefId = reference.Id }],
            },
        };
    }

    [Theory]
    [InlineData("dako-an", "dako-an")]
    [InlineData(null, "Rule mrule#16")]
    public void RuleReferencesShowTheRuleNameOrLabelTheRawIdAsARule(string? captured, string expected)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new WhyThisAnalysisTests.Rig();
            await rig.OpenAsync();
            var response = TraceWithRule("mrule#16", captured, "mrule#16");
            rig.Fake.TraceWordCompletesWith(response);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => fold.State == WhyThisAnalysisState.Ready, "The fold did not become ready.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            var rule = Assert.Single(fold.Rules);
            Assert.Equal(expected, rule.Name);
            Assert.Equal("silfw", rule.Link?.Scheme);
            Assert.Equal($"Open {expected} in FieldWorks", rule.LinkName);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ASpeedByRuleReadThatFailsLeavesTheLoadingState()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, model, fake) = await OpenAsync(f => f.OnTiming((_, _) => Task.FromException<CommandOutcome<TimingResponse>>(
                new InvalidOperationException("The store could not be read."))));
            context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.UtcNow, false));
            await context.EvidencePublication;
            await model.SpeedByRule.ReadAsync("project.fwdata", CancellationToken.None);
            Assert.Equal(MotifAppStrings.DiagnosticsUnreadable, model.SpeedByRule.UnavailableText);
            await context.StopProjectWorkAsync();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ASpeedByRuleTabShowsRecordedRulesInsteadOfReading()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, model, fake) = await OpenAsync(f => f.TimingCompletesWith(
                new TimingResponse("assessment-parse", "all", "rule", 5, null, null, [], [], [])
                {
                    Work = new WordWorkSummary(2, 20, null, null),
                    ParserWorkAggregates = [new("rule", "Recorded rule", "phon_rule", 12, false)],
                }));
            model.SelectedTabIndex = 1;
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = page };
            try
            {
                window.Show();
                context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.UtcNow, false));
                await context.EvidencePublication;
                await Wait.UntilAsync(() => model.SpeedByRule.HasRows, "Speed by Rule never loaded.", TimeSpan.FromSeconds(10),
                    between: () => Dispatcher.UIThread.RunJobs());
                PageScreenshots.Settle(window);
                var shown = Texts(page).ToArray();
                Assert.DoesNotContain(MotifAppStrings.DiagnosticsReading, shown);
                Assert.Contains("Recorded rule", shown);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AWarningsReadThatFailsLeavesTheLoadingState()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, model, fake) = await OpenAsync();
            fake.StoredGrammarCheckHandler = (_, _) =>
                Task.FromException<CommandOutcome<StoredGrammarCheckResponse>>(new InvalidOperationException("The store could not be read."));
            await model.Warnings.ReadAsync("project.fwdata", CancellationToken.None);
            Assert.Equal(MotifAppStrings.DiagnosticsUnreadable, model.Warnings.StatusText);
            await context.StopProjectWorkAsync();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ATraceRequestThatFailsLeavesTheFoldNotLoadingAndStopsCleanly()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new WhyThisAnalysisTests.Rig();
            await rig.OpenAsync();
            rig.Fake.OnTraceWord((_, _) => Task.FromException<CommandOutcome<WordTraceResponse>>(
                new InvalidOperationException("The parser stopped.")));
            using var fold = new WhyThisAnalysisViewModel(rig.Context, WhyThisAnalysisTests.Trace().Word);
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => fold.State != WhyThisAnalysisState.Loading, "The fold stayed loading.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            Assert.Equal(WhyThisAnalysisState.Unavailable, fold.State);
            await fold.StopAsync();
        }, TimeSpan.FromSeconds(60));
    }
}
