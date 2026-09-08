# ADR 0044 — Every parser process is one PanGloss invocation

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
takes its turn in the machine's queue, runs inside the machine's bound, is stopped after a fixed time, and
reports what happened as an answer rather than an error, so nothing above it has to remember to catch
anything.

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
4. Every invocation carries a wall-clock cap. The default is ten minutes, the parser's own ratified execution
   limit, overridable per request. On expiry the process tree is killed, exactly as on cancellation. The
   per-word limit stays a batch argument; it bounds a word, not a process.
5. The module models only subcommands the shipped binary has: `batch`, `stats`, `import`, `parse`. It does
   not model `assess`. `PanGlossAssessmentProcess` stays outside the module, untouched, until K46 settles who
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

### 4. One wall-clock cap

K52 records that the per-word timeout is load-bearing for the process's survival, and nothing stopped a
process that ignored it. The cap is the parser's own limit rather than a number Motif invented, for the same
reason the memory ceiling is.

### 5. Only the real surface

The engine flag the real binary rejected, and the `assess` subcommand it never had, both entered Motif
because the binary's surface was described in six places and checked in none. One typed request per
subcommand is where that surface is written down once. The batch request passes `--threads 1`: the parser's
own hazards guidance for deep-truncation grammars, and the queue already serialises parsers machine-wide, so
fan-out inside one process buys nothing the bound would not take back. The parser's repository has accepted a
request for a machine-readable listing of its subcommands and flags; when it exists, a test pins `FakePanGloss` to it.

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
- **Model `assess` in the module because `FakePanGloss` already implements it.** Rejected: a module that
  documents the binary's surface must not document a subcommand the binary does not have.
