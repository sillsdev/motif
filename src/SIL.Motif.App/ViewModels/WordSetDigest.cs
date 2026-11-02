namespace SIL.Motif.App.ViewModels;

/// <summary>
/// How many words a read covered and an order-free sum of one hash per word, so two sides can be checked to cover
/// the same words without keeping either side's rows.
/// </summary>
internal readonly record struct WordSetDigest(int Count, ulong Sum)
{
    /// <summary>The digest of these spellings; a 64-bit sum is strong evidence of the same words, not proof.</summary>
    public static WordSetDigest Of(IEnumerable<string> words)
    {
        var count = 0;
        var sum = 0UL;
        foreach (var word in words)
        {
            count++;
            var hash = 14695981039346656037UL;
            foreach (var character in word) hash = (hash ^ character) * 1099511628211UL;
            sum += hash;
        }
        return new WordSetDigest(count, sum);
    }
}
