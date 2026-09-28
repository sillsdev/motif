# The Motif API

*2026-08-26. The contract every consumer uses, established by
[ADR 0040](adr/0040-one-api-the-cli.md).*

Motif has one API and it is the `motif` executable. An AI agent, a shell script, a test, and a
FieldWorks-side view model are the same kind of consumer: they run a verb and read its output. There is
no second vocabulary, no library a host embeds, and no wire protocol beside this one.

This document states what the contract is, what part of it is built, and what part is specified and not
yet built. It does not schedule the work.

## The shape of a call

One process is one call. There is no session, no connection, and no handshake; state that outlives a call
lives in `Project.motif.db` beside the project, and a resident job runner picks up work from there.

```
motif <verb> [--project <path.fwdata>] [--store <dir>] [flags] [--json]
```

The table below is regenerated from
`CommandCatalog.All` and `CliVerbCatalog.All`; the usage cells preserve the catalogued strings used by
`UsageLineFor(...)` and the CLI dispatch. `dry-run --wait`, `trial --wait`, and `report --list-kinds`
are selector descriptors with no independent usage line, so they repeat the parent invocation line
that dispatches them.

| Catalog descriptor | Surface | Usage line |
| --- | --- | --- |
| `open` | Released | `open <fwdata> [--json]` |
| `analyses` | Released | `analyses --project <fwdata> [--json]`<br>`analyses --project <fwdata> --assessment <assessmentId> --current-selection-sha256 <sha256> --current-grammar-sha256 <sha256> [--json]` |
| `new` | Developer | `new --project <fwdata> --draft <name> [--label <text>]` |
| `pending-changes` | Developer | `pending-changes --project <fwdata> [--json]` |
| `put-pending-change` | Developer | `put-pending-change --project <fwdata> --expected-revision <revision> --change-id <id> --kind <kind> --word <word> [--wordform-id <id>] [--assessment <id> --reading-index <zero-based> --reading-json <json>] [--stored-analysis-id <id>] [--occurrence-text-id <guid> --occurrence-paragraph-id <guid> --occurrence-segment-id <guid> --occurrence-index <zero-based>] [--json]` |
| `remove-pending-change` | Developer | `remove-pending-change --project <fwdata> --expected-revision <revision> --change-id <id> [--json]` |
| `recheck-pending-changes` | Developer | `recheck-pending-changes --project <fwdata> --expected-revision <revision> [--json]` |
| `reconfirm-pending-change` | Developer | `reconfirm-pending-change --project <fwdata> --expected-revision <revision> --change-id <id> [--json]` |
| `add-set-gloss` | Developer | `add-set-gloss --project <fwdata> --draft <name> --target <canonicalId> --ws <wsTag> --text <text> [--depends-on <opId>[,<opId>...]]` |
| `add-delete-lexeme-form` | Developer | `add-delete-lexeme-form --project <fwdata> --draft <name> --target <canonicalId>` |
| `compose-author-lexeme-form` | Developer | `compose-author-lexeme-form --draft <name> --project <fwdata> --intent '{"entry":...,"morphType":...,"ws":...,"text":...}'` |
| `compose-author-feature-structure` | Developer | `compose-author-feature-structure --draft <name> --project <fwdata> --intent '{"msa":...}'` |
| `promote-gloss` | Developer | `promote-gloss --project <fwdata> --draft <name> --target <canonicalId> --ws <wsTag> --text <text> --corpus <corpusId> [--document <docId>]` |
| `label` | Developer | `label --project <fwdata> --draft <name> <text>` |
| `comment` | Developer | `comment --project <fwdata> --draft <name> <text>` |
| `finalize` | Developer | `finalize --project <fwdata> --draft <name>` |
| `discard-draft` | Developer | `discard-draft --project <fwdata> --draft <name>` |
| `reopen` | Developer | `reopen --project <fwdata> --draft <name> <proposalId>` |
| `duplicate` | Developer | `duplicate --project <fwdata> --draft <newName> <proposalId>` |
| `remove-operations` | Developer | `remove-operations --project <fwdata> --draft <name> <operationId> [<operationId>...] [--force]` |
| `split` | Developer | `split --project <fwdata> <proposalId> <draftName>=<opId>[,<opId>...] [<draftName>=<opId>[,<opId>...] ...] [--force]` |
| `defer` | Developer | `defer --project <fwdata> <proposalId>` |
| `reject` | Developer | `reject --project <fwdata> <proposalId>` |
| `supersede` | Developer | `supersede --project <fwdata> <proposalId> <supersededByProposalId>` |
| `list` | Developer | `list --project <fwdata> [--json]` |
| `show` | Developer | `show --project <fwdata> <proposalId> [--json]` |
| `preflight` | Developer | `preflight --project <fwdata> <proposalId> [--json]` |
| `apply` | Developer | `apply <proposalId> --project <fwdata> --user <name> [--force] [--json]` |
| `apply --all-pending` | Released | `apply --all-pending --project <fwdata> [--revision <r>] [--user <name>] [--json]` |
| `log` | Developer | `log --project <fwdata> [--json]` |
| `config show` | Released | `Usage: motif config show --project <fwdata> [--json]` |
| `report` | Released | `Usage: motif report --project <fwdata> --assessment <assessmentId> --kind <kind> [--word <w>] [--text <t>] [--json] OR motif report --list-kinds [--json]` |
| `report --list-kinds` | Released | `Usage: motif report --project <fwdata> --assessment <assessmentId> --kind <kind> [--word <w>] [--text <t>] [--json] OR motif report --list-kinds [--json]` |
| `compare` | Released | `Usage: motif compare --project <fwdata> --from <assessmentId> --to <assessmentId> [--json]` |
| `baseline capture` | Released | `baseline capture <project> [--json]` |
| `assess` | Released | `assess <project> [--texts <guid,guid>] [--all-wordforms] [--words <file>] [--retry-failed] [--retry-slower-than <ms>] [--retry-source-assessment <id>] [--time-limit-ms <ms>] [--step-cap <steps\|unbounded>] [--json]` |
| `stats` | Released | `stats <project> [--assessment <id>] [--json] [-- <pangloss stats options>]` |
| `selection show` | Released | `selection show --project <fwdata> [--json]` |
| `selection set-default` | Released | `selection set-default --project <fwdata> --name <name> [--texts <guid,guid>] [--add-words <word,word>] [--json]` |
| `store delete-refused` | Developer | `store delete-refused --project <fwdata> [--json]` |
| `overview` | Released | `overview --project <fwdata> [--json]` |
| `warnings` | Released | `warnings --project <fwdata> [--kind <code>] [--left-out] [--json]` |
| `grammar check` | Released | `grammar check --project <fwdata> [--json]` |
| `timing` | Released | `timing --project <fwdata> [--assessment <id>] [--words <set>] [--word <word,word>] [--by kind\|rule] [--rule <name>] [--top N] [--json]` |
| `handoff` | Released | `handoff <project> --out <folder> [--texts <guid,guid>] [--flextext] [--no-assess] [--json]` |
| `add-corpus` | Released | `add-corpus --project <fwdata> --id <id> --description <text> --tokeniser <name> --tokeniser-version <v> [--uri <url>] [--licence <text>] [--tokeniser-notes <text>] [--may-derive true\|false] [--may-redistribute true\|false] [--may-use-commercially true\|false] [--requires-attribution true\|false] [--licence-basis <text>]` |
| `add-document` | Released | `add-document --project <fwdata> --corpus <id> --doc <id> --source <file-or-url> [--title <text>] [--licence <text>] [--may-derive true\|false] [--licence-basis <text>]` |
| `add-corpus-bundle` | Released | `add-corpus-bundle --project <fwdata> --bundle <path>   (the handoff a fetching tool writes)` |
| `corpora` | Released | `corpora --project <fwdata> [--json]` |
| `show-corpus` | Released | `show-corpus --project <fwdata> <corpusId> [--json]` |
| `baseline-refresh` | Released | `baseline-refresh --project <fwdata>` |
| `dry-run` | Developer | `dry-run --project <fwdata> <proposalId> [--wait] [--json]` |
| `dry-run --wait` | Developer | `dry-run --project <fwdata> <proposalId> [--wait] [--json]` |
| `trial` | Developer | `trial --project <fwdata> <proposalId> [--scope <name>] [--all-words] [--wait] [--json]` |
| `trial --wait` | Developer | `trial --project <fwdata> <proposalId> [--scope <name>] [--all-words] [--wait] [--json]` |
| `trial --pending` | Developer | `trial --pending --project <fwdata> [--draft <id>] [--revision <r>] --words <w,…> --wait [--wait-timeout-ms <ms>] [--before-correctness <assessmentId>] [--json]` |
| `jobs show` | Released | `jobs show <jobId> --project <fwdata> [--json]` |
| `jobs assessments` | Released | `jobs assessments <jobId> --project <fwdata> [--json]` |
| `jobs list` | Released | `jobs list --all [--json]` |
| `jobs cancel` | Released | `jobs cancel <jobId> --project <fwdata> [--json]` |
| `jobs requeue` | Released | `jobs requeue <jobId> --project <fwdata> [--json]` |
| `jobs move` | Released | `motif jobs move <jobId> --project <fwdata> (--before <jobId> \| --to-top \| --to-bottom) [--json]` |

`jobs list --all` is the one verb that does not take `--project`: it spans every project this
installation has been pointed at, resolved through the machine store's `KnownProjects` rather than
through the working directory.

By default, `trial` measures only the wordforms touched by the Proposal. This includes touched words
without a manual analysis and gives `apply` evidence for each changed word. A Proposal with no
wordform changes has an empty word Selection by default. `--all-words` measures every wordform in the
project instead, regardless of the scope's word query; `--scope` still chooses the Assessor, kinds,
and limits.

## Released and developer surfaces

The Released surface contains `open`, `analyses`, `config show`, `report`, `report --list-kinds`,
`compare`, `baseline capture`, `assess`, `stats`, `selection show`, `selection set-default`,
`overview`, `warnings`, `grammar check`, `timing`, `handoff`, `add-corpus`, `add-document`,
`add-corpus-bundle`, `corpora`, `show-corpus`, `baseline-refresh`, `jobs show`, `jobs assessments`,
`jobs list`, `jobs cancel`, `jobs requeue`, `jobs move`, and `apply --all-pending`.

The Developer surface contains `new`, `pending-changes`, `put-pending-change`,
`remove-pending-change`, `recheck-pending-changes`, `reconfirm-pending-change`, `add-set-gloss`, `add-delete-lexeme-form`,
`compose-author-lexeme-form`, `compose-author-feature-structure`, `promote-gloss`, `label`, `comment`,
`finalize`, `discard-draft`, `reopen`, `duplicate`, `remove-operations`, `split`, `defer`, `reject`,
`supersede`, `list`, `show`, `preflight`, `apply`, `log`, `dry-run`,
`dry-run --wait`, `trial`, `trial --wait`, and `trial --pending`.

`apply --all-pending` is the one Released pending-change verb and the save-boundary entry point for
FieldWorks. FieldWorks releases the project before calling it. On exit `0`, FieldWorks reloads only when
`applied` is true; on exit `4` with `code: "apply.reconciliation-needed"`, it reloads and checks the project
because a change may have happened. A no-op does not require a reload. The command accepts an optional
`--revision` to require the exact revision FieldWorks previously checked and an optional `--user` for the
Receipt; without `--user`, Motif uses the current account. Nothing pending is a success, not a refusal: the
verb exits `0`, writes nothing to stderr and records no Receipt. With `--json`, its response has
`ok: true`, `applied: false`, and `summary: "Nothing to apply.". An Apply that ran returns the top-level
`receipt`, `applied: true`, and a short `summary` describing the authored pending changes rather than low-level
effects. The summary text is English. These shapes are Contract's `ApplyPendingResult`; FieldWorks reads
`ok`, `applied`, `summary`, and failure `code`, and ignores unknown fields. A per-Proposal list can be added
if the command later applies multiple Proposals. "Nothing pending" means the pending Draft holds no operations,
the same test `trial --pending` uses. Every nonzero exit code is a refusal or an error, with its failure on
stderr (a failure envelope under `--json`), so FieldWorks can treat exit `0` alone as "continue". A project
another program still holds is `Busy`, exit `3`: release it and retry.
An uncertain pending change exits `2` with `code: "apply.change-uncertain"`; FieldWorks keeps it pending
for review and does not reload the project.

#### FieldWorks executable lookup

FieldWorks first checks the `MOTIF_DIR` environment variable. When set, it names the directory containing
`motif.exe`. If it is unset, FieldWorks reads the `InstallationDir` value under
`HKLM\SOFTWARE\SIL\Motif`; that value names the same directory. FieldWorks does not search `PATH` for Motif.
The release installer will write the registry value and verify it in a clean-machine install.

`preflight` reads the live project and reports each collected operation's `status` (`fits`,
`uncertain`, or `no-longer-fits`), its existing `stillFits` boolean, operation id, and reason.
`--json` returns the same entries as structured `changes`. A deleted wordform, changed wordform form,
missing or moved analysis, changed analysis reading, a missing morph reference, an older Baseline, or a
parser reading that has since been stored is `no-longer-fits`. For a decision with an occurrence anchor,
a changed sentence, stale parse, missing Segment, or occurrence that no longer resolves uniquely is
`uncertain` while the analysis decision itself still fits. The result includes `uncertainty.reason`,
`beforeTokens`, and `afterTokens`; each token has its Segment index, canonical wordform id, and form.
`pending-changes` checks the persistent Draft before finalization, and
`remove-pending-change` removes a change by id and expected revision. Apply checks fit again
and refuses an uncertain change as well as one that no longer fits, including under `--force`. An
uncertain refusal names the change and says to check again; `--force` only bypasses Readiness reasons.
An Apply that succeeds records a durable Receipt in the paired project database.

The App and the pending-change CLI verbs use `PendingChanges` in Commands to load, put, remove, check,
and reconfirm changes in one persistent Draft. Each change has its own id, so distinct slots can address
one word. These verbs require the revision returned by the last load; a stale revision is refused. A
parser change carries the exact reading chosen from an Assessment, or identifies a stored analysis.
Display text is never an identity. A current Baseline is required, and the snapshot reports fit for every
change. `put-pending-change` may carry all four occurrence options together: Text GUID, paragraph GUID,
Segment GUID, and a zero-based index in that Segment's analysis sequence. Motif accepts these options
for analysis opinion changes. `recheck-pending-changes` renews Baseline tokens for changes that still
fit, but leaves uncertain occurrence evidence untouched. `reconfirm-pending-change` takes one change id
and replaces its occurrence evidence and BaselineToken after the analysis decision still fits; it refuses changes
that no longer fit. This evidence refresh does not alter Proposal intent. The Draft stays in
`Project.motif.db` when the App closes.

`trial --pending` resolves the current pending Draft when `--draft` and `--revision` are omitted, reading only
the paired project database, so it never opens the FieldWorks project to check them. A `--draft` or `--revision`
that no longer matches is refused as `trial.changes-changed`, and an empty pending Draft as `trial.nothing-pending`,
before any Trial is queued. The `--wait` flag is required and the verb always waits for its Trial. The default
wait timeout is two minutes; `--wait-timeout-ms` changes it, and a value that is not a whole number is refused
with the usage line. A timed-out Trial is cancelled; if cancellation is refused, the job keeps running and
`jobs show` reports it.

`CommandSurfacePolicy.IsAvailable` exposes a command when its surface is Released or when developer
commands are enabled. Set `MOTIF_DEVELOPER_COMMANDS=1` exactly to re-enable the Developer commands for
both help and dispatch; other values leave them unavailable.

`FailureEnvelope.ExitCodeFor` maps `InvalidArgument` to `1`, `NotFound`, `Refused`, and `Cancelled` to `2`,
`Busy` to `3`, and `StoreInconsistent` to `4`; its fallback also returns `4`. No failure reason maps to `0`.

The verb set is expected to churn. [ADR 0021](adr/0021-cli-is-the-full-surface-layer-1-churns.md) settles
that churn is welcome in this surface and forbidden in the hashed operation vocabulary and canonical JSON
form beneath it — those are what digests are computed over, and changing them changes the meaning of
stored evidence.

## What a consumer may rely on

**Text is for humans; `--json` is for everything else.** A consumer that parses the text rendering is
using an unsupported interface. Every projection is reachable as structured data and the text form is a
formatting pass over it, which is the rule ADR 0021 decision 2 sets so that a FieldWorks skin and the CLI
render the same material.

**Success goes to stdout, failure goes to stderr, and the exit code is authoritative.** A consumer decides
success from the exit code, never by inspecting output.

**No verb requires a prior verb in the same process.** Anything a call needs is either passed as a flag or
already durable in the store.

## The failure contract

Every refusal in `Commands.cs`, plus unknown verbs and usage errors, used to render the same way — plain
`error: <message>` on stderr with exit code `1`, **regardless of `--json`**. A machine consumer that asked
for JSON got prose when something went wrong, and could not tell a malformed invocation from a locked
project from a refused Apply.

That was the whole of the work ADR 0040 left behind, and it was small for a reason worth naming: under
the superseded worker protocol each refusal had to be re-expressed as a typed payload a remote client
could reconstitute a message from, which the [API surface note](cli-worker-api-surface.md) called the
largest step in that migration. Here the decision to refuse and its wording stay in one process, so only
the envelope was missing.

### The failure envelope

Under `--json`, a failure emits a single JSON object on stderr and nothing on stdout:

```json
{
  "ok": false,
  "code": "project.busy",
  "reason": "Busy",
  "message": "FieldWorks cannot open the project because another program is using it.",
  "detail": { "project": "…\\Sena 3.fwdata" }
}
```

`code` is the stable machine key. `reason` is a closed set of broad failure categories; `detail` is optional
and reason-specific. `message` is the existing human wording. Without `--json` the current
`error: <message>` rendering stays exactly as it is, because that is the human interface and it works.

Successful `--json` output stays as it is today: the projection itself, unwrapped. A consumer distinguishes
the two by exit code and stream, not by probing for an envelope.

### Exit codes

One code for every failure makes the caller guess. The set is deliberately small, because a code is a
promise and a large set is a large promise:

| Code | Meaning |
| --- | --- |
| `0` | The verb did what it was asked. |
| `1` | The invocation was wrong — unknown verb, missing or malformed flag. Retrying unchanged cannot help. |
| `2` | The request was well-formed and Motif refused it. A Drift refusal, a failed precondition, a policy denial. The state is unchanged and the caller may act on `code`. |
| `3` | The request was well-formed and could not be attempted now — the project is locked, the store is busy, a lease is held. Retrying later may succeed. |
| `4` | The store may be inconsistent, or Motif failed unexpectedly; inspect `code` before continuing. |

`apply.reconciliation-needed` uses exit `4`: a change may have happened; do not retry; the project must be
checked. Other unexpected failures also use the fallback exit `4`. The `2` and `3` split lets a caller tell
a refusal from a temporary lock.

### Versioning

The JSON surface is now a compatibility surface with real consumers, which is what the capability
negotiation in the deleted protocol existed to manage. It is managed here instead by additive discipline:

- New fields may be added to any projection at any time. Consumers ignore unknown fields.
- A field is never removed or retyped in place; a replacement is added beside it and the old one kept
  until every shipped consumer has moved.
- `reason` codes may be added. A consumer treats an unrecognised `reason` as a generic failure of the
  class its exit code names, which is why the exit code carries the retry decision and not the reason.

## What is deliberately not in this API

- **No in-process entry point for a host.** ADR 0040 decision 1: a FieldWorks-side surface runs the
  executable and reads JSON. It does not reference `SIL.Motif.Host`, `SIL.Motif.Worker`,
  `SIL.Motif.Projection`, `Microsoft.Data.Sqlite`, or the schema, and does not open `Project.motif.db` —
  not even to read.

  **One exception, and it is shapes only.** A consumer may reference `SIL.Motif.Contract` to deserialise
  this output into typed values — that is how FieldWorks will know what the fields are and render a diff
  rather than re-deriving the vocabulary. Contract keeps `netstandard2.0` for exactly this reason
  (ADR 0040 decision 3) and holds no behaviour that reaches storage.

  The response records now live in `SIL.Motif.Contract.Responses`, with the `LcmCache`-dependent builders
  left behind in `SIL.Motif.Projection`. `ResponseBindingTests` stands in for the consumer: it names no
  Motif namespace but Contract's, so a shape that ever needs a type from another assembly stops it
  compiling rather than failing quietly in a consumer nobody has built yet.
- **No access to the database as an interface.** `Project.motif.db` is an implementation detail shared by
  the CLI and the job runner, which ship together at one version. It is not a contract for anyone else.
- **No long-lived connection, no server, no port.** The job runner exists so that queued work outlives a
  CLI invocation. Nothing asks it anything; it claims rows.
- **No verb that mutates a project another process currently holds open.** ADR 0040 decisions 6 and 7:
  FieldWorks releases the project, the verb runs, FieldWorks reloads.

## Amendments

### 2026-08-28 — Jobs can be seen and reordered across every project, not just queried one at a time

[ADR 0041](adr/0041-the-database-is-the-only-store.md) decision 6 gives `Jobs` a stored `QueueOrder`
rather than deriving position from `UpdatedUtc`, and this is where the verbs that read and move it land:
`jobs list --all` (the one exception to decision 2's required `--project`, since it spans every project
by definition), `jobs cancel`, `jobs requeue`, and `jobs move <id> --before <id> | --to-top | --to-bottom`.

`jobs list --all` and the runner's own claim must agree on one thing or `move` would be dishonest: both
order strictly by `QueueOrder, JobId`, because `QueueOrder`'s default (`julianday('now')` in epoch
milliseconds) ties under real load — 200 inserts spanning 1,389 ms produced 199 distinct values. `move`
writes the moved job's own `QueueOrder` to a value between its new neighbours rather than swapping with
one, since a neighbour usually lives in a different project's database and no transaction spans two
SQLite files. `--before <id>` ordinarily has a midpoint; when the named job is tied with its predecessor
there is none, so the predecessor is renumbered down first to open one — in its own project's database,
which may differ from the moved job's, as a second single-row write that is not atomic with the move.

`cancel` sets `CancellationRequested` on a running job, which its runner's heartbeat reads and uses to
cancel the handler's own token, landing in the same terminal path a stopped runner already used. A job
that is only queued (or parked waiting on a Baseline or the project host) has no live handler to signal,
so it moves straight to `cancelled`. `requeue` calls the existing retry path to start a fresh attempt of
a terminal job's lineage.

### 2026-08-28 — `discard-draft` closes the abandon-a-draft gap

[ADR 0041](adr/0041-the-database-is-the-only-store.md) decision 3 made a Draft a `Proposals` row rather
than a file a caller could delete by hand, and no verb replaced that escape hatch: a caller who wanted to
abandon a half-written Draft had no way to. `discard-draft --project <fwdata> --draft <name>` is that verb.

What it does is decided by whether the Draft carries a committed revision. A never-finalized Draft
(`CurrentIntentDigest` still null, no `ProposalRevisions` row yet) has its whole row deleted. A Draft
`reopen` produced keeps its source Proposal's `CurrentIntentDigest`, so `discard-draft` only clears
`DraftName`/`DraftJson` instead — the exact inverse of what `reopen` set — leaving that Proposal exactly at
its prior committed revision, `ProposalRevisions` and `Decisions` untouched. Either way the operation is
one transaction, and the draft name is free for reuse immediately afterward.

### 2026-08-28 — `store-cutover` is gone

[ADR 0041](adr/0041-the-database-is-the-only-store.md) decision 1 deletes the file store rather than
migrating it, so the verb that migrated it is deleted with it. It had never worked: it refused every
Proposal the CLI can author, because the operation kinds it validates against are registered by
`SIL.Motif.Runner`'s module initializers and only the `Commands` static constructor forces them to load.

The rest of ADR 0041 changes this contract further — every remaining verb gains a required `--project`,
`dry-run` becomes a job, and a set of cross-project job verbs joins the set. Those land with the tasks
that implement them; this note records only what has already been removed.

### 2026-09-05 — `baseline capture`, `assess`, `stats`, and `handoff` measure a project synchronously, no queue

Four verbs let a caller save a project's current state, see how PanGloss parses it, and package the result
for a person or an AI agent to read — in one call each, with nothing queued and nothing to poll. All four
take the project path as a plain positional argument, not `--project`, and all four route failures through
the same project/store refusals every verb behind `ProjectStoreCommand` shares: `project.not-found`,
`project.invalid`, `project.busy`, `store.unsupported`, `store.inconsistent`. Exit codes for every refusal
code named below follow the table above via `FailureEnvelope.ExitCodeFor`; it is not restated here.

**`baseline capture <project> [--json]`** reads a Baseline from the project's saved `.fwdata` file on
disk — never FieldWorks' in-memory state — copying with delete-sharing so FieldWorks may keep the project
open the whole time and never touching its `.fwdata.lock` or `.bak`. One call copies the saved files,
loads the copy as a scratch cache to compute its semantic digest, zips it into a transport bundle, and
publishes it before returning; nothing here wakes the durable job runner. Every rendering carries the
words **"as of FieldWorks' last save"** beside the captured timestamp — pinned by
`CapturingARealProjectSucceedsAndPrintsHumanTextNamingTheFreshness`.

Human text prints the `.fwdata` path, the project identity, the bundle digest, the captured timestamp, the
last-save timestamp with that same wording, whether FieldWorks currently holds the project open, and
whether the published bytes matched an already-stored Baseline (nothing new written) or a new Baseline was
captured and published. `--json` binds to `BaselineCaptureResponse`: `token` (a `BaselineToken` —
`projectIdentity`, `semanticSnapshotDigest`, `projectionVersion`, `capturedUtc`, `bundleDigest`, and an
optional `capturedHostSessionId`/`capturedEditGeneration` pair), `fwDataPath`, `sourceLastWriteUtc`,
`fieldWorksHeldProject`, `reusedExistingBytes`.

Every `baseline capture` call re-reads the project's current saved file and republishes, even when nothing
changed; `reusedExistingBytes` reports only whether the publisher recognized the resulting bundle's bytes
as already stored under this Baseline's identity, not whether the read-and-digest work was skipped. That is
a different reuse from `assess`'s own, described below.

Refusals of its own: `baseline.source-incomplete` (the copied `.fwdata` failed to validate as a complete
`languageproject` document — e.g. FieldWorks was still writing it), `baseline.copy-unloadable` (the copy
could not be loaded as a scratch cache), `baseline.owned-root-violation` (the managed Baseline root failed
its own invariants), `baseline.busy` (another capture or publish holds the same files).

**`assess <project> [--texts <guid,guid>] [--all-wordforms] [--words <file>] [--retry-failed]
[--retry-slower-than <ms>] [--time-limit-ms <ms>] [--step-cap <steps|unbounded>] [--json]`** ensures a current Baseline exists for the project, composes a
Selection from the named sources or the saved default, sends that Selection through PanGloss under the machine-wide
admission queue, and records the outcome as Assessments — one per collected kind (parse time and
per-object timing), so a single run yields two Assessment ids.

Ensuring a Baseline reuses one already recorded for this project's workspace rather than recapturing, and
only captures a fresh one when no Baseline row exists yet at all — pinned by
`SecondRunReusesTheExistingBaselineRatherThanRecapturing`. That check is purely "does a row already exist"
and never re-reads the file to see whether it changed, unlike `baseline capture` itself, which always
re-reads and republishes.

The explicit sources combine as a union — naming more than one adds their words together:

- `--all-wordforms` — every wordform currently in the project;
- `--texts <guid,guid>` — the wordforms of the named Texts, matched only by their own GUID, never by name
  or title;
- `--words <file>` — one word per line, pasted or typed; blank lines are dropped;
- `--retry-failed` — the words the previous Baseline run recorded no analysis for;
- `--retry-slower-than <ms>` — also the words whose previous Baseline run recorded an elapsed time
  strictly greater than this many milliseconds.

Every source's words pass through the same trim-and-NFD-normalize pipeline before being combined, so a
pasted or typed word can never fail to match a project wordform over a normalization difference alone.
When no explicit source is named, `assess` resolves the saved default Selection. No saved default is refused as
`selection.default-missing`. Naming explicit sources that together contribute no words is refused as
`selection.empty`; naming a `--texts` GUID absent from the project is refused as `selection.text-not-found`.
`--time-limit-ms` overrides the configured per-word wall-clock limit. `--step-cap` overrides the configured
per-word search cap, whose default is 1,000,000 steps; `unbounded` asks PanGloss not to apply a step cap.
The setup wizard estimates a word's duration from the newest stored parser statistics. Before those exist, it
uses a documented typical-machine rate of 250,000 steps per second. It saves a per-word time cap at ten times
that estimate, rounded up to whole seconds; an unbounded step cap has no estimated or saved time cap.
A search that hits either limit is stored as incomplete and appears as Unknown in Compare and Overview.

**Cancellation records nothing.** A cancelled run refuses as `assessment.cancelled`; no partial Assessment
is ever left behind: recording waits for Assessment production and validates every selected word in the
batch statistics cache. Pinned by `CancellationWhileTheAssessorIsRunningRecordsNoAssessments` and
`MissingBatchStatisticsForASelectedWordRefusesTheAssessment`.

In human mode, progress lines ("Ensuring a current Baseline exists...", "Composing the Selection...",
"Parsing the Selection...", "Reading PanGloss's statistics...", "Assessment complete.") print to stderr as
the run proceeds; `--json` suppresses them. The final human rendering names the `.fwdata` path, the
Baseline's last-save timestamp with the same "(as of FieldWorks' last save)" wording, the Selection's word
count and its per-source provenance counts, the recorded Assessment ids, and a summary of statistics collected
during the batch as a fenced code block. The human Assessment text calls retained parser standard-error lines
**Parser load messages**. `--json` binds to `AssessCommandResponse`: `baseline` (a full
`BaselineCaptureResponse`, as above), `selection` (a `SelectionProjection` — `words` and a `provenance`
array of `{source, count}`), `assessmentIds`, `summaryMarkdown`. It does not include the removed
`grammarWarningDetails` field; parser standard-error lines remain in retained invocation evidence.

Refusals of its own: `assess.parser-unavailable` (parser discovery, Assessment production, or batch statistics
validation failed), `assessment.cancelled`, `selection.empty`, `selection.text-not-found`
— plus every
`baseline capture` refusal above, since `assess` captures a Baseline the same way when it needs to. A
mistyped project path is refused as `project.not-found` even when the parser is entirely unavailable,
because the project is resolved before the Assessor is ever built — pinned by
`AMissingProjectIsRefusedBeforeTheParserIsEvenBuilt`.


### 2026-09-26 — `apply --all-pending` is Developer-only

An ordinary `motif` installation no longer offers the command that writes every pending change into the
FieldWorks project. Motif's own window still applies them, because it calls the same workflow directly rather
than through this verb.

The owner ruled that the verbs an agent uses to change a project — the pending-change verbs, `preflight`,
`trial --pending` and `apply --all-pending` — stay developer-only until the full set of agent commands has
its own design and test coverage. `apply --all-pending` had been catalogued Released, so a Released
invocation exposed a project-mutating command that ruling defers. It now answers `command.not-in-release`
without `MOTIF_DEVELOPER_COMMANDS=1`, pinned by `ApplyingAllPendingChangesIsAbsentFromTheReleasedSurface`.
Its behaviour with the opt-in set is unchanged, and so is the FieldWorks save-boundary contract described
above; that surface is not yet built, and making the verb Released again is part of building it.

### 2026-09-26 — Supersede the Developer-only Apply ruling

This amendment supersedes the preceding Developer-only ruling. FieldWorks may call `apply --all-pending`
without `MOTIF_DEVELOPER_COMMANDS` at its save boundary after releasing the project; every other pending-change
and Proposal lifecycle verb remains Developer-only. Exit `0` reports whether a change was applied, and exit `4`
with code `apply.reconciliation-needed` means FieldWorks must check the project before continuing.

## Selection, Overview, and Timing

`config show --json` returns `ProjectConfigurationProjection`. Each scope's `perWordStepLimit` is a structured
`StepCap` object with `steps` and `isUnbounded`; an unbounded cap has `steps: null` and `isUnbounded: true`.
Assessment `ScopeJson` uses the same representation.

**`selection show --project <fwdata> [--json]`** reads the saved default Selection's name, Text GUIDs,
added words, and saved timestamps. **`selection set-default --project <fwdata> --name <name>
[--texts <guid,guid>] [--add-words <word,word>] [--json]`** saves those inputs and makes that named
Selection the default. At least one Text or one added word is required. Text GUIDs are checked against the
current Baseline when one exists; before the first Baseline they are checked when `assess` resolves the
Selection. A later `assess` records the exact resolved word list and digest on its Assessment. Its selection
is the union of the chosen Texts' wordforms and the added words.

`selection show` binds to `DefaultSelectionResponse` (`selection`, or `null` when none is saved).
`selection set-default` binds to the same shape. Refusals include `selection.empty`, `selection.invalid`,
and `selection.text-not-found`.

**`overview --project <fwdata> [--json]`** reads the current Baseline, its saved default Selection, and the
latest ParseTime Assessment with the same Baseline token and exact resolved Selection. It reports project
timestamps and counts, Selection word and Text-occurrence counts, wordforms, rules, lexemes, grammar and
Selection fingerprints, Text Coverage, Accuracy, and parse-time median, p95, slowest words, and step-limited
count. A rule is each distinct affix allomorph on a lexeme or alternate form, plus each compound and
phonological rule; morphemes are not counted. Text Coverage and Accuracy use the same Compare placement rules as the App. A time- or step-limited
word is Unknown, not a verdict. `Warnings` is a typed field in `OverviewResponse`; it is `null` until the
current Baseline has a stored grammar check. It counts warning and information findings and groups them by
diagnostic code using the same data as `warnings`. With no matching Assessment the response still has the project and Selection facts, while
Assessment metrics are empty. `--json` emits `OverviewResponse`.

**`timing --project <fwdata> [--assessment <id>] [--words <set>] [--word <word,word>] [--by kind|rule]
[--rule <name>] [--top N] [--json]`** reads only timings stored with an Assessment. Without `--assessment`,
it resolves the current Baseline's default Selection to its matching Assessment. `<set>` can be `all`,
`step-limit`, `slowest`, `cell:<standing>:<column>`, or a saved Selection name; `--word` supplies an explicit
word list. `--by kind` groups time by rule kind, and `--by rule` groups by rule name. `--rule` also returns
the most costly words for that rule. The response includes the word-time percentiles, slowest words, totals,
time share, attempts, and number of words touched. `--top` defaults to 10. `--json` emits `TimingResponse`.

The App's `TimingRequest.OverrideAssessmentIds` applies stored word re-runs in order over the named
ParseTime Assessment; the CLI has no flag for this request field. `timing.override-not-found` refuses a
missing re-run Assessment, and `timing.invalid-override` refuses one that is not ParseTime or belongs to
a different Baseline.

The Assessment's one `pangloss batch --stats` invocation writes per-word counters and per-object fact rows to
its statistics cache. Motif reads that cache directly, without starting more PanGloss processes, and stores
the normalized rows with the ParseTime Assessment. A missing selected-word row or unreadable cache refuses
the Assessment, so partial statistics cannot look complete. Opening Overview or Timing makes no parser call.

### Grammar findings

The Warnings page reads PanGloss's grammar-health report last stored for the current Baseline. Opening the page
or running `warnings` never invokes PanGloss. The App's **Reload grammar** action and the CLI check run PanGloss
`grammar-health` on the current Baseline. A successful run replaces that Baseline's one stored result. The store
also records the resolved default Selection digest at check time when one exists, but grammar findings remain
valid after the Selection changes.

**`grammar check --project <fwdata> [--json]`** performs a new check. With no Baseline it succeeds with
`hasBaseline: false` and no findings. `--json` emits `GrammarCheckResponse`. Its refusal codes are
`grammarcheck.cancelled`, `grammarcheck.timed-out`, `grammarcheck.parser-unavailable`,
`grammarcheck.parser-refused`, `grammarcheck.malformed-findings`, and `grammarcheck.unsupported-schema`.

**`warnings --project <fwdata> [--kind <code>] [--left-out] [--json]`** reads those stored findings.
`--kind` matches one stable diagnostic code exactly, ignoring case. `--left-out` keeps only findings whose
level is `warning`; the two filters can be combined. Counts by level and kind describe the filtered result.
`warnings` reads the stored result. Without a check, text says the grammar has not been checked and JSON has
`hasCheck: false` with empty findings.
`--json` emits `WarningsResponse`, including each finding's description, origin, guidance, subjects, and links.

PanGloss v2 writes an object with `schema_version: 2`, `fieldworks_project`, `summary`, and `diagnostics`.
The summary groups by `code` and includes `group_name`, `level` (`warning` or `info`), and `count`. Each
diagnostic includes `level`, `code`, `group_name`, `origin` (`check` or `import`), `description`, nullable
`guidance`, and `subjects`.

Subjects retain their FieldWorks class `kind`, `title`, nullable `subtitle`, `guid`, nullable `internal_id`,
and optional `opens_in` target. Their `fieldworks` link is either available with `guid`, `tool`, and `url`, or
unavailable with a `reason`. Available links open FieldWorks; unavailable reasons appear beside the subject.
Import diagnostics appear as **From import**, while grammar-check diagnostics appear as **From grammar check**.
Motif does not publish an audience property; support can be added when PanGloss includes audience in the report.
Standard-error warning lines are not read as grammar findings.

Motif reads schema version 2 only. A bare array or an earlier findings envelope is refused as
`grammarcheck.malformed-findings`. A different integer version is refused as
`grammarcheck.unsupported-schema`, with the received and expected versions; update PanGloss and Motif together.

**`stats <project> [--proposal <id>] [--json] [-- <forwarded to pangloss>...]`** passes a statistics query
straight through to PanGloss's own `stats` command. Motif contributes exactly two arguments of its own —
the grammar path and the cache path — and forwards everything written after a standalone `--` to PanGloss
unchanged: boundaries, order, duplicates, casing, and values that themselves begin with a dash are all
preserved, pinned by `ForwardingAfterTheDelimiterPreservesBoundariesOrderDuplicatesCasingAndLeadingDashes`.
Motif never tokenizes, reorders, deduplicates, or otherwise interprets those arguments. Because `--` also
ends Motif's *own* flag parsing (`ParseArgs`), `--json` only takes effect as Motif's flag when written
before it; after `--` it is just one more argument forwarded to PanGloss.

The grammar path is always the project's current Baseline's `.fwdata` — never a Trial's own candidate
copy, even when `--proposal <id>` selects that Proposal's Trial Assessment instead of the Baseline
Assessment. A Trial's candidate directory is a throwaway scratch copy deleted once its job finishes; only
the per-object statistics cache it produced is kept, keyed by the grammar digest it measured, and that
cache path is Motif's other contributed argument.

`--json` asks PanGloss for its own JSONL rows by appending `--format jsonl` to the forwarded arguments,
then parses each line into a row. **A forwarded `--format`, spelled either `--format jsonl` or
`--format=jsonl`, is refused rather than silently overridden** — `stats.format-conflict` — because Motif
checks only for the flag's presence and never second-guesses the caller's own choice.

Human text (no `--json`) is PanGloss's own stdout, unrendered. `--json` binds to `StatsCommandResponse`:
`assessmentId`, `grammarPath`, `cachePath`, and exactly one of `text` or `rows` populated depending on
which was requested — the other is always `null`.

Refusals of its own: `stats.format-conflict`, `stats.invalid-proposal-id` (a malformed `--proposal` value),
`stats.no-baseline` (no Baseline has been captured for this project yet), `stats.no-assessment` (no
Baseline or Trial Assessment carrying per-object statistics has been recorded yet — the Baseline-case
message directs the caller to run `motif assess` first), `stats.no-cache` (the resolved Assessment was
recorded without a statistics cache), `stats.parser-unavailable` (the executable is absent or cannot start),
`stats.parser-refused` (a nonzero parser exit, with `exitCode` in the refusal facts),
`stats.timed-out` (the invocation exceeded its wall-clock cap, with `capMinutes` in the facts), and
`stats.cancelled`. Every statistics invocation takes machine-queue admission and runs inside the
Windows job object, with a default ten-minute wall-clock cap.

**`handoff <project> --out <folder> --invocation <id> [--flextext] [--no-assess] [--json]`** writes a
self-explaining folder that an AI agent with no network and no package installer can read on its own: the
grammar, the chosen Texts, the exact selection that was parsed, and — unless `--no-assess` — PanGloss's own
statistics, alongside a reader script and reference documents the repository maintains and copies in
unchanged. It composes `baseline capture`, `assess`, and the six `stats` groups rather than reimplementing
any of them, and shares the same project/store refusals every verb behind `ProjectStoreCommand` does.
Both `<project>` and `--out <folder>` are required positional/flag values; omitting either is a usage
failure. `--invocation <id>` selects one completed retained Assessment. It is required unless `--no-assess`
is given, and `--texts` cannot be combined with it: the retained Selection decides which Texts and words
belong in the Handoff. A missing or cross-project invocation is refused before the destination is touched.
The selected invocation's Baseline, source evidence, Selection descriptor, statistics Assessment, and
Assessment ids are exported together, so changing the live project or Selection editor cannot substitute
different evidence.

With `--no-assess`, the command keeps the Baseline-only path: no Assessment is selected, every Text is
exported when `--texts` is absent, and a chosen GUID list restricts the Text files. A named id that does not
resolve to a Text is skipped on this legacy Baseline-only path; retained-result exports refuse a missing
Text id instead. A duplicate Text title is disambiguated by its own GUID in the file name, so two Texts
sharing a title still produce two distinct files — pinned by `DuplicateTextTitlesProduceTwoDistinctFiles`.

**Destination atomicity.** The folder is built in a sibling `.incoming-<guid>` directory next to the
requested `--out` path, its exact listing is validated as complete, and only then is it moved into place
with one `Directory.Move` — pinned by `AnEndToEndHandoffWritesTheExactListingAndEveryFileValidates`. A
refusal returned during populate, or an exception thrown out of it — a cancellation, a PanGloss grammar-import
failure — deletes the incoming directory and leaves no destination directory at all, pinned by
`CancellationDuringGrammarImportLeavesNoDestinationDirectory` and
`APanGlossGrammarImportFailureLeavesNoDestinationDirectory`. An existing destination that is a file, or a
directory that is not empty, is refused as `handoff.destination-exists` without writing into it or clearing
it — pinned by `AnExistingNonEmptyDestinationRefusesWithoutTouchingIt` — so a mistyped `--out` that happens
to name a real folder can never erase it.

**`--no-assess`** omits `statistics.md` and the whole `statistics/` directory, but still writes the grammar,
the Texts, and `selection.txt` — pinned by `NoAssessOmitsStatisticsButStillWritesGrammarTextsAndSelection`.
The response's `assessmentIds` is empty in this case, and human text prints `(none; --no-assess)` where the
Assessment ids would otherwise go.

**`--flextext`** additionally writes a `.flextext.xml` beside each Text's `.flextext.json` mirror, pinned by
`FlexTextAddsMatchingXmlBesideJson`; without it only the JSON form is written.

The complete listing, with `--no-assess` not given: `instructions.md`, `grammar.json`, `selection.txt`,
`statistics.md`, `recipes.md`, `read_handoff.py`, `reference/grammar-format.md`,
`reference/flextext-json-format.md`, `reference/hc-mechanics.md`, one
`texts/<title>-<guid>.flextext.json` per exported Text (plus a matching `.flextext.xml` under
`--flextext`), and **six** `statistics/<group>.jsonl` files — one per group `pangloss stats --group`
accepts: `word`, `object`, `allomorph`, `morpheme`, `group`, and `never-fires`, the only hyphenated one.
`--no-assess` removes `statistics.md` and the entire `statistics/` directory from that listing and changes
nothing else.

`instructions.md`, `recipes.md`, `read_handoff.py`, and the three `reference/` documents are static assets
this repository maintains and embeds in the `motif` binary; every Handoff carries its own unchanged copy,
which is why the folder needs neither network access nor a package installer to be read. `instructions.md`
explains what the folder is for, states plainly that it carries real linguistic data (uploading it to a
chat model sends that data to whoever runs the model), and names the "as of FieldWorks' last save" wording
that governs everything inside. The three `reference/` documents are maintained in the repository at
`docs/handoff/grammar-format.md`, `docs/handoff/flextext-json-format.md`, and `docs/handoff/hc-mechanics.md`
— `instructions.md` points a reader at their raw GitHub URLs for a newer copy, in case a question turns on
a detail fixed after this particular Handoff was written.

Human text prints the output directory, the Baseline's last-save timestamp with the same "(as of
FieldWorks' last save)" wording `baseline capture` and `assess` use, the Selection's word count, the total
file count, and the recorded Assessment ids (or `(none; --no-assess)`). `--json` binds to
`HandoffCommandResponse`: `outputDirectory`, `baseline` (a full `BaselineCaptureResponse`), `selection` (a
`SelectionProjection`), `files` (every Handoff-relative path written, forward-slashed, in write order), and
`invocationId` (the selected retained result, or null for `--no-assess`) and `assessmentIds` (its child
measurements).

Refusals of its own: `handoff.destination-exists`, `handoff.invocation-required`,
`handoff.invocation-not-found`, `handoff.invocation-mismatch`, `handoff.source-unavailable`,
`handoff.statistics-unavailable`, `handoff.text-not-found`, and `handoff.cancelled` (the run was
cancelled; no destination directory was created), `handoff.parser-unavailable` (the grammar import
invocation could not start, was refused, timed out, or produced no grammar file) — plus `selection.empty`,
`selection.text-not-found`, and
every `baseline capture`/`assess` refusal above, since `handoff` composes both the same way it does `stats`.
A mistyped project path is refused as `project.not-found` before any parser is built, the same guarantee
`assess` makes — pinned by `AMissingProjectIsRefusedBeforeTheParserIsEvenBuilt` (also pinned at the argv
layer by `ANonexistentProjectRefusesTheWayEveryOtherVerbDoes`), and an existing non-empty `--out` is
likewise refused before the project even matters — pinned by
`AnExistingNonEmptyOutputDirectoryRefusesBeforeTheProjectEvenMatters`.

## Related

- [ADR 0041 — The database is the only store](adr/0041-the-database-is-the-only-store.md)
- [ADR 0040 — There is one API, and it is the CLI](adr/0040-one-api-the-cli.md)
- [ADR 0021 — The CLI is the full product surface](adr/0021-cli-is-the-full-surface-layer-1-churns.md)
- [ADR 0039 — Baseline and live-host authority](adr/0039-one-worker-baseline-and-live-host-authority.md),
  whose Baseline, queueing, and PanGloss decisions this contract still rests on
- [CLI-to-worker API surface](cli-worker-api-surface.md), superseded, kept for the refusal-fidelity
  analysis that motivated collapsing the boundary
