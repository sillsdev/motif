using SIL.Motif.Host.Texts;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Stored Text words for tests that record a Baseline by hand and never read its Texts. Product code has no such
/// default: a real Baseline is always recorded with the words built from the same saved project.
/// </summary>
public static class TestTextWords
{
    /// <summary>Stored Text words for a Baseline with no Texts.</summary>
    public static TextWordsProjection Empty { get; } = new([], []);
}
