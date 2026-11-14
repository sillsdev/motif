using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrSyncCheckerTests(PristineProjectFixture pristine)
{
    private sealed class Rows(params (string Word, GdrExampleReading Reading)[] rows) : IGdrExampleEvidence
    {
        public int Reads;
        public IReadOnlyCollection<string> Asked = [];

        public IReadOnlyDictionary<string, GdrExampleReading> Read(IReadOnlyCollection<string> words)
        {
            Reads++;
            Asked = words;
            return rows.Where(row => words.Contains(row.Word)).ToDictionary(row => row.Word, row => row.Reading);
        }
    }

    private static GdrExampleReading Parsed(params Guid[][] analyses) => new("A-1", true, true, analyses);

    private static readonly GdrExampleReading NoParse = new("A-1", false, true, []);

    private static readonly IReadOnlyList<GdrLedgerLink> NoLedger = [];

    private static (IPhRegularRule Rule, ILexEntry Entry, IMoAffixProcess Process) SeedGrammar(LcmCache cache)
    {
        IPhRegularRule rule = null!;
        ILexEntry entry = null!;
        IMoAffixProcess process = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, "Nasal assimilation");
            entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.AlternateFormsOS.Add(process);
            process.Form.set_String(cache.DefaultVernWs, "mang");
        });
        return (rule, entry, process);
    }

    private static string LinkData(Guid guid)
    {
        var link = new MarkdownObject
        {
            Kind = MarkdownObjectKind.InternalLink, Guid = guid,
            Url = FieldWorksLinks.ForTarget("proj", new FieldWorksLinkTarget("PhonologicalRuleEdit", guid)),
        };
        Assert.True(DefaultObjectMapper.Instance.TryCreateObjData(link, out var data, out _));
        return data;
    }

    // One piece of a paragraph: text in the analysis or vernacular writing system, or a link to an object.
    private sealed record Piece(string Text, bool Vernacular = false, Guid? Link = null);

    private static Piece Prose(string text) => new(text);

    private static Piece Word(string text) => new(text, true);

    private static Piece Cite(string text, Guid guid) => new(text, true, guid);

    private static ITsString Build(LcmCache cache, params Piece[] pieces)
    {
        var builder = TsStringUtils.MakeStrBldr();
        foreach (var piece in pieces)
        {
            var props = TsStringUtils.MakePropsBldr();
            props.SetIntPropValues((int)FwTextPropType.ktptWs, 0, piece.Vernacular ? cache.DefaultVernWs : cache.DefaultAnalWs);
            if (piece.Link is { } guid) props.SetStrPropValue((int)FwTextPropType.ktptObjData, LinkData(guid));
            builder.Replace(builder.Length, builder.Length, piece.Text, props.GetTextProps());
        }
        return builder.GetString();
    }

    private static IPartOfSpeech? NoCategory => null;

    /// <summary>Adds a record; a paragraph whose first piece starts with "# " is a Heading 2.</summary>
    private static void AddRecord(LcmCache cache, string number, string title, params Piece[][] paragraphs) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var list = services.GetInstance<ICmPossibilityListRepository>().AllInstances()
                .FirstOrDefault(candidate => candidate.Name.get_String(cache.DefaultAnalWs).Text == GdrReader.ListName)
                ?? services.GetInstance<ICmPossibilityListFactory>().CreateUnowned(GdrReader.ListName, cache.DefaultAnalWs);
            var record = services.GetInstance<ICmPossibilityFactory>().Create();
            list.PossibilitiesOS.Add(record);
            record.Name.set_String(cache.DefaultAnalWs, title);
            record.Abbreviation.set_String(cache.DefaultAnalWs, number);
            record.DiscussionOA = services.GetInstance<IStTextFactory>().Create();
            foreach (var pieces in paragraphs)
            {
                var heading = pieces[0].Text.StartsWith("# ", StringComparison.Ordinal);
                if (heading) pieces[0] = pieces[0] with { Text = pieces[0].Text[2..] };
                var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
                record.DiscussionOA.ParagraphsOS.Add(paragraph);
                paragraph.Contents = Build(cache, pieces);
                var rules = TsStringUtils.MakePropsBldr();
                rules.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, heading ? "Heading 2" : "Normal");
                paragraph.StyleRules = rules.GetTextProps();
            }
        });

    private static Piece[] Heading(string text) => [Prose("# " + text)];

    [Fact]
    public void AProjectWithRulesAndNoRecordsWarnsThatEveryKindHasNoSection()
    {
        using var cache = pristine.NewScratch();
        SeedGrammar(cache);

        var result = GdrSyncChecker.Check(cache, new Rows(), NoLedger);

        Assert.False(result.InSync);
        Assert.Equal(GdrSyncCodes.NoRecords, result.Findings[0].Code);
        var rules = Assert.Single(result.Findings, finding => finding.Kind == "Phonological rule");
        Assert.Equal(GdrSyncCodes.ObjectUncovered, rules.Code);
        Assert.Equal(["Nasal assimilation"], rules.Subjects);
        Assert.Contains(result.Coverage, kind => kind is { Kind: "Affix process", Objects: 1, Covered: 0 });
    }

    [Fact]
    public void OneSectionCoversManyObjectsAndALinkToAnEntryCoversItsAffixProcesses()
    {
        using var cache = pristine.NewScratch();
        var (rule, entry, _) = SeedGrammar(cache);
        AddRecord(cache, "GDR-18", "Allomorph selection", Heading("Analysis"),
            [Prose("One rule, "), Cite("Nasal assimilation", rule.Guid), Prose(" and the prefix "), Cite("mang-", entry.Guid)]);

        var result = GdrSyncChecker.Check(cache, new Rows(), NoLedger);

        Assert.True(result.InSync, string.Join("; ", result.Findings.Select(finding => finding.Message)));
        Assert.Contains(result.Coverage, kind => kind is { Kind: "Phonological rule", Objects: 1, Covered: 1 });
        Assert.Contains(result.Coverage, kind => kind is { Kind: "Affix process", Objects: 1, Covered: 1 });
        Assert.Equal((1, 1, 2, 0, 0), (result.Counts.Records, result.Counts.Sections, result.Counts.Links, result.Counts.Examples,
            result.Counts.ExamplesChecked));
    }

    [Fact]
    public void ARuleOutsideEverySectionIsNamedEvenWhenOthersAreCovered()
    {
        using var cache = pristine.NewScratch();
        var (rule, _, _) = SeedGrammar(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var second = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(second);
            second.Name.set_String(cache.DefaultAnalWs, "Final devoicing");
        });
        AddRecord(cache, "GDR-8", "Sound processes", Heading("Analysis"), [Cite("Nasal assimilation", rule.Guid)]);

        var result = GdrSyncChecker.Check(cache, new Rows(), NoLedger);

        var finding = Assert.Single(result.Findings, item => item.Kind == "Phonological rule");
        Assert.Equal(["Final devoicing"], finding.Subjects);
        Assert.Contains(result.Coverage, kind => kind is { Kind: "Phonological rule", Objects: 2, Covered: 1 });
    }

    [Fact]
    public void ASectionThatCitesADeletedObjectIsNamedWithItsSection()
    {
        using var cache = pristine.NewScratch();
        var (rule, _, _) = SeedGrammar(cache);
        var doomed = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var other = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(other);
            doomed = other.Guid;
        });
        AddRecord(cache, "GDR-8", "Sound processes", Heading("Analysis"), [Cite("Nasal assimilation", rule.Guid)],
            Heading("Exceptions"), [Cite("Gone", doomed)]);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => cache.DomainDataByFlid.DeleteObj(
            cache.ServiceLocator.ObjectRepository.GetObject(doomed).Hvo));

        var result = GdrSyncChecker.Check(cache, new Rows(), NoLedger);

        var finding = Assert.Single(result.Findings, item => item.Code == GdrSyncCodes.CitesMissingObject);
        Assert.Equal(("GDR-8", "Exceptions", 1), (finding.Gdr, finding.Section, finding.Count));
        Assert.Equal([doomed.ToString("D")], finding.Subjects);
    }

    [Fact]
    public void APassedHypothesisMustNameAGdrAndSectionTheProjectStillHolds()
    {
        using var cache = pristine.NewScratch();
        AddRecord(cache, "GDR-18", "Allomorph selection", Heading("Analysis"), [Prose("text")]);
        GdrLedgerLink[] ledger =
        [
            new("H-1", "GDR-18", "analysis"), new("H-2", "gdr-18", null), new("H-3", "GDR-18", "Examples"),
            new("H-4", "GDR-99", "Analysis"),
        ];

        var result = GdrSyncChecker.Check(cache, new Rows(), ledger);

        var missing = result.Findings.Where(finding => finding.Code == GdrSyncCodes.LedgerLinkMissing).ToArray();
        Assert.Equal(["H-3", "H-4"], missing.SelectMany(finding => finding.Subjects).Order());
    }

    [Fact]
    public void ExamplesAreReadFromStoredRowsOnceAndEachClaimIsChecked()
    {
        using var cache = pristine.NewScratch();
        var (_, entry, process) = SeedGrammar(cache);
        var other = Guid.NewGuid();
        AddRecord(cache, "GDR-18", "Allomorph selection",
            Heading("Examples"),
            [Cite("mang-", entry.Guid), Prose(" + "), Word("palit"), Prose("\t"), Word("mamalit"), Prose("\t'buy'")],
            [Word("tan-aw"), Prose("\t"), Word("manan-aw"), Prose("\t'look'")],
            [Word("isda"), Prose("\t"), Word("mangisda")],
            [Word("lupad")],
            Heading("False parses"),
            [Word("mamamalit")]);
        var rows = new Rows(
            ("palit", Parsed([entry.Guid])), ("mamalit", Parsed([entry.Guid, Guid.NewGuid()])),
            ("tan-aw", NoParse), ("manan-aw", NoParse),
            ("isda", Parsed([other])), ("mangisda", Parsed([other])),
            ("mamamalit", Parsed([other])));

        var result = GdrSyncChecker.Check(cache, rows, NoLedger);

        Assert.Equal(1, rows.Reads);
        Assert.DoesNotContain(rows.Asked, word => word.EndsWith('-') || word == "'buy'");
        var notParsing = Assert.Single(result.Findings, item => item.Code == GdrSyncCodes.ExampleNotParsing);
        Assert.Equal(["tan-aw", "manan-aw"], notParsing.Subjects);
        Assert.Equal("Examples", notParsing.Section);
        // Only the first row cites an entry, and both its words use it.
        Assert.DoesNotContain(result.Findings, item => item.Code == GdrSyncCodes.ExampleAnalysisChanged);
        var unverified = Assert.Single(result.Findings, item => item.Code == GdrSyncCodes.ExampleUnverified);
        Assert.Equal(["lupad"], unverified.Subjects);
        var nowParses = Assert.Single(result.Findings, item => item.Code == GdrSyncCodes.ExampleNowParses);
        Assert.Equal("False parses", nowParses.Section);
        Assert.Equal(["mamamalit"], nowParses.Subjects);
        Assert.Equal(8, result.Counts.Examples);
        Assert.Equal(7, result.Counts.ExamplesChecked);
        Assert.Equal(["A-1"], result.Counts.EvidenceAssessments);
        _ = process;
    }

    [Fact]
    public void AWordThatParsesWithoutTheMorphemesItsRowCitesIsAChangedClaim()
    {
        using var cache = pristine.NewScratch();
        var (_, entry, _) = SeedGrammar(cache);
        AddRecord(cache, "GDR-18", "Allomorph selection", Heading("Examples"),
            [Cite("mang-", entry.Guid), Prose(" + "), Word("palit"), Prose("\t"), Word("mamalit")]);
        var rows = new Rows(("palit", Parsed([Guid.NewGuid()])), ("mamalit", Parsed([entry.Guid])));

        var result = GdrSyncChecker.Check(cache, rows, NoLedger);

        var finding = Assert.Single(result.Findings, item => item.Code == GdrSyncCodes.ExampleAnalysisChanged);
        Assert.Equal(["palit"], finding.Subjects);
    }

    [Fact]
    public void AnInconclusiveRowSettlesNothing()
    {
        using var cache = pristine.NewScratch();
        AddRecord(cache, "GDR-1", "Scope", Heading("Examples"), [Word("palit")]);
        var rows = new Rows(("palit", new GdrExampleReading("A-1", false, false, [])));

        var result = GdrSyncChecker.Check(cache, rows, NoLedger);

        Assert.Equal(["palit"], Assert.Single(result.Findings, item => item.Code == GdrSyncCodes.ExampleUnverified).Subjects);
        Assert.Equal(0, result.Counts.ExamplesChecked);
    }

    [Fact]
    public void ProseOutsideAnExamplesSectionIsNotReadAsExampleWords()
    {
        using var cache = pristine.NewScratch();
        AddRecord(cache, "GDR-1", "Scope", Heading("Analysis"), [Word("palit"), Prose(" is a stem")]);
        var rows = new Rows();

        var result = GdrSyncChecker.Check(cache, rows, NoLedger);

        Assert.Equal(0, rows.Reads);
        Assert.Equal(0, result.Counts.Examples);
        Assert.True(result.InSync);
    }
}
