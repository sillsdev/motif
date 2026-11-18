using System;
using System.Threading;
using System.Threading.Tasks;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Contract.HandAnalysis;

/// <summary>A source of immutable hand-analysis snapshots for one merged Home and local evidence basis.</summary>
public interface IHandAnalysisIndex
{
    /// <summary>Pin the latest complete publication for a null ID, or the exact retained publication for an ID.</summary>
    /// <remarks>
    /// For <c>--at &lt;revision|baseline&gt;</c>, resolve the revision through project history or the Baseline through
    /// its saved semantic identity in the host.
    /// Match the resolved Home point and any explicitly selected evidence to the project's published bases.
    /// A single matching composite snapshot ID is passed here. An ambiguous match refuses with
    /// <see cref="RefusalCodes.HandAnalysisIndexUnavailable"/> and the ambiguous-basis cause. An absent match or
    /// an exact ID absent from the host's retained and live points refuses with
    /// <see cref="RefusalCodes.HandAnalysisSnapshotExpired"/>. Give the resolved ID in the refusal when known.
    /// A fresh process may open the identical basis when all native and external layers can still be acquired and
    /// verified. The exact ID always binds the Home, Source, parser, settings, comparer and schema basis.
    /// Bind immutable external evidence alongside its revision IDs. A full verification request is host policy
    /// and must bypass the certificate and verified-point cache for the same basis. Existing pins retain their Stamp.
    /// Corrupt, incomplete-native and unavailable acquisitions return IndexUnavailable with a stable cause.
    /// Keep the four latest composite points per project, including different external bases at one Home revision.
    /// A live pin survives eviction without holding a database transaction, write lease or project lock.
    /// Historical resolution is pinned by `HistoricalSelectorsUseTheExactBasisAndRefuseIfItIsGoneOrAmbiguous`.
    /// </remarks>
    ValueTask<CommandOutcome<IHandAnalysisSnapshot>> PinAsync(Guid projectId, string? exactSnapshotId, CancellationToken ct);
}

/// <summary>Queries read the same Home, Source, parser and settings revisions throughout the lifetime of the pin.</summary>
/// <remarks>
/// Open existing Home data read-only, release any acquisition transaction before returning the pin and keep detached facts.
/// External Source and parser facts remain local. Encounter keys preserve native traversal, including sense-less variants
/// and Assessment-scoped parser readings. Evaluations, use counts and rank facts are bound to the pin.
/// A cursor binds the snapshot, full query and total-order key including a unique row discriminator. Page size can vary.
/// Ordering is pinned by `RankedCursorTraversesTiedRowsUsingTheirFullOrder`. Pin isolation is pinned by
/// `CursorBindingRefusesChangedQueriesAndOtherSnapshots`. All pages and detached occurrence and reference rows
/// carry the issuing Stamp. A successful outcome always has a value. Expected acquisition and read failures are refusals.
/// Acquisition and reads observe cancellation throughout verification and materialization, before retaining a new pin or
/// returning a result. Cancellation releases acquisition resources and propagates without a partial result.
/// Disposal is idempotent. Subsequent access to Project or query methods throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public interface IHandAnalysisSnapshot : IAsyncDisposable
{
    SnapshotStamp Stamp { get; }
    ProjectRead Project { get; }
    ValueTask<CommandOutcome<OccurrencePage>> OccurrencesAsync(OccurrenceQuery query, PageRequest page, CancellationToken ct);
    ValueTask<CommandOutcome<Page<WordFact>>> WordsAsync(WordQuery query, PageRequest page, CancellationToken ct);
    ValueTask<CommandOutcome<Page<FormHit>>> FormsAsync(FormQuery query, PageRequest page, CancellationToken ct);
    ValueTask<CommandOutcome<Page<LexicalFact>>> LexiconAsync(LexiconQuery query, PageRequest page, CancellationToken ct);
    ValueTask<CommandOutcome<ReferencePage>> ReferencesAsync(ReferenceQuery query, PageRequest page, CancellationToken ct);
}
