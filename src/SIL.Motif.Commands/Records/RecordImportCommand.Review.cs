using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Records;

public static partial class RecordImportCommand
{
    /// <summary>
    /// Watches a scratch copy while a folder's operations run on it: which existing entries change homograph number, and
    /// which gain a variant or complex-form back-reference from a new entry. liblcm renumbers homographs itself, so the
    /// numbers are read before and after rather than worked out.
    /// </summary>
    private sealed class ReviewProbe(IReadOnlyCollection<Guid> created)
    {
        private readonly Dictionary<Guid, int> _before = [];
        private IReadOnlyList<RecordImportHomographRow> _homographs = [];
        private IReadOnlyList<RecordImportBackReferenceRow> _backReferences = [];
        private bool _measured;

        /// <summary>Notes the homograph number of every entry the project has now.</summary>
        public void Before(LcmCache cache)
        {
            _before.Clear();
            if (created.Count == 0) return;
            foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
                _before[entry.Guid] = entry.HomographNumber;
        }

        /// <summary>Compares the scratch copy with what <see cref="Before"/> noted.</summary>
        public void After(LcmCache cache)
        {
            if (created.Count == 0) return;
            var objects = cache.ServiceLocator.ObjectRepository;
            var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
            var renumbered = new List<RecordImportHomographRow>();
            foreach (var (guid, before) in _before)
                if (entries.TryGetObject(guid, out var entry) && entry.IsValidObject && entry.HomographNumber != before)
                    renumbered.Add(new(CanonicalId.FromGuid(guid).Value, before, entry.HomographNumber));
            var gained = new Dictionary<Guid, (int Variants, int ComplexForms)>();
            foreach (var guid in created)
            {
                if (!objects.TryGetObject(guid, out var made) || made is not ILexEntry entry || !entry.IsValidObject) continue;
                foreach (var reference in entry.EntryRefsOS)
                    foreach (var component in reference.ComponentLexemesRS)
                    {
                        var target = (component as ILexEntry ?? (component as ILexSense)?.Entry)?.Guid;
                        if (target is not { } existing || !_before.ContainsKey(existing)) continue;
                        gained.TryGetValue(existing, out var counts);
                        gained[existing] = reference.RefType == LexEntryRefTags.krtVariant
                            ? (counts.Variants + 1, counts.ComplexForms)
                            : (counts.Variants, counts.ComplexForms + 1);
                    }
            }
            _homographs = renumbered.OrderBy(row => row.Entry, StringComparer.Ordinal).ToList();
            _backReferences = gained.Select(pair => new RecordImportBackReferenceRow(
                CanonicalId.FromGuid(pair.Key).Value, pair.Value.Variants, pair.Value.ComplexForms))
                .OrderBy(row => row.Entry, StringComparer.Ordinal).ToList();
            _measured = true;
        }

        /// <summary>The review for the summary, or null when the folder created no entry to measure.</summary>
        public RecordImportReview? Summary(int edges) => !_measured ? null : new(
            _homographs.Count, _backReferences.Count, edges,
            _homographs.Take(RecordImportPaging.SummaryReviewRows).ToList(),
            _backReferences.Take(RecordImportPaging.SummaryReviewRows).ToList());
    }
}
