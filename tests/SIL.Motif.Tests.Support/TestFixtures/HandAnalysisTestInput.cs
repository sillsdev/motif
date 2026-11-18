using SIL.Motif.Contract.HandAnalyses;

namespace SIL.Motif.Tests.TestFixtures;

public static class HandAnalysisTestInput
{
    public static HandAnalysisInput Create(Guid project, string word = SeededProject.AnalysedWordForm) =>
        new("motif.hand-analysis/1", Guid.NewGuid(), new(project, "saved", [], "context", "segmentation"),
            new("agent"), new("analyst"), new HandAnalysisScriptureAnchor(Guid.NewGuid(), "English", "MAT 5:3#2", "verse"),
            new(NewLangProjFixture.VernacularTag, word, word), null, [], [], new HandAnalysisNoGloss([]), true);

    public static HandAnalysisPiece Linked(SeededProject seed, Guid msa) => new("piece",
        new(NewLangProjFixture.VernacularTag, SeededProject.FirstForm, new("", "")),
        new(seed.FirstEntryId.ToString(), seed.FirstLexemeFormId.ToString(), seed.FirstSenseId.ToString(), msa.ToString()));
}
