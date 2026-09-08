# Known issues

Things that are wrong, or unfinished, and are not waiting on a decision. Anything needing an owner's
decision belongs in [the grill queue](grill-plan-a.md) instead — the parser-contract findings are
section K there.

Each entry says what is wrong, how it was found, and what fixing it would involve. None of them
currently fail the build or the suite.

## Tests that pass but should not be trusted

**`TheFallbackEngineIsReachable_AndAgreesOnWhichWordsParse` is skipped, not deleted.** It compared two
parser engines; there is one engine now, so it compared a run against itself. Restoring it needs a
second engine to exist — see `K48`.

## Duplication worth removing

**Two renderers share a shape.** `CommandTextRenderer` and `ProposalCommandRenderer` both define
`Render<T>(CommandOutcome<T>, bool, bool) where T : class` and a matching `RenderRefusal` that builds a
`FailureEnvelope`. The only functional difference is one `corpus.` prefix check. A third such renderer
is plausible as more command families become typed.

**One resource path, two private constants.** `"SIL.Motif.Commands.Handoff.Assets.instructions.md"`
is a `private const` in both `HandoffWriter` and `HandoffViewModel`. Renaming the asset desyncs one
silently and the app's read fails at run time. An `internal const` on `HandoffWriter` would remove it;
the app already references Commands.

## Unfinished by choice

**The window does not stop Assess and Handoff running together.** The machine-wide PanGloss queue
serialises the real work, so a second run waits rather than competing. Whether the window should also
refuse to start both — so a person is not shown two things "running" with one blocked — was read as
already satisfied server-side. Worth a second opinion.

**`motif stats` takes no queue admission.** Every other parser launch runs under the machine queue and
so under the CPU and memory governor. The standalone `stats` verb does not. Adding admission there is
a scope decision about what the queue is for, not an oversight to patch.

## Not verified, rather than verified working

**Nobody has run the window against a real parser end to end.** The plan's own acceptance — complete
the workflow, then re-capture while a lock file is held — has not been performed.

**Tab order and display scaling are unchecked.** Accessible names are pinned by a test; visual
clipping at 125%, 150% and 200% is not headlessly checkable.

**Drag-and-drop into another application has only been exercised through a fake.**

## Corrections

Earlier entries stay as written; a correction is added here with its date.

### 2026-09-08 — the two flaky tests

Both integration tests that flaked under full-suite load are now deterministic in what they prove.
Recorded here because the first diagnosis of each was wrong, and the wrong diagnosis is the
thing a future flake in the same place would reach for.

- `RunnerSpineTests.AKilledRunnerLeavesItsJobReclaimableRatherThanStranded` was thought to be a fixed
  sleep racing a one-second lease. It was not: the failure text was *"Expected a reclaimed attempt, saw
  attempt 1"*, which means the killed runner had already finished the sub-second refresh between the
  poll that saw it running and the kill. The test now checks that the row is still running after the
  kill and requeues a fresh job when it is not, so a pass proves a dead owner was reclaimed.
- `BaselineBundleReceiverTests` built two zip archives with identical content and passed the first one's
  digest as the token for the second. Zip entry stamps have two-second granularity, so a retry that
  straddled a boundary hashed differently and the receiver correctly rejected it. The helper now stamps
  every entry with one fixed time. Any test in that class that builds a retry bundle was exposed, not
  only the one first blamed.

