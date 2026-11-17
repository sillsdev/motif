# ADR 0019 — Every parser run is one contained PanGloss invocation

**Status:** Accepted, 2026-11-14.

## Context

Every parser path needs the same admission, cancellation, containment and truthful completion reporting.
PanGloss owns parsing; Motif owns durable workflow and review-facing evidence. A resident parser can avoid repeated
load cost, but its requests must obey the same machine limits as batch work.

## Decisions

### D1. Host owns the invocation module

Every one-shot PanGloss execution goes through PanGlossInvoker in Host, including batch, stats, import and parse.
Model the pinned executable's actual typed subcommands/flags; never invent `assess` or a generic argument runner
that scatters surface knowledge among callers. An Assessor creates Assessments using these outcomes, not a
nonexistent parser command. Keep machine queue and platform containment in Host, not optional caller governors.
Callers receive an outcome, never a process they could run outside admission.

Outcomes distinguish completed output, refused exit/stderr, absent/unstartable executable, timeout and cancellation.
Parser behavior does not throw across the seam; exceptions are for programmer errors. Commands map outcomes into
stable typed Refusals under
[ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md). Prove the executable adapter with FakePanGloss
matching the pinned binary; callers use an in-process fake of this module rather than subclassing PanGlossParser.
A machine-readable --describe contract pins supported commands/flags and resident protocol support.

### D2. Admission, cancellation and deadlines are part of execution

Each invocation takes machine-wide queue admission; one job leases both machine slots, with half of
Environment.ProcessorCount batch threads (minimum one). One user worker orders projects through the durable queue;
ordering across users is unspecified. A caller cannot bypass admission because a verb seems quick. Export a candidate's
private interchange inputs, build fresh engines for Proposal Assessments, retain results/bounded logs rather than
engines, and delete ephemeral exports on completion or startup recovery.

Every run is cancellable and cancellation kills its process containment. A Batch without a per-word time limit
has no default overall deadline. Other invocations default to ten minutes with explicit overrides. A Batch with
per-word time limit gets ten minutes plus that limit for every named word; if the duration exceeds timer capacity
there is no overall deadline. Per-word time bounds a word, not the whole batch. Timeout kills the process tree.

### D3. Platform containment reports the guarantees it actually supplies

PanGlossContainmentJob selects platform controls and PanGlossOutcome.Containment records CPU, memory,
aggregate-memory status, process-tree control, applied limit and limitations.

| Platform | CPU | Memory and termination |
| --- | --- | --- |
| Windows | Job Object 5000-basis-point hard cap | aggregate committed-memory limit; suspend/assign before resume; kill-on-close |
| Linux, delegated writable cgroup v2 with cpu/memory and cgroup.kill | cpu.max = processor count × 100000 / 2, period 100000 | child memory.max; memory.swap.max=0 where available; wrapper joins before exec; cgroup.kill plus process-group kill |
| Linux without that cgroup | half-machine thread budget, no hard CPU rate | sampled process-group resident/shared-written/swapped memory; group kill |
| macOS | half-machine thread budget, no hard CPU rate | sampled physical footprint of process group; group kill |

On Unix, posix_spawn establishes a new process group before the wrapper can run/spawn, and controls are installed
before exec. Linux fallback samples every 50 ms, refreshing /proc membership every tenth sample or when all known
members exited, summing RssAnon, RssShmem and VmSwap. macOS enumerates the group with proc_listpids and sums
ri_phys_footprint through proc_pid_rusage(RUSAGE_INFO_V2) every 50 ms. Enumeration/sampling failure kills the group
rather than allowing unobservable control. Sampling can overshoot and miss between-sample peaks. Descendants
permitted to leave the relevant group/cgroup can escape measurement/termination; report this limit.

Do not use RLIMIT_CPU as a CPU-rate substitute or RLIMIT_AS/RLIMIT_DATA as memory controls: reserved per-thread
stacks are not used memory, and macOS rejects finite memory rlimits. Windows charges committed pages and macOS
footprint counts touched memory. Thread count alone cannot bound pathological-word memory.

### D4. The memory ceiling is a setting with a typed failure

Default to 75% of memory the runtime reports available to Motif, respecting container/cgroup limits rather than
assuming physical RAM. MOTIF_PARSER_MEMORY_LIMIT accepts bytes, binary suffixes such as 12GiB, or none. Invalid values
refuse naming the variable and accepted forms; never replace them with default. `none` removes the memory ceiling
on every path (no job memory cap, cgroup memory.max=max, no sampled watchdog), retaining CPU and tree termination.
Every outcome states the applied limit or its absence. A resident session uses its separate lifetime ceiling in D6.

Memory stoppage is a typed signal, not message matching: watchdog sample, cgroup OOM-kill count/memory.peak where
available, or a Windows failed-job peak within 5% of its limit, explicitly a likely-limit heuristic. Map it to
`assess.parser-memory-limit` / FailureReason.Refused for assess, stats and grammarcheck. Name limit, known peak,
setting and remedies (fewer threads, per-word work bound, deliberate raised/removed ceiling). Do not call it a missing
parser. Exact Windows limit-violation records remain a follow-up, not a claimed guarantee.

### D5. OS locks coordinate cooperating processes

Windows preserves Global\MotifPanGlossSlot-0/-1 and byte-identical Local worker-owner names including SID; one run
leases both slots. Unix uses stable /tmp files with nonblocking flock: machine slots mode 0666 across users, worker
owner mode 0600 including effective uid. Advisory locks protect cooperating Motif processes, not arbitrary local
programs that ignore them. Windows Global/Local prefixes confer no Unix cross-user guarantee.

SQLite ownership uses FileShare.None. Keep Unix .owner.lock paths after close so waiters and new holders lock the
same inode; never unlink/recreate the file. Windows retains byte-range locking and delete-on-close. Do not call
unsupported FileStream.Lock on macOS. Worker ownership is user/resolved-root-scoped under ADR 0018.

### D6. A warm session is also an invocation

WarmPanGlossPool in Host may keep one resident `pangloss serve` per open project for App/MCP. Opening Try a Word
prewarms it; one-shot CLI processes do not retain sessions. Sessions key on project, full BaselineToken and grammar
path; replacing a Baseline kills/replaces the old session. Draft/Trial scratch trace work always takes the cold path.

Loading and every request acquire admission only while active. Containment remains for the whole process lifetime
with a 3 GB ceiling. Ceiling kill marks Failed and routes words cold. Idle timeout defaults to ten minutes: close
input then kill; next prewarm/word starts again. Load not ready in 120 seconds is killed/Failed. Cancelling a parse
kills the process (the protocol has no cancel); next word restarts. Cancelling a load wait abandons only that wait.

Load failure, crash, ceiling kill, unreadable reply or per-word error falls back to a one-shot parse, returning its
result or cold refusal. Failed sessions retry on prewarm or a word after 30 seconds. Start serve only when --describe
advertises it and interfaces.serveProtocol is exactly 1, consistent with the release manifest. Otherwise every word
runs cold. A warm parser only parses; enabling one never authorizes MCP Apply.

### D7. Stem experiments use the supplied lexicon

Use PanGloss's supplied-lexicon overlay and classification matrix/guide rather than reloading/recompiling a grammar
or rebuilding the classifier in Motif. The overlay adds ordinary free literal stems to signatures already observed
in the official lexicon. Signature ids derive from authored category/feature/value/inflection-class identities and
canonical encoding, never display names. Expected revisions guard overlay edits; revision participates in analysis
cache identity. Morphology is unapplied before trie lookup, including supported inflected/compound forms; do not
attach an unproved delta-FST compounding limitation to this mechanism.

A refused supplied entry needs grammar investigation: new category, bound stem or irregular allomorph routes to a
grammar Proposal, not a guessed overlay extension. Many refusals also warrant checking language, orthography or whether
inputs are wordforms rather than stems. PanGloss does not persist/sync/promote the batch or mutate loaded grammar.
Motif stores the Proposal/evidence/Receipt and promotes accepted stems by ordinary project Apply. The overlay is
scratch, never storage. Agents can reason over the matrix; people can use the adaptive yes/no/unknown question guide.
Both consume PanGloss's classifier. Pin the young API; material overlay/full-rebuild disagreement reopens this choice.

### D8. Measurements retain inputs and completion facts

A coverage number cites exact text/corpus identity and hash, overlay revision and grammar identity, plus per-word
timeout and timeout count where used. A changed corpus hash makes the old number stale. Timeout is neither a negative
parse nor a grammar failure; coverage with timeouts is explicitly a lower bound. Parser work/limits and actual
executable provenance also accompany results. Store derived evidence durably without letting a transient engine own it.

PanGloss answers parse results, including known-good/known-bad suites with closed-world/required/allowed expectations
and complete empty sets for “must not parse”. FieldWorks defines human-agreement semantics; Motif delivers its counts
headlessly using the same analysis comparison and ParserReport names/JSON shape: NumWords, NumZeroParses,
NumParseErrors, TotalAnalyses, TotalUserApprovedAnalysesMissing, TotalUserDisapprovedAnalyses,
TotalUserNoOpinionAnalyses and TotalParseTime. Cannot-reference ParserCore is a reason to reimplement definitions,
not invent different ones. ADR 0020 owns same-analysis and Assessment comparison contracts.

Report both disapproved and no-opinion analyses: sparse human judgments limit precision claims, while no-opinion
counts show where judgment work remains. Any analysis may establish that a feature combination occurs or reveal
untested combinations, never that machine-produced readings are correct. A rule cannot vindicate itself from its own
output. Motif's feature coverage joins declared and exercised FieldWorks GUIDs, including interactions, not only a
checklist. Holes can mean broad rules or missing examples. This differs from parse/Text Coverage and PanGloss compiler
capability/plan-interaction coverage; always name the measure. The human or AI linguist chooses the fix, including
whether an ad hoc cluster suggests a better analysis. Motif supplies evidence, never a verdict.

### D9. Case handling is in the host

Parse every word as written. When whole-word lowercase differs, also parse it even if the original succeeded.
Use the default vernacular writing system's ICU CaseFunctions (case alias, otherwise ICU locale), never generic
ToLower or Unicode folding. Lowercase readings belong to a separate lowercase wordform; never merge into the
original or suppress either. Assess/Trial put deduplicated lowercase forms in the same batch, marking origin;
shared/already-requested forms parse once. Try a Word returns lowercase trace beside original. Include its parser
work in totals. PanGloss and HermitCrab stay exact-match.

There is no case-required lexeme flag, no partial/midword-capital algorithm, and no per-wordform writing-system
casing while parser input is a bare string. Multiple vernacular systems therefore use the default's rules even when
they differ; report this accepted boundary rather than claiming exact per-WS FieldWorks behavior.

### D10. Parser work is the common measure and bound

Read PanGloss work_steps as “parser work” for word, rule and run. The per-word “parser work limit” defaults to
20,000,000, passed as --work-cap. Do not read cost as Motif's measure: it lacks the statistics/per-rule/cap surface.
PanGloss no longer takes an analysis-attempt --step-cap; do not derive/send one. Time estimates retain the existing
per-attempt rate with 100 parser work per allowed attempt. Workflow/migration/trace steps retain their own names.

Contract members, JSON, store columns, resources, project settings, MCP perWordParserWorkLimit and CLI
--parser-work-limit use the common vocabulary. Parser wire keys work_steps/workSteps remain at the read boundaries
with explanatory comments, not propagated as product terms. Read legacy per-word-step-limit, perWordStepLimit and
`{"steps":N}` as 100×N. Schema 60 converts Named Selection attempt values by the same factor while preserving authored
tables and dropping recomputable results under ADR 0023. Old scripts/arguments must change; old saved values still open.

## Open

The health strip shows a green/yellow/red speed light and parser-step numbers only in its hover under
ADR 0001 decision 3. The aggregation, thresholds, pre-measurement display and any conversion between that
step count and parser work remain Open; display wording does not change D10's work measure or saved-value
conversion. Exact Windows memory-limit notification, broader overlay/full-rebuild equivalence and
ParserReport-count conformance require their own proofs.

## Consequences

Every parser path has one enforceable place for its real executable surface and limits. Results distinguish
unfinished searches from negative evidence, preserve their provenance and expose weaker OS guarantees. Warm sessions
avoid repeat loading while sharing admission. Parsing, durable workflow and linguistic judgment have explicit owners.
