using SIL.Motif.Contract.Phonology;
using SIL.Motif.Host.Corpus;

namespace SIL.Motif.Host.Assess;

/// <summary>The closed identity of the material measured, independent of its result availability.</summary>
public abstract record AssessmentSubject
{
    private AssessmentSubject() { }

    public sealed record Words(Selection Selection, string TokeniserName, string TokeniserVersion) : AssessmentSubject;
    public sealed record PhonologyExamples(PhonologyRevisionReference Revision) : AssessmentSubject;
}
