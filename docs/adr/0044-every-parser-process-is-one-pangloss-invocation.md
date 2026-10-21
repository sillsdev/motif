# ADR 0044 — Every parser process is one PanGloss invocation

When Motif runs PanGloss, its limits and results should describe the actual parser execution. This decision treats each parser command as one bounded, recorded invocation.

**Status:** accepted, 2026-09-08. Builds on [ADR 0039](0039-one-worker-baseline-and-live-host-authority.md)
decision 5 (PanGloss bounding), [ADR 0042](0042-a-job-produces-assessments-an-assessor-makes-them.md) (an
Assessor makes Assessments; adding one is an addition, not a redesign), and
[ADR 0043](0043-one-command-catalog-two-front-ends.md) invariant 2 (every command returns a typed result or a
typed Refusal). The parser-contract findings this answers are section K of [the grill queue](../grill-plan-a.md).

**In plain terms:** Motif starts the PanGloss parser as a separate program, and it had six different pieces
of code for doing so, each with its own copy of the same twenty lines: find the program, start it, read both
of its output streams, wait for it, kill it if it hangs, and decide what its exit meant. The machinery that
keeps a runaway parser from taking the machine down was built and connected to none of the six. A parser
failing mid-run threw an error that five of the six let escape, and the sixth caught it; one of those escapes
killed the Motif window when a person clicked Run. **From now on there is one way to run the parser.** It
takes its turn in the machine's queue, runs inside the machine's bound, observes its applicable time limit, and
reports what happened as an answer rather than an error, so nothing above it has to remember to catch
anything.

**Amended 2026-10-20** by the section "A long-lived parser session is an invocation" below.

## Context

The five files in `SIL.Motif.Host` that start `pangloss` — the batch parser, the assessment process, the
grammar import, the batch statistics run and the statistics query — each own a `ProcessStartInfo`, two
stream redirects, two drains, a wait-or-kill, a call to the executable locator, a mapping of a nonzero exit
to `ParserUnavailableException`, and an optional call to an `IParserProcessGovernor`. The governor is
threaded through every call as an optional parameter; two commands admit their launches through
`MachinePanGlossQueue`, one launches without admission, and the ledger recorded that as an open scope
decision. One launcher is synchronous with a fifteen-minute cap; the others are asynchronous with no cap.

Two defects a person found in the running application on 2026-09-08 lived in this scatter. The job object
carrying the CPU cap, the kill-on-close and the committed-memory ceiling was configured and assigned to no
process, because the assignment had to be written at five seams and was written at none. And the missing
`assess` subcommand — which the shipped binary does not have — surfaced as an exception escaping
`AssessCommand`'s run, through the view model, to the dispatcher.

The parser's own repository is mid-way through an architecture campaign of its own. None of its four
candidates changes the subcommand set, the `assess` question, or the containment limits; its statistics card
preserves the two facts Motif depends on. The right response to a moving target is to hold every fact Motif
knows about it in one place.

## Decision

This ADR establishes six invariants:

1. There is one module through which every `pangloss` process Motif starts is launched: the **PanGloss
   invocation**, as CONTEXT.md now defines it. `SIL.Motif.Host` owns it, and the machine queue and the
   Windows job object move into Host with it.
2. Every invocation takes admission through the machine's parser queue and runs inside its job object. No
   launch may skip either. This closes the standalone `stats` verb's admission question in the ledger.
3. An invocation returns an outcome and never throws for anything the parser did: completed with its output;
   refused with its exit code and standard error; unavailable because the executable was absent or would not
   start; timed out; cancelled. Exceptions remain for programmer error only. `ParserUnavailableException`
   leaves the seam callers cross.
4. Every invocation remains cancellable. A Batch without a per-word time limit has no default overall
   wall-clock deadline, so a legitimate long run may finish unless cancelled. Other invocations default to
   ten minutes, overridable per request. On expiry the process tree is killed, exactly as on cancellation.
   The per-word limit stays a batch argument; it bounds a word, not a process. A Batch with a per-word time
   limit gets ten minutes plus that limit for every word it names (no deadline once that exceeds what a timer
   can hold), so a long word list is never killed for its length alone.
5. The module models only subcommands the shipped binary has and Motif uses: `batch`, `stats`, `import`. It
   does not model `assess`. `PanGlossAssessmentProcess` stays outside the module, untouched, until K46 settles who
   produces the report. `FakePanGloss` models the same surface and no more.
6. The module is proven through `FakePanGloss`, because the executable seam has two adapters and that is the
   real one. Callers cross the module's interface with an in-process fake. The virtual-method subclassing
   seam on `PanGlossParser` retires.

### 1. One module, owned by Host

The queue and the job object are facts about the parser process, and the parser process is Host's. They sat
in `SIL.Motif.Worker` because that is where they were first needed, which left Host able to see containment
only through an interface with one production adapter. Moving them down removes that seam rather than
adding a second one. Worker keeps referencing Host; no dependency direction changes. Host was already
Windows-only in practice; it now says so in one place.

### 2. Admission and containment are the module's, not the caller's

The queue exists so several parsers cannot add up past the machine's bound. A launch that skips it is
exactly the case the bound is for. "This verb is quick" is the reasoning that let the governor go unwired,
and the module removes the opportunity to reason that way: a caller cannot obtain a process, only an
outcome.

### 3. An outcome, not an exception

ADR 0043 already requires every command to return a typed result or a Refusal. A parser outcome crossing a
seam as an exception meant every caller had to know to catch it, and one did. With the outcome as a value,
`AssessCommand`, `StatsCommand` and `HandoffCommand` map it to their Refusal codes in the one place each
already does so, and a new caller cannot forget.

### 4. Applicable wall-clock limits and cancellation

People must be able to stop a parser run, while a deliberately uncapped Batch may take as long as its
work requires. An overall deadline applies by default to other invocations; it does not turn an uncapped
Batch into a timed run. Explicit caller overrides remain available, and cancellation terminates containment.

### 5. Only the real surface

The engine flag the real binary rejected, and the `assess` subcommand it never had, both entered Motif
because the binary's surface was described in six places and checked in none. One typed request per
subcommand is where that surface is written down once. The batch request passes `--threads` using half of
`Environment.ProcessorCount`, with a minimum of one. Because deep-truncation grammars can multiply memory use
when words fan out, the queue leases both machine-wide slots and admits one parser job at a time. ADR 0048
records the CPU and memory limits. The parser's repository has accepted a request for a machine-readable
listing of its subcommands and flags; when it exists, a test pins `FakePanGloss` to it.

## Consequences

- `AssessCommand.Run`'s test-facing signature loses its assessor factory, statistics-query factory and queue
  parameters; it takes the invocation module's interface instead.
- The five launchers shrink to argument shaping and output parsing. `PanGlossStatsProcess` disappears into
  the batch request. `PanGlossParser` becomes asynchronous and loses its virtual methods.
- `IPanGlossStatsQuery`, `IPanGlossStatsRunner` and `IPanGlossGrammarImporter` are retired if nothing but the
  module's own tests would implement them; the two-adapter rule decides, case by case, during the plan.
- `MachinePanGlossQueue`, `WindowsCpuJob`, `WindowsCpuJobGovernor` and `MachineSlotLease` move from
  `SIL.Motif.Worker.PanGloss` to `SIL.Motif.Host`. `IParserProcessGovernor` is retired: containment is no
  longer a seam because nothing varies across it.
- The known-issues ledger's "standalone `stats` takes no queue admission" entry closes by construction.
- What this ADR does not settle: who produces the assessment report (K46), whether `ParserEngine` retires
  when the parser's explicit-backend choice reaches its command line (K48), and where Assessment provenance
  comes from now that the parser's repository has confirmed the statistics meta row carries input identity
  only (K49). Those stay in the grill queue.

## A long-lived parser session is an invocation

**Amended 2026-10-20.** Owner decision, 2026-10-20: one resident `pangloss serve` process per open project is
acceptable, and opening the Try a Word page in the App starts loading that project (prewarm). Loading a large
lexicon costs about ten seconds and about 1.1 GB, and a word then costs about 0.1 seconds; one process per word
pays the load every time. Invariants 2 to 4 are restated for a process that lives for minutes:

1. **A session is an invocation.** It is started through the same Host module, and `PanGlossInvoker` remains the only
   route for every one-shot request. The resident process is `WarmPanGlossPool`, in Host, because the pool owns
   a process that outlives a request.
2. **Admission per request.** The load takes machine-queue admission, and so does every request, and each holds the
   machine's slots only while it runs. A word waiting behind an Assessment waits in the queue like any other parser job.
3. **Containment and the memory ceiling for the life of the session.** The process runs inside its own containment
   job from start to end, held to a memory ceiling of 3 GB (about 2.5 GB was the largest peak measured). A session past
   the ceiling is killed, becomes Failed and its words take the cold path.
4. **Idle timeout.** A session unused for ten minutes (default) is shut down: input closed, then killed. The next word
   or prewarm starts another. A load that is not ready within 120 seconds is killed and Failed, so no state waits forever.
5. **Cancel kills, then restart.** Cancelling a parse kills the process, because the protocol has no cancel; the next
   word starts a new one. Cancelling a wait for a load abandons only the wait.
6. **Cold-path fallback.** Any load failure, crash, ceiling kill, unreadable reply or per-word error sends that word
   through the one-shot `parse` invocation, so the person gets a result or the cold error. A failed session is retried by
   the next prewarm, or by a word after a 30-second cooldown. A parser that lacks `serve` protocol 1 (its `--describe`
   has no `serve` command, or `interfaces.serveProtocol` is absent or not 1; the release manifest declares the same
   `serveProtocol` once the pin carries it) is
   never started: every word runs cold. The pin of 0.8.8 lacks it.
7. **Keying and invalidation.** Sessions are keyed by Motif project plus BaselineToken and the Baseline's grammar path.
   A new Baseline is a new token and replaces the session, killing the old process. Draft and Trial scratch projects
   (a Proposal's trace) never use a session: they take the cold path.
8. **Who hosts it.** Long-lived hosts (the MCP server, the App) enable sessions; a one-shot CLI process does not, since it
   would exit before a second word could use the load. MCP never applies: a session only parses.

## Rejected alternatives

- **A generic process runner: given arguments, run contained and return what came back.** Rejected: it
  concentrates the mechanics and leaves the surface knowledge — which flags exist, which the binary rejects —
  spread across the callers, which is where both surface defects lived.
- **Keep admission in the commands and let the module launch.** Rejected: it preserves the one path that
  today runs unadmitted and relies on every future caller remembering the queue. The module cannot enforce a
  bound it does not own.
- **Keep the exception and catch it in every caller.** Rejected: the crash this ADR answers was exactly one
  caller not catching it. A value cannot be forgotten.
- **Wait for the parser's own architecture campaign to land, then design against the result.** Rejected: the
  campaign changes nothing Motif calls, and concentrating Motif's knowledge of the binary first is what
  makes the next change to that binary a one-file adjustment rather than a six-file hunt.
- **Keep one process per word.** Rejected for Try a Word on large lexicons: the load, not the parse, is over 99% of the
  time.
- **Model `assess` in the module because `FakePanGloss` already implements it.** Rejected: a module that
  documents the binary's surface must not document a subcommand the binary does not have.
