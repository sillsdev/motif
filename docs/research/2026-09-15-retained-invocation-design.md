# Retained invocation storage design

This slice gives a measured result a durable identity that can be selected after the command returns. A
later reader can therefore choose the exact Assessment invocation, its saved Baseline, and its original
Selection without guessing from the newest row of one Assessment kind.

## Aggregate and members

`RetainedInvocation` is the immutable aggregate keyed by `InvocationId`. It records the project workspace,
the exact serialized `BaselineToken`, the published Baseline directory and `.fwdata` path, the source file's
saved time, the aggregate saved time, assessor and scope provenance, and the retained PanGloss artifact
reference. Its `SelectionDescriptor` records the caller's Text IDs, pasted words, all-wordforms choice, retry
request, resolved retry source Assessment ID, threshold, final resolved words and hash, and per-source counts.

The aggregate does not copy parser artifacts or word measurements. The existing `AssessmentInvocations` row
owns the shared artifact evidence, and the member table maps each Assessment kind to exactly one Assessment
row. Members remain the source of full per-word evidence.

`RetainedInvocationMembers` uses `(InvocationId, Kind)` as its key and also makes an Assessment ID unique
within retained results. The aggregate stores the expected kind set alongside that mapping, so a missing
member remains detectable. The repository requires a non-empty, unique member set and verifies every
member's invocation, kind, Baseline token, Selection words/hash, scope, and Assessor before returning it.

## Atomic write and query rules

The retained repository writes the aggregate, its members, and all Assessment rows in one SQLite transaction.
It reuses the Assessment repository's insert helpers, so a duplicate or malformed member cannot leave an
aggregate, a header, or word rows behind. Existing databases are not migrated; the unreleased schema 16
shape is edited in place and older shapes are refused by the normal pre-1.0 gate.

`Get` and `List` validate the same invariants as the write path. A missing member, a member pointing at a
different invocation or kind, or disagreement about Baseline, Selection, scope, or Assessor is a store
inconsistency rather than a partial result. Listing is ordered by aggregate saved time and then invocation
identity.

## Command and retry wiring

`AssessCommand` creates the aggregate only when the Assessor supplies one shared invocation evidence record
with a non-null identity. It passes the already resolved Baseline token into `SelectionComposer`; retry
selection requires an explicit source Assessment ID. A source from another Baseline or project, a Proposal
source, or a source of the wrong kind is refused before parsing. The command response exposes the retained
invocation identity and the full selection descriptor, while Handoff and other consumers remain outside this
storage slice.

## Deliberate limits

This change does not define a Handoff manifest, export schema, UI history chooser, or retention cleanup policy.
Its CLI surface is limited to the `assess --retry-source-assessment <id>` option. It preserves the existing
project-owned Baseline refresh behavior: replacing the current Baseline row does not rewrite a retained
aggregate's paths or token.

## Review decisions and remaining follow-ups

Successful release Assessments now require one shared PanGloss invocation, bind every member's grammar-source
digest to that invocation's source bytes, and return validated child Assessment records from `Get`. Selection
descriptors use canonical text identities and pasted-word intent, with their RFC 8785 digest stored in the
current unreleased schema shape; there is no migration path.

Retry source identity is explicit: missing, extra, Proposal, wrong-kind, wrong-Baseline, and wrong-project
references are refused before parsing. The repository validates the same rules on read, and list reads use
Assessment headers instead of loading every member's words. A transaction test covers failure after member
Assessment insertion and verifies that the shared evidence, headers, words, aggregate, and members roll back.

Remaining follow-ups are intentionally outside R2: cleaner pin wiring and its layout remain an explicit R8
dependency; artifact-file containment and missing-file checks belong at the later R5 opening boundary; and the
public Handoff schema remains deferred.
