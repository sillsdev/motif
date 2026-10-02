using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Worker.Store;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TryWordPageTests
{
    [Fact]
    public void TreeContextDisplaysCapturedFieldWorksNamesWithoutBecomingABestPath()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!);
            var page = new TryWordPageModel(context);
            context.TryWord("word");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.Empty(page.Trace.Reading!.RulesOnBestPath);
            Assert.Equal("Vowel harmony", page.Trace.Root!.Children[0].Source);
            page.Trace.Candidates[0].IsTreeContextExpanded = true;
            Assert.Equal("Vowel harmony", page.Trace.Candidates[0].RecordedTreeContext[0].Source);
            Assert.Equal("Producer name", Assert.Single(page.Trace.Reading!.Refs).Label);
        });
    }

    [Fact]
    public void AncestorRecordedRulesRetainTheirCapturedNamesAndInspectorIdentity()
    {
        RunOnAvalonia(async () =>
        {
            var diagnostic = System.Text.Json.Nodes.JsonNode.Parse(TraceEnvelope.CapturedRuleLabel)!;
            var children = diagnostic["trace"]!["children"]!.AsArray();
            var terminal = children[1]!.DeepClone();
            children.RemoveAt(1);
            children[0]!["children"]!.AsArray().Add(terminal);
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(diagnostic.ToJsonString()).Value!);
            var page = new TryWordPageModel(context);
            var timing = new TimingPageModel(context);
            context.TryWord("word");
            await page.Trace.TryCommand.ExecutionTask!;

            var row = Assert.Single(page.Trace.RecordedRoots).Children[0];
            Assert.Equal("Vowel harmony", row.Source);
            var subject = Assert.IsType<InspectorSubject>(row.InspectSubject);
            Assert.Equal(InspectorSubjectKind.Rule, subject.Kind);
            Assert.Equal("Vowel harmony", subject.Label);
            Assert.Equal(new TraceTimingKey("phon_rule", TraceEnvelope.CapturedRuleId) { IdentityQuality = "authored" }, subject.TimingKey);
            Assert.Equal("authored", subject.IdentityQuality);
            Assert.Equal("Producer name", Assert.Single(page.Trace.Reading!.Refs).Label);
            page.OpenTimingCommand.Execute(null);
            Assert.Equal(["word"], timing.Focus!.Words);
            Assert.Null(timing.Focus.Rule);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpertRecordedContextDisplaysCapturedNamesBesideProducerEvidence(bool reopened)
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            page.Trace.IsExpert = true;
            if (reopened)
            {
                context.ProjectPath = null;
                page.Trace.Result = TraceWordViewModel.FromDiagnosticJson(TraceEnvelope.CapturedRuleLabel).Result;
            }
            else
            {
                fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!);
                context.TryWord("word");
                await page.Trace.TryCommand.ExecutionTask!;
            }

            var root = Assert.Single(page.Trace.RecordedRoots);
            Assert.Equal("Vowel harmony", root.Children[0].Source);
            Assert.Equal("Producer name", root.Children[0].RecordedStep.Source);
            Assert.Same(page.Trace.Reading!.Root.Children[0], root.Children[0].RecordedStep);
            Assert.Equal("Producer name", Assert.Single(page.Trace.Reading.Refs).Label);
            var window = new Window
            {
                Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1040, Height = 1600,
            };
            try
            {
                window.Show();
                Settle(window);
                Assert.Contains("Captured FieldWorks: Vowel harmony", VisibleTexts(window));
                Assert.Contains("Producer: Producer name", VisibleTexts(window));
                var link = Assert.Single(window.GetVisualDescendants().OfType<InspectLink>(), control =>
                    control.IsEffectivelyVisible && control.DataContext is TraceStepViewModel step &&
                    ReferenceEquals(step.RecordedStep, root.Children[0].RecordedStep));
                Assert.Equal(new TraceTimingKey("phon_rule", TraceEnvelope.CapturedRuleId) { IdentityQuality = "authored" }, link.Subject!.TimingKey);
                Assert.Equal("Vowel harmony", link.Subject.Label);
                Assert.Equal("authored", link.Subject.IdentityQuality);
                Assert.Contains(link.Captured!, detail => detail.Label == "Producer" && detail.Value == "Producer name");
                var expert = window.GetVisualDescendants().OfType<ExpertTracePanel>().Single();
                Assert.Contains("Producer: Producer name", VisibleTexts(expert));
                Assert.Contains("Captured FieldWorks: Vowel harmony", VisibleTexts(expert));
                page.Trace.SelectedStep = root.Children[0];
                var full = window.GetVisualDescendants().OfType<Expander>().Single(expander =>
                    AutomationProperties.GetName(expander) == "Full derivation tree");
                full.IsExpanded = true;
                page.Trace.RuleFilter = "Vowel harmony";
                Settle(window);
                Assert.Contains("Producer: Producer name", VisibleTexts(window));
                Assert.Contains("Captured FieldWorks: Vowel harmony", VisibleTexts(window));
                var fullCard = window.GetVisualDescendants().OfType<DiagnosticPanel>().Single()
                    .FindControl<Border>("DetailHost")!;
                Assert.Contains("Producer: Producer name", VisibleTexts(fullCard));
                Assert.Contains("Captured FieldWorks: Vowel harmony", VisibleTexts(fullCard));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ARecordedNameWithoutATypedKeyHasNoInspectorLink()
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            var result = WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!;
            page.Trace.Result = result with
            {
                Reading = result.Reading with
                {
                    Refs = result.Reading.Refs.Select(reference => reference with { TimingKey = null }).ToArray(),
                },
            };
            var window = new Window
            {
                Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1040, Height = 1600,
            };
            try
            {
                window.Show();
                Settle(window);
                Assert.Contains("Captured FieldWorks: Vowel harmony", VisibleTexts(window));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<InspectLink>(), link =>
                    link.IsEffectivelyVisible && link.DataContext is TraceStepViewModel);
            }
            finally { window.Close(); }
        });
    }

    private const string ProjectPath = @"C:\projects\one.fwdata";
    private readonly AvaloniaHeadlessFixture _avalonia;

    public TryWordPageTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Theory]
    [InlineData(1800000, 1, 0.9, 0.5, "50%")]
    [InlineData(400000, 0, 0.3, 0.75, "75%")]
    public void EarlierTimingUsesTheResponsesPreciseShare(long ns, int roundedMs, double selfMs,
        double share, string expected)
    {
        RunOnAvalonia(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            fake.TimingCompletesWith(DogsTiming() with
            {
                Words = [new TimingWordRow("dogs", roundedMs, TimingCompletion.Finished) { ElapsedNs = ns, Origin = DogsOrigin }],
                Aggregates = [new TimingAggregateRow("same-label-key", "Plural", selfMs, share, 1)],
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.Equal(expected, Assert.Single(page.EarlierRuleTimes).ShareText);
            Assert.Null(Assert.Single(page.EarlierRuleTimes).KindTip);
            Assert.DoesNotContain("Other time", page.EarlierRuleTimes.Select(row => row.Rule));
        });
    }

    [Fact]
    public void EarlierTimingReportsOnlyTheRecordedResidualAndOverrun()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            fake.TimingCompletesWith(DogsTiming() with
            {
                Attribution = new WordTimeAttribution(1, 10, 4, 2, 0.2, 0.7, true),
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.Equal("2 ms", page.EarlierRuleTimes.Single(row => row.IsOtherTime).TimeText);
            Assert.Equal("Morphological rules", page.EarlierRuleTimes.Single(row => !row.IsOtherTime).KindTip);
            Assert.Null(page.EarlierRuleTimes.Single(row => row.IsOtherTime).KindTip);
            Assert.Contains("0.7 ms", page.EarlierOverrunText);
            Assert.DoesNotContain("round", page.EarlierOverrunText, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpertShowsTheReturnedGrammarSourceInsteadOfClaimingItIsMissing(bool baseline)
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            page.Trace.IsExpert = true;
            var captured = Assessment().Baseline.Token;
            page.Trace.Result = DogsTrace() with
            {
                GrammarSource = baseline ? null : "Recorded producer grammar",
                HostCapture = baseline ? new TraceHostCapture(null, null, null, null, null, null, [])
                {
                    Baseline = new TraceBaselineSource(captured, DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
                        DateTimeOffset.Parse("2026-09-02T11:00:00Z"), "Returned capture description"),
                } : null,
            };
            var window = new Window { Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1040, Height = 1600 };
            try
            {
                window.Show();
                Settle(window);
                var texts = VisibleTexts(window).ToArray();
                Assert.DoesNotContain("Grammar source not recorded", texts);
                Assert.Contains(texts, text => text.Contains(baseline ? captured.CapturedUtc : "Recorded producer grammar", StringComparison.Ordinal));
                if (baseline)
                {
                    Assert.Contains(texts, text => text.Contains("2026-09-01", StringComparison.Ordinal));
                    Assert.Contains(texts, text => text.Contains("Returned capture description", StringComparison.Ordinal));
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(null, "PanGloss didn't record why.")]
    [InlineData("Returned explanation", "PanGloss refused this step here: Returned explanation")]
    public void PlainUsesTheSelectedEventsReturnedExplanation(string? explanation, string expected)
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            var response = DogsTrace();
            page.Trace.Result = response with
            {
                Reading = response.Reading with
                {
                    Root = new TraceStep("Failed", null, "in", "out", "FutureReason", [])
                    {
                        StepId = "0", ReasonExplanation = explanation,
                        FailureEvidence = new TraceFailureEvidence("decisionGate", "owner", "FutureReason", "captured",
                            null, explanation, null, null, null),
                    },
                },
            };
            page.Trace.SelectedStep = Assert.Single(page.Trace.RecordedRoots);
            var window = new Window { Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1040, Height = 1600 };
            try
            {
                window.Show();
                Settle(window);
                var texts = VisibleTexts(window).ToArray();
                Assert.Contains(expected, texts);
                Assert.DoesNotContain(texts, text => text.Contains("FutureReason", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void PlainExplainsARecordedRuleRefusalWithoutShowingItsParserEvidence()
    {
        RunOnAvalonia(async () =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            var response = MatinluTrace();
            var refusal = new TraceStep("MorphologicalRuleSynthesis", "lu", null, "tinlu",
                "NonPartialRuleProhibitedAfterFinalTemplate", [])
            {
                StepId = "0",
                OutcomeStatus = "failed",
                FailureEvidence = new TraceFailureEvidence("decisionGate", "TraceSink.failure_reason",
                    "NonPartialRuleProhibitedAfterFinalTemplate", "unavailable", null, null, null, null,
                    "owner-payload-not-captured"),
            };
            page.Trace.Result = response with { Reading = response.Reading! with { Root = refusal } };
            page.Trace.SelectedStep = Assert.Single(page.Trace.RecordedRoots);
            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1040, Height = 1600 };
            try
            {
                window.Show();
                Settle(window);
                var texts = VisibleTexts(view).ToArray();
                Assert.Contains("PanGloss refused this rule here: it can't apply after the last template.", texts);
                Assert.DoesNotContain(texts, text => text.Contains("MorphologicalRuleSynthesis", StringComparison.Ordinal));
                Assert.DoesNotContain(texts, text => text.Contains("Producer:", StringComparison.Ordinal));
                Assert.DoesNotContain(texts, text => text.Contains("Evidence source:", StringComparison.Ordinal));
                Assert.DoesNotContain(texts, text => text.Contains("Evidence reason code:", StringComparison.Ordinal));
                Assert.DoesNotContain(texts, text => text.Contains("owner-payload-not-captured", StringComparison.Ordinal));
                Assert.DoesNotContain("Recorded source analyses", texts);
                Assert.DoesNotContain(texts, text => text.StartsWith("Grammar source:", StringComparison.Ordinal));
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ExpertTraceShowsEachEventNameOnceAndLeavesMissingValuesBlank()
    {
        RunOnAvalonia(async () =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            page.Trace.Result = MatinluTrace();
            page.Trace.IsExpert = true;
            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1240, Height = 1600 };
            try
            {
                window.Show();
                Settle(window);
                var texts = VisibleTexts(view).ToArray();
                Assert.Contains("Showing every step", texts);
                Assert.DoesNotContain("Input not recorded", texts);
                Assert.DoesNotContain("Output not recorded", texts);
                Assert.DoesNotContain("Subrule not recorded", texts);
                Assert.DoesNotContain("Subrule: 0", texts);
                Assert.Equal("WordAnalysis", Assert.Single(texts, text => text.Contains("WordAnalysis", StringComparison.Ordinal)));
                var attempt = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>());
                Assert.Equal("Show one analysis path", attempt.PlaceholderText);
                var missingInput = view.GetVisualDescendants().OfType<TraceNotationToken>().First(token =>
                    ToolTip.GetTip(token)?.ToString() == "Input not recorded");
                Assert.True(missingInput.IsTabStop);
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void PlainShowsAvailabilityAndOneKeyboardReachableAnalyzeTextsAction()
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            page.Trace.WordToTry = "dogs";
            page.Trace.Result = DogsTrace();
            var window = new Window { Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1240, Height = 1600 };
            try
            {
                window.Show();
                Settle(window);
                var texts = VisibleTexts(window).ToArray();
                Assert.DoesNotContain("Grammar source not recorded", texts);
                Assert.Contains("FieldWorks word context unavailable", texts);
                Assert.Contains("Recorded trace", texts);
                var analyze = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    button.Command == page.OpenInTextsCommand);
                Assert.True(analyze.Focusable);
                Assert.Equal(0, analyze.Opacity);
                analyze.Focus();
                Settle(window);
                Assert.Equal(1, analyze.Opacity);
                Assert.True(analyze.IsHitTestVisible);
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    button.Classes.Contains("ruleRow") && button.Command == page.OpenTimingCommand);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ASelectedOccurrenceSurvivesBothMountedTreesAndFiltering()
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            page.Trace.Result = WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!;
            var window = new Window
            {
                Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1240, Height = 2400,
            };
            try
            {
                window.Show();
                Settle(window);
                var plain = window.GetVisualDescendants().OfType<TreeView>().Single(tree =>
                    AutomationProperties.GetName(tree) == "Recorded trace tree");
                SelectRule(plain);
                var occurrence = page.Trace.SelectedStep!.RecordedStep;
                Assert.Same(page.Trace.Reading!.Root.Children[0], occurrence);
                page.Trace.IsExpert = true;
                Settle(window);
                var full = window.GetVisualDescendants().OfType<Expander>().Single(expander =>
                    AutomationProperties.GetName(expander) == "Full derivation tree");
                full.IsExpanded = true;
                page.Trace.RuleFilter = "Vowel harmony";
                Settle(window);
                var expert = window.GetVisualDescendants().OfType<TreeView>().Single(tree =>
                    AutomationProperties.GetName(tree) == "Filtered full derivation tree");
                SelectRule(expert);
                Assert.Same(occurrence, page.Trace.SelectedStep!.RecordedStep);
                page.Trace.RuleFilter = "a rule absent from this trace";
                Settle(window);
                Assert.Same(occurrence, page.Trace.SelectedStep!.RecordedStep);
                Assert.Contains("Producer: Producer name", VisibleTexts(window));
                page.Trace.RuleFilter = "Vowel harmony";
                Settle(window);
                Assert.Same(occurrence, page.Trace.SelectedStep!.RecordedStep);
                full.IsExpanded = false;
                Settle(window);
                Assert.Same(occurrence, page.Trace.SelectedStep!.RecordedStep);
                page.Trace.IsExpert = false;
                Settle(window);
                Assert.Same(occurrence, page.Trace.SelectedStep!.RecordedStep);
                page.Trace.Result = WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!;
                Settle(window);
                Assert.Null(page.Trace.SelectedStep);
            }
            finally { window.Close(); }

            void SelectRule(TreeView tree)
            {
                var root = Assert.IsType<TreeViewItem>(tree.ContainerFromIndex(0));
                root.IsExpanded = true;
                Settle(window);
                var rule = Assert.IsType<TreeViewItem>(root.ContainerFromIndex(0));
                rule.IsSelected = true;
                Settle(window);
            }
        });
    }

    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void ExpandedFilteredNotationFitsInsideTheFullTreeViewport(int width)
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            page.Trace.Result = WordTraceQuery.LoadDiagnostic(File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!;
            page.Trace.RuleFilter = "lu";
            page.Trace.IsExpert = true;
            var window = new Window
            {
                Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = width, Height = 2400,
            };
            try
            {
                window.Show();
                window.GetVisualDescendants().OfType<Expander>().Single(expander =>
                    AutomationProperties.GetName(expander) == "Full derivation tree").IsExpanded = true;
                Settle(window);
                var tree = window.GetVisualDescendants().OfType<TreeView>().Single(control =>
                    AutomationProperties.GetName(control) == "Filtered full derivation tree");
                var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
                var notation = tree.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(block => block.IsEffectivelyVisible && block.Classes.Contains("traceNotation")).ToArray();
                Assert.NotEmpty(notation);
                foreach (var block in notation)
                {
                    var origin = block.TranslatePoint(default, tree)!.Value;
                    Assert.True(origin.X + block.Bounds.Width <= tree.Bounds.Width + 1,
                        $"Notation extends past the {width}px tree viewport: {block.Text}");
                }
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1,
                    $"The {width}px filtered tree requires horizontal scrolling for its wrapped notation.");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void InterruptedSearchShowsItsStatusAndKeepsTheRecordedEvents()
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            var progress = new TraceStep("MorphologicalRuleAnalysis", "Plural", "dogs", "dog", null, [])
            {
                StepId = "0.0", OutcomeStatus = "attempted",
            };
            page.Trace.Result = new WordTraceResponse("dogs", false, false, "limit", 1, null, 1,
                TraceReadingBuilder.Build("dogs", new TraceStep("WordAnalysis", null, "dogs", null, null, [progress]), [], []));
            var window = new Window
            {
                Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1040, Height = 1600,
            };
            try
            {
                window.Show();
                Settle(window);
                Assert.Contains("Search incomplete: limit", VisibleTexts(window));
                var tree = window.GetLogicalDescendants().OfType<TreeView>().Single(control =>
                    AutomationProperties.GetName(control) == "Recorded trace tree");
                var root = Assert.IsType<TraceStepViewModel>(Assert.Single(tree.Items));
                Assert.Same(page.Trace.Reading!.Root.Children[0], Assert.Single(root.Children).RecordedStep);

                page.Trace.Result = DogsTrace();
                Settle(window);
                Assert.DoesNotContain("Search incomplete: limit", VisibleTexts(window));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void PointingAtTheWordRowRevealsTheToolsMenu()
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            var window = new Window { Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1040, Height = 800 };
            try
            {
                window.Show();
                Settle(window);
                var tools = window.GetLogicalDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Try a Word tools");
                var input = window.GetLogicalDescendants().OfType<TextBox>().Single(box =>
                    AutomationProperties.GetName(box) == "Word to try");
                Assert.Equal(0, tools.Opacity);

                window.MouseMove(input.TranslatePoint(new Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value);
                Settle(window);

                Assert.Equal(1, tools.Opacity);
                Assert.True(tools.IsHitTestVisible);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ARecentWordInTheToolsMenuTracesItAgain()
    {
        _avalonia.Invoke(() =>
        {
            var page = new TryWordPageModel(NewContext(out _));
            page.RecentWords.Add("kitabu");
            var window = new Window { Content = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page), Width = 1040, Height = 800 };
            try
            {
                window.Show();
                var tools = window.GetLogicalDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Try a Word tools");
                tools.Flyout!.ShowAt(tools);
                Settle(window);

                var menu = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(tools.Flyout).Content);
                var word = menu.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Try kitabu again");
                Assert.Same(page.OpenRecentWordCommand, word.Command);
                Assert.Equal("kitabu", word.CommandParameter);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TypedWordReadsEveryOpinionBeforeAnyMainAssessment()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var approved = new ParserReading([new("dog", "dog", "n", null, false, null)])
                { StoredAnalysisId = "approved", StoredAnalysisOpinion = "approved" };
            var rejected = approved with { StoredAnalysisId = "rejected", StoredAnalysisOpinion = "disapproved" };
            var candidate = approved with { StoredAnalysisId = "candidate", StoredAnalysisOpinion = "candidate" };
            fake.WordContextHandler = (request, _) => Task.FromResult(
                SIL.Motif.Contract.Commands.CommandOutcome<WordContextResponse>.Success(new(request.Word, true)
                {
                    IsInFieldWorks = true, Analyses = [approved, rejected, candidate], ExpectedAnalysis = approved,
                }));
            var page = new TryWordPageModel(context);
            page.Trace.WordToTry = "dogs";
            await Task.Yield();
            Assert.Contains(fake.WordContextRequests, request => request.Word == "dogs");
            Assert.Equal("dog", Assert.Single(page.Trace.ExpectedMorphs).Form);
            Assert.Equal(["approved", "disapproved", "candidate"], page.WordContext!.Analyses.Select(analysis =>
                analysis.StoredAnalysisOpinion));
            Assert.Null(context.Evidence.Assessment);
        });
    }

    [Fact]
    public void ADelayedWordContextCannotReplaceTheNewWordAndUnknownMembershipStaysUnknown()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            var delayed = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<WordContextResponse>>();
            fake.WordContextHandler = (request, _) => request.Word == "old" ? delayed.Task : Task.FromResult(
                SIL.Motif.Contract.Commands.CommandOutcome<WordContextResponse>.Success(new(request.Word, false)));
            var page = new TryWordPageModel(context);
            page.Trace.WordToTry = "old";
            page.Trace.WordToTry = "new";
            Assert.Equal("new", page.WordContext!.Word);
            Assert.Null(page.WordContext.IsInFieldWorks);
            delayed.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<WordContextResponse>.Success(new("old", true)
                { IsInFieldWorks = true }));
            await Task.Yield();
            Assert.Equal("new", page.WordContext.Word);
            Assert.Empty(page.Trace.ExpectedMorphs);
        });
    }

    [Theory]
    [InlineData(false, null, "FieldWorks word context unavailable")]
    [InlineData(true, null, "FieldWorks membership not recorded")]
    [InlineData(true, false, "Word absent from the captured FieldWorks Baseline")]
    [InlineData(true, true, "No stored analyses in the captured FieldWorks Baseline")]
    public void ReturnedWordContextDistinguishesAbsenceFromEmptyAndUnavailable(bool baseline, bool? member,
        string expected)
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            fake.WordContextHandler = (request, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(
                new(request.Word, baseline) { IsInFieldWorks = member }));
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;
            var window = new Window { Content = new TryWordPanel(page), Width = 1040, Height = 1500 };
            try
            {
                window.Show();
                Settle(window);
                Assert.Contains(expected, ContextTexts(window));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ReturnedContextShowsEveryOpinionAndItsCapturedSaveAfterTheInputChanges()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            var saved = new DateTimeOffset(2026, 9, 22, 9, 18, 0, TimeSpan.Zero);
            var baseline = Assessment().Baseline.Token;
            var readings = new[] { "approved", "disapproved", "candidate" }.Select(opinion =>
                new ParserReading([new("dog", opinion, "n", null, false, null)])
                { StoredAnalysisId = opinion, StoredAnalysisOpinion = opinion }).ToArray();
            fake.WordContextHandler = (request, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(
                new(request.Word, true)
                {
                    IsInFieldWorks = true, Analyses = request.Word == "dogs" ? readings : [],
                    Baseline = baseline, SourceLastWriteUtc = saved, IsStale = true,
                }));
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;
            page.Trace.WordToTry = "cats";
            var window = new Window { Content = new TryWordPanel(page), Width = 1040, Height = 1500 };
            try
            {
                window.Show();
                Settle(window);
                var texts = ContextTexts(window);
                foreach (var opinion in new[] { "approved", "disapproved", "candidate" }) Assert.Contains(opinion, texts);
                var savedLocal = WorkspaceDateLabel(saved, context.Clock);
                Assert.Contains(texts, text => text is not null && text.Contains(savedLocal));
                Assert.DoesNotContain(texts, text => text is not null && text.Contains(saved.ToString("O")));
                Assert.Contains("FieldWorks saved since", texts);
                Assert.DoesNotContain("No stored analyses in the captured FieldWorks Baseline", texts);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ContextSaveBeforeItsBaselineDoesNotShowASavedSinceMessage()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            var baseline = Assessment().Baseline.Token;
            var saved = new DateTimeOffset(2026, 9, 5, 10, 18, 0, TimeSpan.Zero);
            fake.WordContextHandler = (request, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(
                new(request.Word, true)
                {
                    IsInFieldWorks = true, Baseline = baseline, SourceLastWriteUtc = saved, IsStale = true,
                }));
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;
            var window = new Window { Content = new TryWordPanel(page), Width = 1040, Height = 1500 };
            try
            {
                window.Show();
                Settle(window);
                var texts = ContextTexts(window);
                Assert.DoesNotContain("FieldWorks saved since", texts);
                var savedLocal = WorkspaceDateLabel(saved, context.Clock);
                Assert.Contains(texts, text => text is not null && text.Contains(savedLocal));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ADelayedResultContextCannotReplaceANewerDisplayedWord()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            var delayed = new TaskCompletionSource<CommandOutcome<WordContextResponse>>();
            fake.WordContextHandler = (request, _) => request.Word == "dogs" ? delayed.Task : Task.FromResult(
                CommandOutcome<WordContextResponse>.Success(new(request.Word, true) { IsInFieldWorks = false }));
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;
            fake.TraceWordCompletesWith(DogsTrace() with { Word = "cats" });
            context.TryWord("cats");
            await page.Trace.TryCommand.ExecutionTask!;
            delayed.SetResult(CommandOutcome<WordContextResponse>.Success(new("dogs", true)
                { IsInFieldWorks = true }));
            await Task.Yield();
            Assert.Equal("cats", page.ResultWordContext!.Word);
            Assert.Equal("Word absent from the captured FieldWorks Baseline", page.FieldWorksContextStatus);
            Assert.Empty(page.FieldWorksAnalyses);
        });
    }

    private static string?[] ContextTexts(Window window) => window.GetVisualDescendants().OfType<Border>()
        .Single(border => AutomationProperties.GetName(border) == "FieldWorks beside the parser")
        .GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
        .Select(block => block.Text).ToArray();

    private static string WorkspaceDateLabel(DateTimeOffset value, TimeProvider clock)
    {
        var local = TimeZoneInfo.ConvertTime(value, clock.LocalTimeZone);
        return local.Date == clock.GetLocalNow().Date
            ? local.ToString("t", CultureInfo.CurrentCulture) + " today"
            : local.ToString("ddd d MMM, ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
    }

    [Fact]
    public void RecentWordsSitApartSoTwoWordsNeverReadAsOne()
    {
        _avalonia.Invoke(() =>
        {
            var model = new TryWordPageModel(NewContext(out _));
            model.RecentWords.Add("kitabu");
            model.RecentWords.Add("matinlu");
            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            var window = new Window { Content = view, Width = 1240, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                var tools = window.GetLogicalDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Try a Word tools");
                tools.Focus();
                tools.Flyout!.ShowAt(tools);
                Settle(window);

                var menu = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(tools.Flyout).Content);
                var list = menu.GetLogicalDescendants().OfType<ItemsControl>()
                    .Single(control => AutomationProperties.GetName(control) == "Recent words");
                var words = list.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.Text is "kitabu" or "matinlu")
                    .Select(text => new Rect(text.TranslatePoint(default, window)!.Value, text.Bounds.Size))
                    .ToArray();
                Assert.Equal(2, words.Length);
                Assert.True(Application.Current!.TryGetResource("Intent.Space.Related", null, out var gap));
                var apart = words[0].Top == words[1].Top ? words[1].Left - words[0].Right : words[1].Top - words[0].Bottom;
                Assert.True(apart >= (double)gap! - 0.5, $"the two recent words are {apart:F1} px apart");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RegistryBuildsTheTryAWordPageWithNamedLinksAndNoApprovalOutsideTheText()
    {
        _avalonia.Invoke(() =>
        {
            var context = NewContext(out _);
            var model = new TryWordPageModel(context);
            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal("TryAWordPage", view.GetType().Name);
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "AI Handoff for this word");
                // Opinions change only in the text, so the page offers the text rather than an approval.
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    (AutomationProperties.GetName(button) ?? string.Empty).Contains("Approve", StringComparison.Ordinal));
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Open in Analyze texts");
                // Opening a saved trace is a tool, so it waits behind the menu beside Try it.
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Open a saved diagnostic");
                var tools = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Try a Word tools");
                // The flyout keeps the trace's capture details and writing systems the embedded panel no longer shows.
                var menu = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(tools.Flyout).Content);
                var names = menu.GetLogicalDescendants().OfType<CopyableTextBlock>()
                    .Select(block => AutomationProperties.GetName(block)).ToList();
                Assert.Contains("Diagnostic capture details", names);
                Assert.Contains("Writing system direction and font", names);
                Assert.Contains(window.GetLogicalDescendants().OfType<Expander>(), expander =>
                    Equals(expander.Header, "Aggregate parser effort by category"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ATypedWordCanBeTriedWithoutTextDataAndItsRuleRowsUseOnlyStoredPerRuleTiming()
    {
        RunOnAvalonia(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var step = new TraceStep("MorphologicalRuleAnalysis", "Plural", "dog", "dogs", null, [])
            {
                OutcomeStatus = "succeeded",
            };
            fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
                TraceReadingBuilder.Build("dogs", new TraceStep("WordAnalysis", null, null, null, null, []), [new TraceCandidate([], true, null, "Built the word", [step])], [])));
            fake.TimingCompletesWith(new TimingResponse("assessment-1", "selected", "rule", 1, 10, 10, [],
                [new TimingAggregateRow("Plural", "Plural", 4, 0.4, 1) { Kind = "morph_rule" }], [])
            {
                Words = [new TimingWordRow("dogs", 10, TimingCompletion.Finished) { ElapsedNs = 10_000_000, Origin = DogsOrigin }],
                Attribution = new WordTimeAttribution(1, 10, 4, 6, 0.6, 0, false),
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            // The stored share is of the word's whole parse time in that parse, not of the rules' recorded time.
            Assert.True(page.HasEarlierTiming);
            Assert.Equal("Share of 10 ms", page.EarlierShareHeader);
            Assert.Equal([("Plural", "Morphological rules", "4 ms", "40%"), ("Other time", "", "6 ms", "60%")],
                page.EarlierRuleTimes.Select(time => (time.Rule, time.KindLabel, time.TimeText, time.ShareText)));
            Assert.Contains("dogs", page.RecentWords);
            var request = Assert.Single(fake.TimingRequests);
            Assert.Equal("rule", request.By);
            Assert.Equal("assessment-parse", request.AssessmentId);
            Assert.Equal(["dogs"], request.ExplicitWords);
        });
    }

    [Fact]
    public void AStoredAssessmentSuppliesRuleTimingForANewTrace()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
                TraceReadingBuilder.Build("dogs", new TraceStep("WordAnalysis", null, null, null, null, []), [new TraceCandidate([], true, null, "Built the word", [
                    new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])])], [])));
            fake.TimingCompletesWith(new TimingResponse("assessment-1", "selected", "rule", 1, 10, 10, [],
                [new TimingAggregateRow("Plural", "Plural", 4, 0.4, 1)], [])
            {
                Words = [new TimingWordRow("dogs", 10, TimingCompletion.Finished) { Origin = DogsOrigin }],
            });

            await context.PublishCurrentEvidenceAsync(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow, null,
                EvidenceFreshness.Current, null, null, null, null, StoredAssessment()));
            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.Equal("4 ms", Assert.Single(page.EarlierRuleTimes).TimeText);
            Assert.Equal("assessment-1", Assert.Single(fake.TimingRequests).AssessmentId);
        });
    }

    [Fact]
    public void TimingFromAnEarlierParseSaysWhenItRanAndThatItIsNotThisTry()
    {
        RunOnAvalonia(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            fake.TimingCompletesWith(DogsTiming());
            var parsedAt = DogsOrigin.MeasuredUtc;
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), parsedAt, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            var when = parsedAt.ToLocalTime().ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture);
            Assert.Equal($"Time from the parse of {when}", page.EarlierTimingTitle);
            Assert.Equal("Not from this traced search. In that parse dogs took 10 ms; each rule's time covers that parse's " +
                "whole search for the word, including attempts that stopped.",
                page.EarlierTimingSource);

            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1240, Height = 1600 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var bestPath = Assert.Single(window.GetLogicalDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetName(border) == "Recorded trace context");
                var texts = bestPath.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
                Assert.Contains("Recorded trace", texts);
                Assert.DoesNotContain(texts, text => text is not null &&
                    (text.Contains("time", StringComparison.OrdinalIgnoreCase) || text.EndsWith('%')));
                var earlier = Assert.Single(window.GetLogicalDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetName(border) == "Time from an earlier parse");
                Assert.True(earlier.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ReturnedTimingOriginNamesTheWordsParseAfterARerun()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            fake.TimingCompletesWith(DogsTiming() with
            {
                Words = [new TimingWordRow("dogs", 10, TimingCompletion.Finished)
                {
                    ElapsedNs = 10_000_000,
                    Origin = new WordMeasurementOrigin("rerun-1", "invocation-rerun", DateTimeOffset.Parse("2026-09-25T12:00:00Z")),
                }],
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment() with { TimingOverrideAssessmentIds = ["rerun-1"] },
                DateTimeOffset.UtcNow, WasRerun: true));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.True(page.HasEarlierTiming);
            Assert.Contains(DateTimeOffset.Parse("2026-09-25T12:00:00Z").ToLocalTime().ToString("ddd d MMM, h:mm tt",
                CultureInfo.CurrentCulture), page.EarlierTimingTitle);
        });
    }

    private static WordTraceResponse DogsTrace() => new("dogs", true, true, null, 1, null, 10,
        TraceReadingBuilder.Build("dogs", new TraceStep("WordAnalysis", null, null, null, null, []),
            [new TraceCandidate([], true, null, "Built the word", [
                new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, []) { OutcomeStatus = "succeeded" }])], []));

    private static WordTraceResponse MatinluTrace() => WordTraceQuery.LoadDiagnostic(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!;

    private static readonly WordMeasurementOrigin DogsOrigin = new("assessment-parse", "invocation/one",
        DateTimeOffset.Parse("2026-09-22T09:18:00Z"));

    [Fact]
    public void TimingWithoutAReturnedOriginCannotBorrowTheWorkspacesDate()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            fake.TimingCompletesWith(DogsTiming() with
            {
                Words = [new TimingWordRow("dogs", 10, TimingCompletion.Finished) { ElapsedNs = 10_000_000 }],
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));
            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;
            Assert.False(page.HasEarlierTiming);
            Assert.Empty(page.EarlierRuleTimes);
        });
    }

    private static TimingResponse DogsTiming() => new("assessment-parse", "selected", "rule", 1, 10, 10, [],
        [new TimingAggregateRow("Plural", "Plural", 4, 0.4, 1) { Kind = "morph_rule" }], [])
    {
        Words = [new TimingWordRow("dogs", 10, TimingCompletion.Finished) { ElapsedNs = 10_000_000, Origin = DogsOrigin }],
    };

    [Fact]
    public void TimingAndWordLinksRouteThroughTheWorkspaceContext()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var timing = new TimingPageModel(context);
            var texts = new TextsPageModel(context);
            var handoff = new AiHandoffPageModel(context);
            fake.TraceWordCompletesWith(new WordTraceResponse("typed-only", false, true, null, 1, null, 1,
                TraceReadingBuilder.Build("typed-only", new TraceStep("WordAnalysis", null, null, null, null, []), [new TraceCandidate([], false, "failure", "Stopped", [
                    new TraceStep("MorphologicalRule", "Plural", "typed-only", null, "failure", [])
                    { OutcomeStatus = "failed", SourceIdentityKind = "morphRule", SourceIdentityId = "plural-key",
                        SourceIdentityQuality = "authored" }])], [])));

            context.TryWord("typed-only");
            await page.Trace.TryCommand.ExecutionTask!;
            page.OpenTimingCommand.Execute(null);
            Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
            Assert.Null(timing.Focus!.Rule);
            Assert.Equal(["typed-only"], timing.Focus.Words);

            page.OpenInTextsCommand.Execute(null);
            Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
            Assert.Equal(TextsTab.AnalyzeTexts, texts.Tab);

            page.HandOffCommand.Execute(null);
            Assert.Equal(WorkspacePage.AiHandoff, context.CurrentPage);
            Assert.Equal(["typed-only"], handoff.Handoff.ChosenWords);
        });
    }

    [Theory]
    [InlineData("cats", false)]
    [InlineData("", false)]
    [InlineData("cats", true)]
    [InlineData("", true)]
    public void ResultActionsKeepTheReturnedWordAfterInputEdits(string edited, bool duringTrace)
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var timing = new TimingPageModel(context);
            var handoff = new AiHandoffPageModel(context);
            var requests = new WordRequestObserver(context);
            var completion = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>();
            fake.OnTraceWord((_, _) => completion.Task);
            var changes = 0;
            page.OpenTimingCommand.CanExecuteChanged += (_, _) => changes++;
            Assert.False(page.OpenTimingCommand.CanExecute(null));
            context.TryWord("dogs");
            var run = page.Trace.TryCommand.ExecutionTask!;
            if (duringTrace) page.Trace.WordToTry = edited;
            completion.SetResult(CommandOutcome<WordTraceResponse>.Success(DogsTrace()));
            await run;
            if (!duringTrace) page.Trace.WordToTry = edited;
            var resultChanges = changes;
            Assert.Equal("dogs", page.Trace.Result!.Word);
            Assert.True(page.OpenTimingCommand.CanExecute(null));
            Assert.True(page.OpenInTextsCommand.CanExecute(null));
            Assert.True(page.HandOffCommand.CanExecute(null));
            page.OpenTimingCommand.Execute(null);
            Assert.Equal(["dogs"], timing.Focus!.Words);
            Assert.Null(timing.Focus.Rule);
            page.OpenInTextsCommand.Execute(null);
            Assert.Equal("dogs", requests.Word);
            page.HandOffCommand.Execute(null);
            Assert.Equal(["dogs"], handoff.Handoff.ChosenWords);
            Assert.Same(page.Trace.Result, handoff.Handoff.SelectedTrace);
            page.Trace.Result = null;
            Assert.False(page.OpenTimingCommand.CanExecute(null));
            Assert.False(page.OpenInTextsCommand.CanExecute(null));
            Assert.False(page.HandOffCommand.CanExecute(null));
            Assert.True(changes > resultChanges);
        });
    }

    private sealed class WordRequestObserver(WorkspaceContext context) : PageModel(context)
    {
        public string? Word { get; private set; }
        protected override void OnRequested(PageRequest request)
        {
            if (request is OpenWordRequest word) Word = word.Word;
        }
    }

    [Fact]
    public void GeneralTimingActionDoesNotChooseAnArbitraryRule()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var timing = new TimingPageModel(context);
            fake.TraceWordCompletesWith(new WordTraceResponse("verb", true, true, null, 1, null, 1,
                TraceReadingBuilder.Build("verb", new TraceStep("WordAnalysis", null, null, null, null, []), [new TraceCandidate([], true, null, "Built the word", [
                    new TraceStep("MorphologicalRule", "Verb template", "stem", "verb", null, [])
                    { OutcomeStatus = "succeeded", SourceIdentityKind = "morphRule", SourceIdentityId = "verb-key",
                        SourceIdentityQuality = "authored" }])], [])));

            context.TryWord("verb");
            await page.Trace.TryCommand.ExecutionTask!;

            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var title = Assert.Single(window.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    AutomationProperties.GetName(block) == "Try a Word result");
                Assert.Equal("verb", title.Text);
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    AutomationProperties.GetName(block) == "Try a Word page description");
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    AutomationProperties.GetName(block) == "Try a Word screenshot note");
                var ruleTable = Assert.Single(window.GetLogicalDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetName(border) == "Recorded trace context");
                var headers = ruleTable.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(block => block.IsVisible)
                    .Select(block => block.Text)
                    .OfType<string>()
                    .Select(value => value.Trim())
                    .Where(value => value is "Rule" or "Kind" or "Outcome" or "Explanation" or
                        "Stored time" or "Attempts" or "Word share").ToArray();
                Assert.Empty(headers);
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Timing for this word");

                page.OpenTimingCommand.Execute(null);

                Assert.Null(timing.Focus!.Rule);
                Assert.Equal(["verb"], timing.Focus.Words);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SidebarTimingOmitsUnresolvedRulesForTheReturnedWord(bool supplyUnresolvedKey)
    {
        RunOnAvalonia(async () =>
        {
            var diagnostic = WordTraceQuery.LoadDiagnostic(TraceEnvelope.UnresolvedRuleTiming);
            Assert.True(diagnostic.Succeeded, diagnostic.Refusal?.Message);
            var response = diagnostic.Value!;
            if (supplyUnresolvedKey)
            {
                var rule = Assert.Single(response.Reading.Refs);
                response = response with
                {
                    Reading = response.Reading with
                    {
                        Refs = [rule with
                        {
                            TimingKey = new TraceTimingKey("morph_rule", "") { IdentityQuality = "structural" },
                        }],
                    },
                };
            }
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            fake.TraceWordCompletesWith(response);
            var page = new TryWordPageModel(context);
            var timing = new TimingPageModel(context);

            context.TryWord("word");
            await page.Trace.TryCommand.ExecutionTask!;
            page.Trace.WordToTry = "edited";

            Assert.False(Assert.Single(page.Trace.RecordedRoots).CanInspect);
            Assert.True(page.OpenTimingCommand.CanExecute(null));
            page.OpenTimingCommand.Execute(null);

            Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
            Assert.Equal(["word"], timing.Focus!.Words);
            Assert.Null(timing.Focus.Rule);
            Assert.Null(timing.Focus.Label);
        });
    }

    [Fact]
    public void CategoryTotalsRemainAggregateAndAreNeverCopiedToARuleStep()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var effort = new TraceEffort("Morphology", 3, 1, 0, 0, 0, 2, 8);
            fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
                TraceReadingBuilder.Build("dogs", new TraceStep("WordAnalysis", null, null, null, null, []), [new TraceCandidate([], true, null, "Built the word", [
                    new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])])], []))
            {
                Effort = [effort],
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.False(page.HasEarlierTiming);
            Assert.Empty(page.EarlierRuleTimes);
            Assert.Equal("8.0 ms", Assert.Single(page.Trace.Effort).Time);
        });
    }

    [Fact]
    public void TheSeededTraceShowsLogicalSummariesBeforeRecordedContext()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = context.Assess.ProjectPath = ProjectPath;
            fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!);
            var page = new TryWordPageModel(context);
            context.TryWord("matinlu");
            await page.Trace.TryCommand.ExecutionTask!;
            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1240, Height = 2400 };
            try
            {
                window.Show();
                Settle(window);
                var visible = VisibleTexts(view).ToArray();
                Assert.Contains("Parsed: 2 analyses", visible);
                Assert.Contains("Morphemes: MA + TIN + LU", visible);
                Assert.DoesNotContain("Recorded twice", visible);
                Assert.Single(visible, text => text == "Analysis 1");
                Assert.True(TopOf(view, "Parsed: 2 analyses", window) < TopOf(view, "Recorded trace", window));
                Assert.DoesNotContain("Why the other attempts stopped", visible);
                Assert.DoesNotContain("Further derivation is prohibited after a final template.", visible);
                Assert.DoesNotContain("Rules on this word's best path", visible);
                var plainDetails = view.GetVisualDescendants().OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible && block.Classes.Contains("traceNotation")).ToArray();
                Assert.DoesNotContain(plainDetails, block => block.Text!.Contains("WordAnalysis", StringComparison.Ordinal));
                var full = window.GetLogicalDescendants().OfType<Expander>().Single(expander =>
                    AutomationProperties.GetName(expander) == "Full derivation tree");
                Assert.False(full.IsExpanded);
            }
            finally { window.Close(); }
        });
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static IEnumerable<string> VisibleTexts(Control view) => view.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
        .Select(block => block.Text!.Trim()).ToArray();

    private static double TopOf(Control view, string text, Window window) => view.GetVisualDescendants()
        .OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == text)
        .TranslatePoint(default, window)!.Value.Y;

    private static void RunOnAvalonia(Func<Task> work) =>
        AvaloniaHeadlessFixture.RunUntilComplete(work, TimeSpan.FromSeconds(10));

    private static (WorkspaceContext Context, FakeCommandClient Fake) NewContext()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return (new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake), fake,
            new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(fake)), fake);
    }

    private static WorkspaceContext NewContext(out FakeCommandClient fake)
    {
        (var context, fake) = NewContext();
        return context;
    }

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(new BaselineToken("project-1", "sha256:" + new string('a', 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64)), ProjectPath, DateTimeOffset.UtcNow,
            false, false), new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
            Measurements = [new ProducedAssessmentReference("assessment-1", "ObjectTiming", "invocation/one"),
                new ProducedAssessmentReference("assessment-parse", "ParseTime", "invocation/one")],
        };

    private static AssessmentRecord StoredAssessment() => new(
        "assessment-1", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
        "whitespace", "1", "{}", Selection.Create("Default", ["dogs"]), null, null,
        "sha256:grammar", null, null, null, "2026-09-24T12:00:00.0000000+00:00", Words: []);

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(Avalonia.Input.PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, Avalonia.Input.DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
