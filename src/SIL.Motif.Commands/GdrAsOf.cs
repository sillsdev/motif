using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;

namespace SIL.Motif.Commands;

/// <summary>Lays the Guide edits of a Draft's or Proposal's operations over a section as the Baseline shows it.</summary>
internal static class GdrAsOf
{
    private const string Discussion = "CmPossibility.Discussion", Name = "CmPossibility.Name", Description = "CmPossibility.Description";

    /// <summary>The section as it reads once <paramref name="operations"/> apply; operations on other objects are ignored.</summary>
    public static GdrShowResponse Apply(GdrShowResponse shown, IEnumerable<OperationEnvelope> operations)
    {
        var target = CanonicalId.Parse(shown.Record.Id).ToGuid();
        var result = shown;
        foreach (var operation in operations)
        {
            if (operation.Target is not { } operationTarget || operationTarget.ToGuid() != target || operation.After is not { } after)
                continue;
            switch (operation.Kind)
            {
                case GdrSectionOperationKinds.Move:
                    result = result with { Record = result.Record with { ParentId = after.TryGetProperty("parent", out var parent) ? parent.GetString() : null } };
                    break;
                case CmPossibilityNameOperationKinds.SetName:
                    result = result with { TitleAlternatives = Replace(result.TitleAlternatives,
                        after.GetProperty("ws").GetString()!, [Plain(after.GetProperty("text").GetString()!, after.GetProperty("ws").GetString()!)]) };
                    break;
                case CmPossibilityNameOperationKinds.ClearName:
                    result = result with { TitleAlternatives = Replace(result.TitleAlternatives, after.GetProperty("ws").GetString()!, []) };
                    break;
                case TextRichStringOperationKinds.Set:
                    var rich = TextRichStringPayload.Parse(after);
                    var runs = rich.Runs.Select(Run).ToArray();
                    if (rich.Field == Name && rich.Ws is { } nameWs)
                        result = result with { TitleAlternatives = Replace(result.TitleAlternatives, nameWs, runs) };
                    else if (rich.Field == Description && rich.Ws is { } descriptionWs)
                        result = result with { DescriptionAlternatives = Replace(result.DescriptionAlternatives, descriptionWs, runs) };
                    break;
                case TextParagraphsOperationKinds.Set:
                    var edit = TextParagraphsPayload.Parse(after);
                    if (edit.Field == Discussion) result = result with { Discussion = Paragraphs(result, edit) };
                    break;
            }
        }
        var summary = result.Record with
        {
            Title = Text(result.TitleAlternatives, result.DefaultAnalysis, result.Record.Title),
            Description = Text(result.DescriptionAlternatives, result.DefaultAnalysis, result.Record.Description),
        };
        return result with { Record = summary };
    }

    private static string Text(IReadOnlyList<RichStringAlternative> alternatives, string ws, string fallback) =>
        alternatives.FirstOrDefault(item => item.WritingSystem == ws)?.Text ?? fallback;

    private static IReadOnlyList<RichStringAlternative> Replace(IReadOnlyList<RichStringAlternative> alternatives, string ws, GdrRun[] runs) =>
        alternatives.Any(item => item.WritingSystem == ws)
            ? alternatives.Select(item => item.WritingSystem == ws ? item with { Runs = runs } : item).ToArray()
            : [.. alternatives, new RichStringAlternative(ws, runs)];

    private static GdrRun Plain(string text, string ws) =>
        new(text, ws, null, new GdrTextProperties(new Dictionary<int, GdrIntProperty>(), new Dictionary<int, string>()));

    private static IReadOnlyList<GdrParagraph> Paragraphs(GdrShowResponse shown, TextParagraphsPayload edit)
    {
        var stored = shown.Discussion.Where(paragraph => paragraph.Key is not null).ToDictionary(paragraph => paragraph.Key!, StringComparer.Ordinal);
        var changes = edit.Changed.ToDictionary(change => change.Index);
        var result = new List<GdrParagraph>();
        for (var index = 0; index < edit.Order.Count; index++)
        {
            var key = edit.Order[index];
            if (changes.TryGetValue(index, out var change))
            {
                var ints = (change.Props?.Ints ?? new Dictionary<int, (int, int)>())
                    .ToDictionary(pair => pair.Key, pair => new GdrIntProperty(pair.Value.Variation, pair.Value.Value));
                var strings = (change.Props?.Strings ?? new Dictionary<int, string>()).ToDictionary(pair => pair.Key, pair => pair.Value);
                if (change.Style is { } style) strings[TextPropertyNumbers.NamedStyle] = style;
                result.Add(new GdrParagraph(change.Style, ints.TryGetValue(128, out var direction) && direction.Value == 1,
                    change.Runs.FirstOrDefault()?.Ws ?? shown.DefaultAnalysis, new GdrTextProperties(ints, strings),
                    change.Runs.Select(Run).ToArray(), key));
            }
            else if (key is not null && stored.TryGetValue(key, out var kept)) result.Add(kept);
        }
        return result;
    }

    private static GdrRun Run(TextRun run)
    {
        var ints = (run.Props?.Ints ?? new Dictionary<int, (int, int)>())
            .ToDictionary(pair => pair.Key, pair => new GdrIntProperty(pair.Value.Variation, pair.Value.Value));
        var strings = (run.Props?.Strings ?? new Dictionary<int, string>()).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (run.CharacterStyle is { } style) strings[TextPropertyNumbers.NamedStyle] = style;
        return new GdrRun(run.Text, run.Ws, run.CharacterStyle, new GdrTextProperties(ints, strings), run.ObjData);
    }
}
