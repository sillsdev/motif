using SIL.Motif.App.ViewModels;
using SIL.Motif.Generator;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalWorkflowModelTests
{
    private const string Path = @"C:\projects\one.fwdata";
    private const string Id = "proposal/one";

    [Fact]
    public void TheVerdictDoesNotClaimNoLossesWithoutComparableEvidence()
    {
        var trial = new TrialSummary("job/one", DateTimeOffset.UnixEpoch, TrialFreshness.Current,
            TrialWordScope.AllWords, new Paired<double?>(null, null, false, false, false, null),
            new Paired<int>(43, 43, true, true, false, null), new Paired<int>(52, 52, true, true, false, null),
            new Paired<double?>(null, null, false, false, false, null),
            new Paired<int?>(null, null, false, false, false, null), 0, 0);
        var card = new ProposalCard(Id, "Sound change", "proposed", ProposalGroup.WaitingForYou,
            false, [], TrialStanding.Current, trial, []);

        Assert.Equal(TrialStandingWords.Current.Sentence, new ProposalCardViewModel(card).VerdictText);
        Assert.Equal("Keeps 43 of 52 approved, loses none.",
            new ProposalCardViewModel(card with { Trial = trial with { LostApprovedKnown = true } }).VerdictText);
        Assert.Equal(TrialStandingWords.Current.Sentence, new ProposalCardViewModel(card with
        {
            Trial = trial with { LostApprovedKnown = true, ApprovedTotal = trial.ApprovedTotal with { HasBefore = false } },
        }).VerdictText);
    }

    [Fact]
    public async Task SelectingAProposalReadsItsRationaleAndDescribesEveryOperation()
    {
        var fake = NewFake();
        fake.ProposalDetail = new ProposalDetailProjection(Id, "proposed", "Sound change",
            new RationaleView("Why this helps", "old", "2026-10-01", true, null, true), "new",
            [new ProposalOperationView("op/one", "grammar/phPhonData/createPhonRules", null, null, [], null),
             new ProposalOperationView("op/two", "analysis/langProject/createWordforms", null, null, [], null)]);
        var change = new PendingChange("change/one", "word/one", "word", "approve", null, null, ["op/two"]);
        fake.Review = fake.Review with
        {
            Entries = [new WordChangeEntry("change/one", 0, "Approve", ["op/two"], [], "digest", false,
                change, null, default)],
        };
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        await page.DetailReading;

        Assert.Equal("Why this helps", page.Detail!.Rationale!.Markdown);
        Assert.Equal("Why this helps", page.RationaleEditor!.Markdown);
        Assert.True(page.RationaleEditor.CanStartEdit);
        Assert.True(page.Detail.Rationale.OutOfDate);
        Assert.Equal(["Add a phonological rule.", "Add a wordform (affects 1 word)."],
            page.OperationDescriptions);
    }

    [Fact]
    public async Task ASlowDetailReadCannotReplaceTheNewSelection()
    {
        var firstRead = new TaskCompletionSource<CommandOutcome<ProposalDetailProjection>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = NewFake();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
        [
            new ProposalCard(Id, "First", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []),
            new ProposalCard("proposal/two", "Second", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []),
        ], 0));
        fake.ShowProposalHandler = (request, _) => request.ProposalId == Id ? firstRead.Task : Task.FromResult(
            CommandOutcome<ProposalDetailProjection>.Success(
                new ProposalDetailProjection(request.ProposalId, "proposed", "Second", null, null, [])));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        var oldRead = page.DetailReading;
        var second = page.Rows.OfType<ProposalCardViewModel>().Single(card => card.ProposalId == "proposal/two");

        page.SelectProposalCommand.Execute(second);
        await page.DetailReading;
        firstRead.SetResult(CommandOutcome<ProposalDetailProjection>.Success(
            new ProposalDetailProjection(Id, "proposed", "First", null, null, [])));
        await oldRead;

        Assert.Equal("proposal/two", page.Detail!.ProposalId);
    }

    [Fact]
    public async Task DecliningSavesTheReasonBeforeTheProposalCloses()
    {
        var fake = NewFake();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        var selected = page.SelectedProposal!;
        page.AskToDeclineCommand.Execute(selected);
        page.DeclineReason = "The analysis needs work.";

        await page.DeclineCommand.ExecuteAsync(selected);

        var request = Assert.Single(fake.DeclineProposalRequests);
        Assert.Equal("The analysis needs work.", request.Reason);
        Assert.Equal(Id, request.ProposalId);
        Assert.Equal("Declined.", page.DecisionNotice);
    }

    [Fact]
    public async Task DecidingLaterMovesTheReadyProposalToDeferred()
    {
        var fake = NewFake();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);

        await page.DecideLaterCommand.ExecuteAsync(page.SelectedProposal);

        Assert.Equal(Id, Assert.Single(fake.DeferProposalRequests).ProposalId);
        Assert.Empty(fake.DeclineProposalRequests);
    }

    [Fact]
    public async Task AProposalBecomesReadOnlyWhenAPersonSelectsItNotWhenTheListSelectsIt()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [new ProposalCard(Id, "Sound change", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []) { IsUnread = true }], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        await page.DetailReading;
        context.OpenPage(WorkspacePage.Proposals);
        await page.ReadMarkWriting;
        Assert.True(page.SelectedProposal!.IsUnread);
        Assert.Empty(fake.ReviewMarkRequests);

        page.SelectProposalCommand.Execute(page.SelectedProposal);
        await page.ReadMarkWriting;

        Assert.False(page.SelectedProposal.IsUnread);
        Assert.Equal(ReviewMarkKeys.ProposalRead, Assert.Single(fake.ReviewMarkRequests).EntryKey);
    }

    [Fact]
    public async Task AListReloadThatReselectsTheProposalDoesNotMarkItRead()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [new ProposalCard(Id, "Sound change", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []) { IsUnread = true }], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        context.OpenPage(WorkspacePage.Proposals);
        await context.OpenProjectAsync(Path);
        await page.DetailReading;
        await page.ReadMarkWriting;

        Assert.True(page.SelectedProposal!.IsUnread);
        Assert.Empty(fake.ReviewMarkRequests);
    }

    [Theory]
    [InlineData("proposed", false, ProposalGroup.WaitingForYou, "Open")]
    [InlineData("proposed", false, ProposalGroup.NotTried, "Try")]
    [InlineData("proposed", false, ProposalGroup.NeedsAnotherTry, "Try")]
    [InlineData("deferred", false, ProposalGroup.NotTried, "Try")]
    [InlineData("proposed", true, ProposalGroup.NotTried, "Try")]
    [InlineData("rejected", false, ProposalGroup.SetAside, "Reopen")]
    [InlineData("withdrawn", false, ProposalGroup.SetAside, "Reopen")]
    [InlineData("rejected", true, ProposalGroup.NotTried, "Try")]
    [InlineData("superseded", false, ProposalGroup.SetAside, "Open")]
    [InlineData("applied", false, ProposalGroup.SetAside, "Open")]
    public void EveryStateHasExactlyOnePrimaryButton(string status, bool isDraft, ProposalGroup group, string expected)
    {
        var standing = group == ProposalGroup.NeedsAnotherTry
            ? TrialStanding.NeedsAnotherTry(TrialStaleReason.ProposalChanged)
            : group == ProposalGroup.WaitingForYou ? TrialStanding.Current : TrialStanding.NotTried;
        var card = new ProposalCardViewModel(new ProposalCard(Id, "Sound change", status, group, false, [],
            standing, null, []) { IsDraft = isDraft });

        var primary = new[] { (card.CanTry, "Try"), (card.IsReopenPrimary, "Reopen"), (card.IsOpenPrimary, "Open") }
            .Where(button => button.Item1).Select(button => button.Item2).ToArray();

        Assert.Equal([expected], primary);
    }

    [Fact]
    public void OnlyTheNamedButtonsCarryThePrimaryClassInTheInspector()
    {
        var xaml = File.ReadAllText(System.IO.Path.Combine(
            RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App", "Views", "Pages", "ProposalsPage.axaml"));
        var start = xaml.IndexOf("ProposalInspectorActionsTemplate", StringComparison.Ordinal);
        var end = xaml.IndexOf("</DataTemplate>", start, StringComparison.Ordinal);
        var section = xaml[start..end];
        var primaries = System.Text.RegularExpressions.Regex.Matches(section, @"Classes\.Primary=""\{Binding (\S+?)\}""|Classes=""Primary""");

        Assert.Equal(new[] { "", "", "SelectedProposal.IsOpenPrimary", "SelectedProposal.IsReopenPrimary" },
            primaries.Select(match => match.Groups[1].Value).Order().ToArray());
    }

    [Fact]
    public void NamingWhatAnOperationMakesDependsOnItsKindNotOnTheEnglishOfItsTitle()
    {
        var namedKind = SIL.Motif.Commands.ChangeVocabulary.Of("grammar/phPhonemeSet/createPhonemes")
            with { Title = "Ajouter un phonème" };
        var otherKind = SIL.Motif.Commands.ChangeVocabulary.Of("analysis/wfiWordform/createAnalysis")
            with { Title = "Ajouter une analyse" };
        var named = ProposalOperationSentence.For(new ProposalOperationView("op/a",
            "grammar/phPhonemeSet/createPhonemes", null, null, [], "{\"text\":\"ɸ\"}"), 0, namedKind);
        var notMaking = ProposalOperationSentence.For(new ProposalOperationView("op/b",
            "analysis/wfiWordform/createAnalysis", null, null, [], "{\"text\":\"ɸ\"}"), 0, otherKind);

        Assert.Contains("ɸ", named);
        Assert.DoesNotContain("ɸ", notMaking);
        Assert.True(SIL.Motif.Commands.ChangeVocabulary.MakesNamedThing("lexical/lexEntry/createLexemeForm"));
        Assert.False(SIL.Motif.Commands.ChangeVocabulary.MakesNamedThing("lexical/lexEntry/deleteLexemeForm"));
        Assert.False(SIL.Motif.Commands.ChangeVocabulary.MakesNamedThing("unknown/kind"));
    }

    [Fact]
    public async Task AnOperationThatNamesWhatItMakesSaysSoAndOmitsAnUnknownCount()
    {
        var fake = NewFake();
        fake.ProposalDetail = new ProposalDetailProjection(Id, "proposed", "Suffix", null, "new",
            [new ProposalOperationView("op/one", "lexical/lexEntry/createLexemeForm", null, null, [],
                "{\"morphType\":\"x\",\"ws\":\"seh\",\"text\":\"-an\"}"),
             new ProposalOperationView("op/two", "grammar/phPhonData/createPhonRules", null, null, [], "{}")]);
        var change = new PendingChange("change/one", "word/one", "word", "approve", null, null, ["op/one"]);
        fake.Review = fake.Review with
        {
            Entries = [new WordChangeEntry("change/one", 0, "Approve", ["op/one"], [], "digest", false,
                change, null, default)],
        };
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        await page.DetailReading;

        Assert.Equal(["Adds the allomorph -an (affects 1 word).", "Add a phonological rule."],
            page.OperationDescriptions);
        Assert.DoesNotContain(page.OperationDescriptions, text => text.Contains("not available"));
    }

    private static FakeCommandClient NewFake()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [new ProposalCard(Id, "Sound change", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, [])], 0));
        return fake;
    }
}
