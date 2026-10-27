using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>
/// A tiny lexicon in one script, with the writing systems of its forms and glosses. Every test of the text view
/// runs over these, so a left-to-right and a right-to-left sample differ only in their data.
/// </summary>
internal sealed record TextViewSample(
    string Name,
    string FormWritingSystem,
    string GlossWritingSystem,
    bool RightToLeft,
    string[] Forms,
    string[] Glosses,
    string[] Categories)
{
    /// <summary>Latin script: forms and glosses both run left to right.</summary>
    public static TextViewSample Ltr { get; } = new("ltr", "sw", "en", false,
        ["wa-", "toto", "-ni", "ki-", "tabu", "-zi", "m-", "somaji", "-a", "ku-"],
        ["3PL", "child", "LOC", "7", "book", "PL", "1", "read", "FV", "INF"],
        ["pro", "n", "loc", "pre", "n", "pl", "pre", "v", "fv", "inf"]);

    /// <summary>
    /// Hebrew forms with vowel points, so most forms carry combining marks, glossed in English:
    /// right-to-left forms beside left-to-right glosses.
    /// </summary>
    public static TextViewSample Rtl { get; } = new("rtl", "he", "en", true,
        ["וְ", "הַ", "סֵפֶר", "ים", "בְּ", "בַּיִת",
            "שָׁלוֹם", "מֶלֶךְ", "אֲ", "עָל"],
        ["and", "the", "book", "PL", "in", "house", "peace", "king", "to", "on"],
        ["conj", "art", "n", "pl", "prep", "n", "n", "n", "prep", "prep"]);

    public static TextViewSample[] Both { get; } = [Ltr, Rtl];

    /// <summary>The sample called <paramref name="name"/>, as the theories spell it.</summary>
    public static TextViewSample Named(string name) => Both.Single(sample => sample.Name == name);

    /// <summary>One long form whose letters carry combining marks, for tests of wrapping inside a column.</summary>
    public string LongForm => RightToLeft
        ? "\u05D4\u05B7\u05DE\u05BC\u05B7\u05DE\u05B0\u05DC\u05B8\u05DB\u05D5\u05BC\u05EA\u05B5\u05D9\u05D4\u05B6\u05DD\u05D5\u05B0\u05D4\u05B7\u05DE\u05BC\u05B7\u05DE\u05B0\u05DC\u05B8\u05DB\u05D5\u05BC\u05EA"
        : "ku\u0301nu\u0300nya\u0303ka\u0302lu\u0308wa\u0301ku\u0301nu\u0300nya\u0303ka\u0302lu\u0308wa\u0301";

    /// <summary>The morpheme forms of word <paramref name="word"/>, three stored by FieldWorks.</summary>
    public string[] StoredForms(int word) => Enumerable.Range(0, 3).Select(i => Forms[(word * 3 + i) % Forms.Length]).ToArray();

    /// <summary>The surface form of word <paramref name="word"/>, with its hyphens removed.</summary>
    public string WordText(int word) => string.Concat(StoredForms(word).Select(form => form.Trim('-')));

    /// <summary>The morpheme forms PanGloss proposes for the word: the stored three, then one more.</summary>
    public string[] ParserForms(int word) =>
        [.. StoredForms(word).Take(2), Forms[(word * 3 + 5) % Forms.Length], Forms[(word * 3 + 7) % Forms.Length]];

    public ParserReadingMorph Morph(string form, bool parser) =>
        new(form, Glosses[Array.IndexOf(Forms, form)], Categories[Array.IndexOf(Forms, form)], null, false, null)
        {
            FormWritingSystem = FormWritingSystem,
            GlossWritingSystem = GlossWritingSystem,
            CategoryWritingSystem = GlossWritingSystem,
            AllomorphId = Identity(parser ? "parser-allomorph " : "allomorph ", form),
            GrammaticalInfoId = Identity("grammatical info ", form),
        };

    private static string Identity(string kind, string form) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(kind + form))).ToString("D");

    /// <summary>A word whose stored reading differs from PanGloss's, so the strip shows both morphology lines.</summary>
    public ResultsTokenViewModel Token(int word, string title = "Parity")
    {
        var text = WordText(word);
        var stored = new ParserReading([.. StoredForms(word).Select(form => Morph(form, parser: false))])
        {
            StoredAnalysisId = Guid.Parse($"00000000-0000-0000-0000-{word + 1:D12}").ToString("D"),
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };
        stored = stored with
        {
            Identity = new ApprovedMorphology([.. stored.Morphs.Select(morph =>
                new ApprovedMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, [morph.Form]))]),
        };
        var parser = new ParserReading([.. ParserForms(word).Select(form => Morph(form, parser: true))]);
        var analysis = new ProjectAnalysis(stored.StoredAnalysisId!, stored.Morphs)
        {
            StoredAnalysisId = stored.StoredAnalysisId,
            StoredAnalysisOpinion = stored.StoredAnalysisOpinion,
            Identity = stored.Identity,
        };
        ParseAnalysis Raw(ParserReading reading) => new([.. reading.Morphs.Select(morph =>
            new ParseMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, null))]);
        var result = new AssessmentWordResult(text, "analysed", false, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, text, 3, false, false, false, [Raw(parser), Raw(stored)], []),
            Readings = [parser, stored],
            ReadingGrades = ["no-opinion", "approved"],
            ProjectStanding = ProjectStanding.Approved,
            OccurrenceCount = 1,
            StoredAnalyses = [stored],
            ExpectedAnalysis = stored,
            WordWritingSystem = FormWritingSystem,
        };
        return new ResultsTokenViewModel(title, 1, new TextToken(text, text, null, "approved")
        {
            TextWritingSystem = FormWritingSystem,
            FormWritingSystem = FormWritingSystem,
            GlossWritingSystem = GlossWritingSystem,
            CategoryWritingSystem = GlossWritingSystem,
            WordformId = Guid.Parse($"00000000-0000-0000-0001-{word + 1:D12}"),
            Analysis = analysis,
            StoredAnalyses = [analysis],
            StoredAnalysisId = analysis.StoredAnalysisId,
        }, result);
    }
}
