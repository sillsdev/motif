# Known issues

Things that are wrong, or unfinished, and are not waiting on a decision. Anything needing an owner's
decision belongs in [the grill queue](grill-plan-a.md) instead — the parser-contract findings are
section K there.

Each entry says what is wrong, how it was found, and what fixing it would involve. None of them
currently fail the build or the suite.

## Tests that pass but should not be trusted

**Two integration tests flake under full-suite load.** Both pass on a retry and both have failed once
each under a loaded run.

- `RunnerSpineTests.AKilledRunnerLeavesItsJobReclaimableRatherThanStranded` starts a runner holding a
  one-second lease, kills it, then `Thread.Sleep(1500)` and expects the next runner to reclaim an
  expired lease. That is a fixed sleep racing a one-second lease on a machine running ~1500 other
  tests. The observed failure was *"Expected a reclaimed attempt, saw attempt 1"*. Poll for the
  condition instead of sleeping a fixed interval.
- `BaselineBundleReceiverTests.PublishVerifiedAsync_ValidatesAnEmptySharedSettingsInAnExistingPublication`
  builds two zip archives whose DOS timestamps can straddle a two-second boundary.

Two flaky tests is where retrying starts to hide a real failure. Every retry so far has passed, so
there is no evidence of a product bug — which is exactly the state in which one would go unnoticed.

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
