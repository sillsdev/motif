Motif’s tests now prove its own project, result, identity, and cancellation behavior. PanGloss’s suite owns grammar conformance and parser-scale counts.

## Desktop service boundary

The removed `DesktopServiceBoundaryTests` cases configured `FakeCommandClient`, called it, and checked that it returned the configured value. Production consumers already prove the behaviors that matter:

| Removed fake-only case | Production consumer coverage |
| --- | --- |
| Baseline response | `BaselineViewModelTests.RefreshCommandUpdatesStateOnSuccess` |
| Baseline refusal | `BaselineViewModelTests.RefreshCommandLeavesStateUntouchedAndShowsTheRefusalOnRefusal` |
| Assessment progress | `AssessViewModelTests.RunningReplaysTheCommandsProgressStepsInOrderWithoutInventingAnyOfItsOwn` |
| Assessment refusal | `AssessViewModelTests.ARefusalThatIsNotCancellationReachesRefusedAndDisplaysTheCommandsOwnMessage` |
| Assessment cancellation | `AssessViewModelTests.CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode` |
| Statistics response | `StatisticsViewModelTests.LoadingForwardsTheSelectedGroupAndPopulatesRows` |
| Statistics refusal | `StatisticsViewModelTests.ARefusalLeavesTheLastSuccessfulResultVisibleButMarkedStale` |
| Handoff progress | `HandoffViewModelTests.RunningReplaysTheCommandsProgressStepsInOrderWithoutInventingAnyOfItsOwn` |
| Handoff cancellation | `HandoffViewModelTests.CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode` |

The boundary class still checks service-interface policy, desktop adapter ownership, folder-picker wording, and `FakeCommandClient`’s fail-fast behavior when no command response is configured.

## Motif integration and lifecycle

`SeededProjectRealTransferTests.AssessmentAndHandoffTransferTheRetainedResultWithoutChangingTheSavedProject` runs against the real released parser with a Motif-owned seeded project. It checks selected result words, nonempty parser evidence, transfer of the retained `InvocationId` and Assessment ids into Handoff, and an unchanged saved project. `HandoffWalkthroughTests.WritingHandoffPublishesFilesForCompletedAssessment` retains the real window-level release test for files, identity, and saved-project immutability.

The former slow-grammar timing cases now use seeded projects for Motif window behavior. Assessment cancellation and window disposal wait for a fake parser heartbeat and process id before canceling; Handoff cancellation does the same, then verifies that no output was published and a retry succeeds. `ConcurrentWalkthroughTests.TwoWindowsShareTheManagedRootWhileBothAssessmentsComplete` holds two separate fake parser processes in flight at once, then checks completion and each project’s retained invocation selection.

`RealParserLimitTests.AStepCappedWordFinishesUnderAHigherStepLimit` tests Motif’s step cap on a one-stem, one-infix Motif-authored project. It checks capped partial work and completion under a higher cap without requiring an exact analysis count.

## Imported grammar disposition

The removed project came from the Machine repository at `https://github.com/sillsdev/machine`, branch `conformance/correctness-fixtures`, commit `f150e2a005ce639f7d68ef17fb0db25b2f6aaa3c`, path `conformance/edge-cases/deep-optional-affix-nesting/`. Its source record described a synthetic project with thirteen entries, twelve optional prefix slots, exact one-reading cases, and a 924-reading case. That data tests HermitCrab grammar conformance and engine-scale analysis counts, which PanGloss owns.

Motif no longer copies or loads that project. Its Motif-owned LibLCM operation checks now use `PristineProjectFixture` and the explicit identities in `SeededProject`. The remaining mention in `EntryPointStartupTests` is a negative packaging assertion that smoke scripts do not refer to the removed fixture; it does not consume fixture data.
