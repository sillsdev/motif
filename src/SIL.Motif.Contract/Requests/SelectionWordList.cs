namespace SIL.Motif.Contract.Requests;

/// <summary>
/// The file a Selection's added words came from and the count each word carries in it. A word the file gave no
/// count for is absent from <see cref="Counts"/>: its count is not known, which is not the same as zero.
/// </summary>
/// <param name="Source">The file's name, without its folders.</param>
/// <param name="Sha256">The SHA-256 of the file's bytes, as <c>sha256:</c> and lowercase hexadecimal.</param>
/// <param name="Counts">Each counted word, in NFD, with the sum of the counts the file gave it.</param>
public sealed record SelectionWordList(string Source, string Sha256, IReadOnlyDictionary<string, int> Counts)
{
    /// <summary>The count of one word, or null when the file gave it none.</summary>
    public int? CountOf(string word) => Counts.TryGetValue(word, out var count) ? count : null;
}
