using System.Linq;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the Warnings page's finding rows, word evidence, filters and refresh state.</summary>
public sealed class GrammarWarningsViewModelTests
{
    private static readonly GrammarWarning EntryWarning = new(
        GrammarDiagnosticLevel.Warning,
        "Entry",
        [new("lex entry", GrammarWarningPartRole.Text), new("kuona", GrammarWarningPartRole.Object,
            "11111111-1111-1111-1111-111111111111", "Entry", "silfw://localhost/link?x")],
        [new("does not resolve within this entry", GrammarWarningPartRole.Text)],
        "warning: future.entry: does not resolve within this entry")
    {
        Group = "PanGloss entry group",
        Code = "future.entry",
        Description = "PanGloss says the entry's grammatical info cannot be read.",
        Guidance = "Choose the entry's grammatical info in FieldWorks.",
        Origin = GrammarFindingOrigin.Import,
    };

    private static readonly GrammarWarning PhonemeWarning = new(
        GrammarDiagnosticLevel.Information,
        "Phoneme",
        [new("phoneme", GrammarWarningPartRole.Text), new("ng", GrammarWarningPartRole.Object,
            "22222222-2222-2222-2222-222222222222", "Phoneme")],
        [new("is not modelled", GrammarWarningPartRole.Text)],
        "info: future.phoneme: is not modelled")
    {
        Group = "PanGloss phoneme group",
        Code = "future.phoneme",
        Description = "PanGloss says the phoneme is not modelled.",
        Origin = GrammarFindingOrigin.Check,
    };

    [Fact]
    public void TheRowUsesPanGlossTextAndDoesNotInventAdvice()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, PhonemeWarning]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(table.Rows
            .Cast<GrammarWarningRowViewModel>().Single(item => item.GroupCode == EntryWarning.Code));

        Assert.Equal("PanGloss entry group", row.PanGlossTitle);
        Assert.Equal("PanGloss says the entry's grammatical info cannot be read.", row.Message);
        Assert.Equal("Choose the entry's grammatical info in FieldWorks.", row.PanGlossGuidance);
        Assert.False(row.HasExplanation);
        Assert.False(row.HasHelp);
    }

    [Fact]
    public void NoGroupOrDescriptionDoesNotGetMotifFallbackText()
    {
        var warning = EntryWarning with { Group = null, Title = null, CodeLabel = "", Description = string.Empty, Guidance = null };
        var table = new GrammarWarningsViewModel();
        table.Load([warning]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));

        Assert.Equal(string.Empty, row.PanGlossTitle);
        Assert.Equal(string.Empty, row.Message);
        Assert.False(row.HasGuidance);
        Assert.False(row.HasExplanation);
    }

    [Fact]
    public void ProblemDetailsStaySeparateOnlyWhenTheyAddEvidenceBeyondTheDescription()
    {
        const string description = "PanGloss's full description.";
        var warning = EntryWarning with
        {
            Description = description,
            Problem = [new GrammarWarningPart(description, GrammarWarningPartRole.Text)],
        };

        Assert.False(new GrammarWarningRowViewModel(warning).HasProblemParts);
        Assert.True(new GrammarWarningRowViewModel(warning with
        {
            Problem = [new GrammarWarningPart("Typed or separate evidence", GrammarWarningPartRole.Text)],
        }).HasProblemParts);
    }

    [Fact]
    public void SeverityCountsAndBucketsFollowTheReportLevel()
    {
        var error = EntryWarning with { Code = "future.entry.error", Severity = GrammarDiagnosticLevel.Error };
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, error, PhonemeWarning]);

        Assert.Equal(1, table.ErrorCount);
        Assert.Equal(1, table.WarningCount);
        Assert.Equal(1, table.InformationCount);

        table.SetBucketCommand.Execute(GrammarFindingBucket.Errors);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal("error", row.Severity);
        Assert.True(row.IsError);
        Assert.Equal("1 error, 1 warning, 1 information finding.", table.BreakdownText);
        Assert.Equal(Mark.Error, Assert.Single(table.Rows.Cast<GrammarWarningRowViewModel>()).SeverityMark);
    }

    [Fact]
    public void TouchYourWordsShowsExactUsesAndLeavesSpellingMatchesOut()
    {
        var exact = WithWords(EntryWarning with { Code = "grammar.exact" }, WarningWordsMatch.Identity,
            "exact-one", "exact-two");
        var spelling = WithWords(PhonemeWarning with { Code = "grammar.spelling" }, WarningWordsMatch.Spelling,
            "spelling-one");
        var noWords = EntryWarning with
        {
            Code = "grammar.none",
            YourWords = new WarningWords(WarningWordsMatch.Identity, [], []),
        };
        var table = new GrammarWarningsViewModel();
        table.Load([exact, spelling, noWords]);

        var rows = table.Rows.Cast<GrammarWarningRowViewModel>().ToDictionary(row => row.GroupCode);
        Assert.Equal(WarningDisplayState.ExactUses, rows["grammar.exact"].AttributionState);
        Assert.Equal("2 of your words", rows["grammar.exact"].ReachSummaryText);
        Assert.Equal(WarningDisplayState.SpellingCandidates, rows["grammar.spelling"].AttributionState);
        Assert.Equal("None of your words", rows["grammar.spelling"].ReachSummaryText);
        Assert.Equal("Matched by spelling only; this does not confirm the phoneme was used",
            rows["grammar.spelling"].ReachStateText);
        Assert.Equal(WarningDisplayState.NoneInSelection, rows["grammar.none"].AttributionState);
        Assert.Equal("None of your words", rows["grammar.none"].ReachSummaryText);

        table.TouchYourWords = true;

        Assert.Equal(["grammar.exact"], table.Rows.Cast<GrammarWarningRowViewModel>().Select(row => row.GroupCode));
    }

    [Fact]
    public void RepeatedUnattributedDiagnosticsOfOneKindShareARowAndKeepTheirCount()
    {
        var warning = EntryWarning with
        {
            Code = "grammar.environment.invalid",
            Title = "Environment could not be read",
            Description = "environment representation failed validation",
            Subject = [],
            YourWords = new WarningWords(WarningWordsMatch.UnresolvedIdentity, [], [])
            {
                Reason = WarningAttributionReason.NoSubject,
            },
        };
        var table = new GrammarWarningsViewModel();

        table.Load(Enumerable.Repeat(warning, 7).ToArray());

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal(7, row.RepeatCount);
        Assert.Equal("Environment could not be read · 7 findings", row.RowTitleText);
        Assert.Equal("Can't tell: PanGloss names nothing", row.ReachSummaryText);
    }

    [Fact]
    public void OneKindKeepsNamedAndUnnamedFindingsInOneRowAndShowsBothWhenOpened()
    {
        var unnamed = EntryWarning with
        {
            Code = "grammar.environment.invalid",
            Title = "Environment could not be read",
            Subject = [],
            YourWords = null,
        };
        var named = EntryWarning with
        {
            Code = "grammar.environment.invalid",
            Title = "Environment could not be read",
            Subject = [new GrammarWarningPart("e2", GrammarWarningPartRole.Object,
                "55555555-5555-5555-5555-555555555555", "PhEnvironment")],
            YourWords = new WarningWords(WarningWordsMatch.Identity, [], []),
        };
        var table = new GrammarWarningsViewModel();

        table.Load([unnamed, named]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal("Environment could not be read · 2 findings", row.RowTitleText);
        Assert.Equal("None of your words", row.ReachSummaryText);
        Assert.Equal(2, row.Details.Count);
        Assert.Contains(row.Details, detail => detail.SubjectParts.Count == 0);
        Assert.Contains(row.Details, detail => detail.SubjectParts.Any(part => part.Text == "e2"));
    }

    [Fact]
    public void AKindCountsDistinctWordsAcrossEveryNamedFinding()
    {
        var first = WithWords(EntryWarning with { Code = "grammar.same-kind" },
            WarningWordsMatch.Identity, "one", "shared");
        var secondSubject = EntryWarning.Subject[1] with
        {
            ObjectId = "55555555-5555-4555-8555-555555555555",
            SubjectGuid = "55555555-5555-4555-8555-555555555555",
        };
        var second = WithWords(EntryWarning with
        {
            Code = "grammar.same-kind",
            Subject = [EntryWarning.Subject[0], secondSubject],
        }, WarningWordsMatch.Identity, "shared", "three");
        var table = new GrammarWarningsViewModel();

        table.Load([first, second]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal("3 of your words", row.ReachSummaryText);
        Assert.Equal(3, row.WordRows.Count);
    }

    [Fact]
    public void AKindWithNoExactUsesNamesTheItemAndSaysNoSelectionWordUsesIt()
    {
        var warning = EntryWarning with
        {
            Code = "conversion.unsegmentable-form",
            Subject = [new GrammarWarningPart("kat", GrammarWarningPartRole.Object, "form-kat", "MoForm")],
            YourWords = new WarningWords(WarningWordsMatch.Identity, [], []),
        };
        var table = new GrammarWarningsViewModel();
        table.Load([warning]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));

        Assert.Equal("None of your words", row.ReachSummaryText);
        Assert.Equal("None of your words use kat", row.NoExactUsesText);
        Assert.False(row.HasReachStateText);
        Assert.Empty(row.WordRows);
    }

    [Fact]
    public void TouchYourWordsChipShowsTheDistinctSelectionWordCount()
    {
        var first = WithWords(EntryWarning with { Code = "grammar.first" }, WarningWordsMatch.Identity,
            "walikata", "anakata");
        var second = WithWords(EntryWarning with { Code = "grammar.second" }, WarningWordsMatch.Identity,
            "anakata", "wamekata");
        var table = new GrammarWarningsViewModel();

        table.Load([first, second]);

        Assert.Equal(3, WarningWordsQuery.Touched(table.Findings)!.Words);
        Assert.Equal("Touch your words · 3 words", table.TouchYourWordsText);
    }

    [Fact]
    public void MembershipCandidatesStaySeparateFromExactUsesAndTheTouchYourWordsFilter()
    {
        var membership = WithWords(EntryWarning with { Code = "grammar.membership" },
            WarningWordsMatch.Membership, "member-only");
        var table = new GrammarWarningsViewModel();
        table.Load([membership]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal(WarningDisplayState.MembershipCandidates, row.AttributionState);
        Assert.Empty(row.WordRows);
        Assert.Equal("member-only", Assert.Single(row.MembershipCandidateRows).Row.Word);
        Assert.Null(row.YourWordsCount);

        table.TouchYourWords = true;

        Assert.Empty(table.Rows);
    }

    [Fact]
    public void MissingAndUnresolvedSubjectsUsePlainStateWords()
    {
        var missing = EntryWarning with
        {
            Code = "grammar.missing",
            YourWords = new WarningWords(WarningWordsMatch.MissingObject, [], [])
            {
                Reason = WarningAttributionReason.StaleGuid,
                Paths = [WarningWordsPath.MissingObject],
            },
        };
        var noSubject = EntryWarning with
        {
            Code = "grammar.no-subject",
            Subject = [],
        };
        var table = new GrammarWarningsViewModel();
        var unresolved = EntryWarning with
        {
            Code = "grammar.unresolved",
            Subject = [new GrammarWarningPart("unknown entry", GrammarWarningPartRole.Object, "not-a-guid", "LexEntry")
            {
                Reach = new WarningReach(WarningWordsPath.UnresolvedIdentity)
                { Reason = WarningAttributionReason.NamedWithoutProjectGuid },
            }],
        };
        table.Load([missing, noSubject, unresolved]);

        var rows = table.Rows.Cast<GrammarWarningRowViewModel>().ToDictionary(row => row.GroupCode);
        Assert.Equal(WarningDisplayState.MissingObject, rows["grammar.missing"].AttributionState);
        Assert.Equal("The item PanGloss named is missing from this project", rows["grammar.missing"].ReachStateText);
        Assert.Equal(string.Empty, rows["grammar.missing"].ReachSummaryText);
        Assert.Equal(WarningDisplayState.NoSubject, rows["grammar.no-subject"].AttributionState);
        Assert.Equal("PanGloss names nothing here", rows["grammar.no-subject"].ReachStateText);
        Assert.Equal(WarningDisplayState.UnresolvedIdentity, rows["grammar.unresolved"].AttributionState);
        Assert.Equal(string.Empty, rows["grammar.unresolved"].ReachSummaryText);
    }

    [Fact]
    public void AReportedObjectWithoutStoredReachIsUnresolvedRatherThanNoSubject()
    {
        var warning = EntryWarning with
        {
            Code = "grammar.unreached",
            Subject = [new GrammarWarningPart("named entry", GrammarWarningPartRole.Object,
                "44444444-4444-4444-4444-444444444444", "LexEntry")],
            YourWords = null,
        };
        var table = new GrammarWarningsViewModel();
        table.Load([warning]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));

        Assert.Equal(WarningDisplayState.UnresolvedIdentity, row.AttributionState);
        Assert.Equal(string.Empty, row.ReachSummaryText);
        Assert.Equal("Word counts are unavailable for this named item", row.ReachStateText);
        Assert.DoesNotContain("did not name a subject", row.ReachStateText, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWideResourcesSayTheyHaveNoWordAttribution()
    {
        var warning = EntryWarning with
        {
            Code = "grammar.project-wide",
            Subject = [new GrammarWarningPart("feature system", GrammarWarningPartRole.Object,
                "55555555-5555-5555-5555-555555555555", "FsFeatureSystem")
            {
                Reach = new WarningReach(WarningWordsPath.ProjectWide)
                { Reason = WarningAttributionReason.NoWordAttribution },
            }],
        };
        var table = new GrammarWarningsViewModel();
        table.Load([warning]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));

        Assert.Equal(WarningDisplayState.ProjectWide, row.AttributionState);
        Assert.Equal(string.Empty, row.ReachSummaryText);
        Assert.Equal("This item applies across the grammar", row.ReachStateText);
    }

    [Fact]
    public void AnIdentifiedUnsupportedSubjectIsNamedWithAndWithoutWordEvidence()
    {
        const string guid = "33333333-3333-3333-3333-333333333333";
        var named = EntryWarning with
        {
            Code = "grammar.unsupported",
            Subject = [new GrammarWarningPart("Verb template", GrammarWarningPartRole.Object, guid,
                "MoInflAffixTemplate", "silfw://localhost/link?tool=InflAffixTemplateEdit")
            {
                SubjectGuid = guid,
                FieldWorksGuid = guid,
                LinkStatus = FieldWorksLinkStatus.Available,
                Reach = new WarningReach(WarningWordsPath.UnresolvedIdentity)
                { Reason = WarningAttributionReason.UnsupportedKind },
            }],
        };
        var withEvidence = named with { YourWords = WarningWordsQuery.YourWordsOf(named, [], []) };
        var table = new GrammarWarningsViewModel();

        table.Load([withEvidence, named with { Code = "grammar.unsupported.no-assessment" }]);

        var rows = table.Rows.Cast<GrammarWarningRowViewModel>().ToDictionary(row => row.GroupCode);
        foreach (var row in rows.Values)
        {
            Assert.Equal(WarningDisplayState.NamedUnsupportedRoute, row.AttributionState);
            Assert.Equal(string.Empty, row.ReachSummaryText);
            Assert.Contains("without a word list", row.ReachStateText, StringComparison.Ordinal);
            Assert.Equal("Verb template", row.SubjectParts.Single().Text);
        }
        Assert.Equal(WarningWordsMatch.UnresolvedIdentity, rows["grammar.unsupported"].YourWords!.Match);
        Assert.Null(rows["grammar.unsupported.no-assessment"].YourWords);
    }

    [Fact]
    public void MissingWordEvidenceDoesNotLookLikeZeroWords()
    {
        var warning = EntryWarning with
        {
            Subject = [new GrammarWarningPart("entry", GrammarWarningPartRole.Object, "g", "LexEntry")
            {
                Reach = new WarningReach(WarningWordsPath.Uses),
            }],
            YourWords = null,
        };
        var table = new GrammarWarningsViewModel();
        table.Load([warning]);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));

        Assert.Equal(WarningDisplayState.EvidenceUnavailable, row.AttributionState);
        Assert.Equal(string.Empty, row.ReachSummaryText);
        Assert.Equal("Word counts are unavailable for this finding", row.ReachStateText);
    }

    [Fact]
    public void MostYourWordsFirstSortsByExactWordCountAndKeepsInputOrderForTies()
    {
        var one = WithWords(EntryWarning with { Code = "grammar.one" }, WarningWordsMatch.Identity, "one");
        var three = WithWords(EntryWarning with { Code = "grammar.three" }, WarningWordsMatch.Identity,
            "three-one", "three-two", "three-three");
        var spelling = WithWords(PhonemeWarning with { Code = "grammar.spelling" }, WarningWordsMatch.Spelling,
            "candidate-one", "candidate-two", "candidate-three", "candidate-four");
        var table = new GrammarWarningsViewModel();

        table.Load([one, three, spelling]);

        Assert.Equal(["grammar.three", "grammar.one", "grammar.spelling"],
            table.Rows.Cast<GrammarWarningRowViewModel>().Select(row => row.GroupCode));
        table.MostYourWordsFirst = false;
        Assert.Equal(["grammar.one", "grammar.three", "grammar.spelling"],
            table.Rows.Cast<GrammarWarningRowViewModel>().Select(row => row.GroupCode));
    }

    [Fact]
    public void AFindingRemovedByRefreshRemainsVisibleAsGoneForOneRefresh()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([WithWords(EntryWarning, WarningWordsMatch.Identity, "one")]);

        table.Load([], preserveResolved: true);

        var gone = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.True(gone.IsGone);
        Assert.Equal("Gone after Refresh", gone.StatusText);
        Assert.True(gone.CanParseAgain);

        table.Load([], preserveResolved: true);

        Assert.Empty(table.Rows);
    }

    [Fact]
    public void AChangedPanGlossMessageKeepsTheSameFindingWhenItsTypedSubjectIdentityIsStable()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning]);

        table.Load([EntryWarning with { Description = "The value changed; the same entry remains invalid." }],
            preserveResolved: true);

        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.False(row.IsGone);
        Assert.Equal("The value changed; the same entry remains invalid.", row.Description);
    }

    [Fact]
    public void AReplacementSubjectOfTheSameKindStaysInsideTheSameRow()
    {
        var original = EntryWarning;
        var replacementPart = original.Subject[1] with
        {
            ObjectId = "44444444-4444-4444-4444-444444444444",
            SubjectGuid = "44444444-4444-4444-4444-444444444444",
        };
        var replacement = original with { Subject = [original.Subject[0], replacementPart] };
        var table = new GrammarWarningsViewModel();
        table.Load([original]);

        table.Load([replacement], preserveResolved: true);

        var rows = table.Rows.Cast<GrammarWarningRowViewModel>().ToArray();
        var row = Assert.Single(rows);
        Assert.False(row.IsGone);
        Assert.Equal(replacementPart.ObjectId, row.Details.Single().SubjectParts[1].ObjectId);
    }

    [Fact]
    public void HomonymousSubjectsOfOneKindStayTogetherAndRefreshMarksTheKindGone()
    {
        var first = EntryWarning;
        var secondPart = first.Subject[1] with
        {
            ObjectId = "55555555-5555-5555-5555-555555555555",
            SubjectGuid = "55555555-5555-5555-5555-555555555555",
        };
        var table = new GrammarWarningsViewModel();

        table.Load([first, first with { Subject = [first.Subject[0], secondPart] }]);

        var grouped = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal(2, grouped.Details.Count);
        Assert.All(grouped.Details, detail => Assert.Equal("kuona", detail.SubjectParts[1].Text));

        var unidentified = first with
        {
            Subject = [new GrammarWarningPart("kuona", GrammarWarningPartRole.Object, "legacy-id", "Entry")],
        };
        table.Load([unidentified]);
        table.Load([], preserveResolved: true);

        var old = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.True(old.IsGone);
        Assert.Equal("Gone after Refresh", old.StatusText);
    }

    [Fact]
    public void NotRequestedReadingAvailabilitySurvivesTheJsonRoundTrip()
    {
        var word = new SIL.Motif.Contract.Responses.WordRow("motifa", WordRowOutcome.Same, "Fine", WordRowTone.Fine)
        {
            PanGlossReadingAvailability = WordRowReadingAvailability.NotRequested,
        };

        var json = ProjectionJson.Serialize(word);
        var restored = ProjectionJson.Deserialize<SIL.Motif.Contract.Responses.WordRow>(json);

        Assert.Contains("\"panGlossReadingAvailability\": \"not_requested\"", json, StringComparison.Ordinal);
        Assert.Equal(WordRowReadingAvailability.NotRequested, restored!.PanGlossReadingAvailability);
    }

    private static GrammarWarning WithWords(GrammarWarning warning, WarningWordsMatch match, params string[] forms) =>
        warning with
        {
            YourWords = new WarningWords(match,
                forms.Select(form => new ObjectUseWord(
                    new SIL.Motif.Contract.Responses.WordRow(form, WordRowOutcome.Same, "Fine", WordRowTone.Fine)
                    { Places = 2 })).ToArray(), []),
        };
}
