using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Tests.TestFixtures;

internal static class CorrectnessFixture
{
    internal static AssessedWord Word(string word, bool matched)
    {
        var morph = new ParseMorph("11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222", null, null);
        var expected = new ApprovedMorphology([new(morph.Form, morph.Msa, null, [])]);
        var evidence = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, word, 1, false, false, false,
            matched ? [new ParseAnalysis([morph])] : [], []);
        return new AssessedWord(word, matched ? "analysed" : "no-analysis", [])
        {
            Morphology = evidence,
            Correctness = new WordCorrectness(1, matched ? 1 : 0, matched ? "covered" : "unmatched",
                [expected], matched ? [] : [0]),
        };
    }
}
