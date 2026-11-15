namespace SIL.Motif.App.Compare;

/// <summary>Finds keyed paragraphs whose relative order changed, excluding additions and removals.</summary>
internal static class ParagraphMoves
{
    public static IReadOnlyDictionary<string, ChangeAuthor> Find(TextVersions versions)
    {
        var shared = versions.Starting.Select(paragraph => paragraph.Key).ToHashSet(StringComparer.Ordinal);
        shared.IntersectWith(versions.Mine.Select(paragraph => paragraph.Key).OfType<string>());
        shared.IntersectWith(versions.FieldWorks.Select(paragraph => paragraph.Key).OfType<string>());
        var starting = versions.Starting.Select(paragraph => paragraph.Key).Where(shared.Contains).Distinct().ToArray();
        var mine = versions.Mine.Select(paragraph => paragraph.Key).OfType<string>().Where(shared.Contains).ToArray();
        var other = versions.FieldWorks.Select(paragraph => paragraph.Key).OfType<string>().Where(shared.Contains).ToArray();
        if (mine.SequenceEqual(other)) return new Dictionary<string, ChangeAuthor>();
        var mineMoves = Moved(starting, mine);
        var otherMoves = Moved(starting, other);
        var result = new Dictionary<string, ChangeAuthor>(StringComparer.Ordinal);
        foreach (var key in mineMoves.Union(otherMoves))
        {
            var beforeMine = mine.TakeWhile(candidate => candidate != key).ToHashSet(StringComparer.Ordinal);
            var beforeOther = other.TakeWhile(candidate => candidate != key);
            if (beforeMine.SetEquals(beforeOther)) continue;
            result[key] = !versions.StartingOrderKnown ? ChangeAuthor.Both : mineMoves.Contains(key)
                ? otherMoves.Contains(key) ? ChangeAuthor.Both : ChangeAuthor.Mine
                : ChangeAuthor.FieldWorks;
        }
        return result;
    }

    private static HashSet<string> Moved(string[] starting, string[] side)
    {
        var map = Alignment.Match(starting.Select(Key).ToArray(), side.Select(Key).ToArray());
        return starting.Where((_, index) => map[index] < 0).ToHashSet(StringComparer.Ordinal);
    }

    private static Token Key(string key) => new(key, TokenKind.Word, []);
}
