using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;

namespace SIL.Motif.Tests.App;

/// <summary>Records and Discussions the Grammar Guide page tests read.</summary>
internal static class GrammarGuideTestSupport
{
    internal static readonly GdrSummary PartOne = new("gdr/part-1", "1", "Nouns", "");
    internal static readonly GdrSummary SectionA = new("gdr/sec-a", "1.1", "Noun classes", "", "gdr/part-1");
    internal static readonly GdrSummary SectionB = new("gdr/sec-b", "1.2", "Agreement", "", "gdr/part-1");
    internal static readonly GdrSummary PartTwo = new("gdr/part-2", "2", "Verbs", "");

    internal static GdrSummary[] Hierarchy => [PartOne, SectionA, SectionB, PartTwo];

    internal static GdrTextProperties Props(string? style = null) => new(new Dictionary<int, GdrIntProperty>(),
        style is null ? new Dictionary<int, string>() : new Dictionary<int, string> { [TextPropertyNumbers.NamedStyle] = style });

    internal static GdrParagraph Paragraph(string style, string key, params GdrRun[] runs) =>
        new(style, false, "en", Props(style), runs, key);

    internal static GdrRun Plain(string text) => new(text, "en", null, Props());

    internal static GdrRun Link(string text, string objData, GdrLinkTarget target) => new(text, "en", null, Props(), objData, target);

    internal static string ObjectLink(Guid guid) => $"\u0004silfw://localhost/link?database%3dthis%24%26guid%3d{guid:D}%26tag%3d";

    internal static GdrShowResponse Show(GdrSummary record, params GdrParagraph[] paragraphs) => new(record, paragraphs)
    {
        DefaultAnalysis = "en",
        DefaultVernacular = "en",
        Styles =
        [
            new GdrStyle("Normal", GdrStyleKind.Paragraph, new Dictionary<string, GdrTextProperties> { ["en"] = Props() }),
            new GdrStyle("Heading 1", GdrStyleKind.Paragraph, new Dictionary<string, GdrTextProperties> { ["en"] = Props() }),
            new GdrStyle("Heading 2", GdrStyleKind.Paragraph, new Dictionary<string, GdrTextProperties> { ["en"] = Props() }),
        ],
        WritingSystems = [new GdrWritingSystem("en", "ltr", "Segoe UI")],
    };

    internal static FakeCommandClient Fake(IReadOnlyList<GdrSummary> records, Func<GdrSummary, GdrShowResponse>? show = null)
    {
        var fake = new FakeCommandClient();
        fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Success(new(true, records)));
        fake.GrammarGuideShowHandler = (request, _) => Task.FromResult(CommandOutcome<GdrShowResponse>.Success(
            (show ?? (record => Show(record, Paragraph("Normal", "p-" + record.Id, Plain(record.Title)))))(records.Single(record => record.Id == request.Id))));
        return fake;
    }
}
