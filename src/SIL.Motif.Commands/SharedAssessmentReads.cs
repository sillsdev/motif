using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Reads each Assessment at most once for one listing. Every Trial of a project usually names the same Baseline
/// Assessment, so reading it per Trial would load its words again for every Proposal.
/// </summary>
internal sealed class SharedAssessmentReads(MotifDatabase database)
{
    private readonly AssessmentRepository _repository = new(database);
    private readonly Dictionary<string, AssessmentRecord> _records = new(StringComparer.Ordinal);

    /// <exception cref="KeyNotFoundException">No Assessment is recorded under this id.</exception>
    public AssessmentRecord Get(string assessmentId)
    {
        if (!_records.TryGetValue(assessmentId, out var record))
            _records[assessmentId] = record = _repository.Get(assessmentId);
        return record;
    }
}
