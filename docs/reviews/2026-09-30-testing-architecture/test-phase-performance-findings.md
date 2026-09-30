# Test phase performance findings

The largest individual test cost is portable-package verification, and walkthrough setup also repeats hidden-tab layout work. The last process to finish is a Commands shard, so the largest individual case is not the complete explanation of the final tail. This note records read-only findings from the existing Windows artifacts; no build or test was run for this investigation.

## Measurements

The 17 TRX files retained in `.tmp/test-runtime-measurements/candidate-6288d72e-prior-558s` span 552.75 seconds. The reported test phase was 558.3 seconds, leaving 5.55 seconds outside the TRX envelope. Their process wall times sum to 1,565.11 seconds, or 2.83 process-seconds per elapsed second across the span. The per-test-case durations sum to 1,659.57 seconds; they overlap across test processes and are not additional suite wall time.

The TRX intervals show at most three test processes active at once: three were active for 471.69 seconds, two for 68.98 seconds, and one for 12.08 seconds. This is process occupancy, not CPU occupancy; TRX does not record CPU, memory, disk, or competing-work samples. With the same 1,565.11 seconds of process work and a fixed three-process cap, the ideal scheduling floor is 521.70 seconds (`1,565.11 / 3`). The observed TRX span is 31.04 seconds above that floor. Keeping the observed 5.55 seconds outside the TRX envelope fixed gives a 527.26-second test-phase floor, about 31.04 seconds below 558.3. This bounds schedule-only savings for this run; reducing test work changes the bound.

| Process or case | Elapsed |
| --- | ---: |
| `SIL.Motif.Tests.Cli.shard2` process | 269.50 s |
| `PortableWorkerPackageTests.PortablePackageRunsCliAndSiblingWorkerWithTheRuntimeLibrary` | 209.33 s |
| All three publishes in that case | 201.33 s |
| App publish | 93.05 s |
| CLI publish | 52.72 s |
| Worker publish | 55.55 s |
| Next-longest process, App shard 3 | 146.41 s |
| Synthetic Bantu parser case | 51.10 s |
| Synthetic Turkic parser case | 49.60 s |

The package test occupies 37.5% of the reported test phase, and its CLI shard occupies 48.3%. The package test's measured publish work is the largest specific contributor in these artifacts. The separate parent measurement of 128.30 seconds for this package test does not match this TRX set's 209.33 seconds; those readings must be tied to their exact runs before using them as a before/after comparison.

Keep that publish cost separate from App-test timing: the four App shards took 132.80, 111.35, 111.23, and 146.41 seconds; App.Lifetime took 31.34 seconds. These current-run values alone do not show whether App tests inflated versus the retained same-revision run. The portable publish test is a distinct 201.33-second case component here, not evidence that App tests caused that increase. Compare matching App shard TRXs and matching package-test timings in the controlled same-revision gate before assigning cause.

There are 87 case names containing `Walkthrough`, with 272.35 seconds of summed case duration. This is concurrent case time, not 272.35 seconds of exclusive suite wall time. The slowest walkthrough cases in this set are replay (19.5 s), concurrent windows (18.9 s), and overview accuracy (18.6 s). The artifacts therefore show substantial UI workflow work, but do not isolate how much of it is layout setup.

## Walkthrough layout loop

`WalkthroughWindow.Show` initially lays out and pumps the window, then calls `RealizeEveryStage` (`tests/SIL.Motif.Tests.App/App/Walkthrough/WalkthroughWindow.cs:93-99`). The current method visits seven `WorkspacePage` values and selects all four `TextsTab` values for every page (`:431-447`), for 28 inner `UpdateLayout` plus dispatcher-pump passes. Including the surrounding setup, that is 30 layout calls and 29 pumps per `Show`.

The page template is selected by the current page (`src/SIL.Motif.App/Views/MainWindow.axaml:190`). The four tab contents belong to the Texts page (`src/SIL.Motif.App/Views/Pages/TextsPage.axaml:67-93`), and changing to `Lists` also initializes its selection (`src/SIL.Motif.App/ViewModels/TextsPageModel.cs:173-179`). Thus, cycling tabs while any of the other six pages is selected does not realize another visible Texts template. Each page still needs one visit; the four tab values still need a visit while Texts is selected.

A safe experiment is to keep the page enumeration, lay out and pump once for each non-Texts page, and do all four tab selections with a layout and pump while Texts is selected. That is 10 inner passes instead of 28, removing 18 layout/pump pairs per `Show` while retaining every page and tab state. Preserve the final Matrix tab and original-page restoration. This is a 64.3% reduction in the inner passes (18 of 28), or 60% fewer total layout calls per `Show`; no elapsed-time savings have been measured.

The exact source-supported reduction is 18 passes per `Show`; its measured time lower bound is zero. Neither these TRXs nor the current harness records how many `Show` calls ran or how long `RealizeEveryStage` took. The 87 walkthrough case durations cannot be used as a bound on this loop because they include project setup, parser and workflow work, and parallel overlap.

Before accepting that change, keep every `WorkspacePage` in the traversal and verify the page's walkthrough controls are discoverable after its visit. Verify each of the four Texts tab contents is realized while Texts is active, including the Lists initialization, and verify restoration to Matrix and the opening page. Retain the existing walkthrough replay and workflow assertions. Measure per-`Show` time and the full test phase on a comparable run before claiming a suite-level improvement. The loop touches Avalonia's dispatcher, so Linux and macOS CI remain required for cross-platform evidence.

## Fixture work

The project fixture already avoids rebuilding a full LibLCM project for every test. `PristineProjectFixture` creates and saves one master, then each test receives an isolated scratch copy; its source documents about 3.9 seconds to create a project versus about 35 milliseconds to copy and reopen the saved 48 KB project (`tests/SIL.Motif.Tests.Support/TestFixtures/PristineProjectFixture.cs:8-27, 103-127`). Sharing a mutable cache would defeat the fixture's isolation and collide with LibLCM's serialized cache constraint. The current evidence does not support replacing that fixture with shared mutable project state.

## Evidence limits

This is a single existing Windows TRX set, not a controlled before/after run. The three publish timings explain most of the long CLI shard, while parser and walkthrough cases are separate contributors distributed over concurrent test processes. No 120-second result is established. No tests were removed, skipped, or weakened, and no build or test was started while other workers were active.

## Bounded next experiment

Compare the planned Sol-owned same-revision gate with the archived candidate run before attributing any elapsed-time change to an optimization. Match test names and phases, and correlate them with CPU, memory, disk and competing-checkout samples. No measured saving from the walkthrough loop is established.

If a clean same-revision run still leaves the walkthrough loop as a candidate, make one diagnostic measurement of `RealizeEveryStage` call count and elapsed time without changing its behavior. Only if that measurement shows material time should a separate one-change experiment try the 10-pass traversal. Keep all page and tab realizations, restoration, replay/workflow assertions, and the existing deadlines; compare one controlled full `./test.ps1` run with the same parser and machine conditions. Stop after that comparison unless its measurements justify another bounded experiment.
