---
name: cross-platform
description: >-
  Motif on Linux and macOS: the rules that keep code working off Windows, and how to diagnose a failure
  that only CI's Ubuntu or macOS jobs show. Use when code touches child processes, environment variables,
  file locks, paths, native libraries (SIL ICU, PanGloss), signals, stored timestamps, or the Avalonia
  dispatcher; when a test fails only on ubuntu or macos CI; or when a test launches a process or waits.
---

# Cross-platform Motif

Motif ships for win-x64, linux-x64, osx-arm64 and osx-x64, and CI runs the full suite on all four
(`.github/workflows/ci.yml`). There is no local Linux or macOS environment: CI is the only Unix, and each
Unix question costs a 15–30 minute round trip. So write the code right for every OS the first time, and
make every failure carry its own evidence.

## Rules

Each rule below is one this codebase has already paid for. Follow the positive form; the symptom tells you
the rule was broken.

### Child processes

- Launch Motif children through the shared launchers, which carry the environment rules below. Product:
  the parser via `PanGlossProcessEnvironment` (an allowlist); the runner via `ProcessRunnerLauncher`.
  Tests: `CliProcess` (`Start`, `StartQueuedWorkerAsync`, `TryStartQueuedWorkerAsync`) and
  `InterruptibleCli`.
- An allowlisted child environment carries `DOTNET_ROOT`, `DOTNET_ROOT_X64` and `DOTNET_ROOT_ARM64`. A
  .NET apphost child (the fake parser, the runner) finds its runtime only through them where .NET lives
  outside the default location, as on the macOS runners. Symptom: exit 131, "You must install .NET".
- Every child environment drops `ICU_DATA` (see SIL ICU below).
- Interrupt a CLI child with Ctrl+Break on Windows and SIGINT (`kill(pid, 2)`) on Unix; both reach the
  CLI's `Console.CancelKeyPress`. `InterruptibleCli` does both.
- Bound a wait on a child by the child's lifetime: poll `while (!child.HasExited)`. After
  `WaitForExit(timeout)` returns false, kill the child and treat it as absent; read `ExitCode` only after
  it has exited. A CLI that finishes without queueing the job you expected is an outcome for the test to
  judge (`TryStartQueuedWorkerAsync` returns null), not a harness failure.
- Every assertion on a child's exit code includes the child's stderr.
- Exit code 134 is SIGABRT: an unhandled .NET exception or a native abort. The child's stderr names the
  exception; on macOS the crash report (`.ips`) holds the native stack.

### SIL ICU and .NET globalization

- `FwDataProjectLoader.Init` owns ICU start-up, in this order: one culture-aware string compare (so .NET
  binds its own ICU before SIL ICU 70 is in the process), load the SIL libraries, set `ICU_DATA` only
  around `CustomIcu.InitIcuDataDir`, restore it.
- The SIL ICU libraries sit beside the apphosts (`nativeOutputDirectory` "."). Scripts read
  `tools/icu-payload.json` in its real shape: per RID, `libraries`, `nativeOutputDirectory`, `sources`.
- Test processes initialize ICU at module load (`tests/Shared/ProcessWritingSystemRepositoryInitializer.cs`),
  so no test starts a child while `ICU_DATA` is set.
- Symptom map:
  - "Could not load ICU data. UErrorCode: 2" in a child: it inherited `ICU_DATA`.
  - Abort inside `GlobalizationNative_LoadICU`: .NET bound its ICU after SIL's was loaded.
  - `FileNotFoundException` from `Normalizer2.GetInstance`: `ICU_DATA` was absent during `InitIcuDataDir`.
  - An ICU version of 78.x where a test expects 70: the SIL libraries weren't found beside the app.

### Locks, files and paths

- Unix file locks go through `UnixFileLock`: `open` flag values differ between Linux and macOS, an
  interrupted open is retried, and a failed open reports its errno and path. A lock that cannot be opened
  fails the one job that needed it, never the queue.
- Machine-wide mutexes and lock files take a per-test-process namespace in tests, as
  `MachinePanGlossQueue`'s test slot namespace does, so concurrent test projects never share one.
- A lock that guards against one rare operation (an update, a reset) is shared by ordinary activities and
  exclusive only for that operation.
- Unix checks permissions when a file is opened, so a read-only attribute leaves an already-open SQLite
  connection writable. Force a store failure in a test through a seam (for example
  `MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR`), not through file attributes.
- LibLCM writes `<project>.fwdata.lock` beside a project it has open; code that validates a folder's
  layout allows that file while its owner process is alive.
- Paths: `Path.Combine` and `Path.GetTempPath()` (macOS temp lives under `/var/folders/…`); apphosts carry
  `.exe` on Windows only. Test fakes write only into test-owned folders.

### Timing and waits

- Wait on a condition or on progress, bounded by the lifetime of the thing that makes progress. A fixed
  wall-clock deadline is a flake on a slower runner.
- CI runners have two or three cores and run slower than a desktop; `test.ps1` runs at most half as many
  test projects at once as there are cores.
- Polling a job's status by launching the CLI is slow on macOS, so a fast job can pass through a state
  between two polls. Treat any state past the one you waited for as reached.
- Stored timestamps that SQL compares as text share one fixed-width format. For jobs that is
  `JobTimestamp.FormatUtc` (seven fractional digits, `Z`), and test helpers seed rows with it too; as
  text, `…12.123Z` sorts after `…12.1234567Z`.

### Avalonia headless

- View models post UI work to the `SynchronizationContext` they captured when they were created.
  `Dispatcher.UIThread` belongs to views: touching it binds Avalonia's UI thread to the calling thread, and
  every later test in the process then fails "The calling thread cannot access this object because a
  different thread owns it."
- An await that resumes after the dispatcher has shut down uses `ConfigureAwait(false)`.
- Headless clicks wait until the target's bounds are stable across layout passes
  (`WalkthroughWindow.ClickControl`).
- Walkthrough pixel baselines match only the machine that captured them. CI reports pixel differences;
  `MOTIF_WALKTHROUGH_STRICT_BASELINES=1` turns them into failures on the reference machine.

### Test process isolation

`test.ps1` runs each test project in its own process, several at once. Each process gets a private
writing-system repository, a private PanGloss slot namespace, and offline SLDR lookups
(`MOTIF_TEST_SLDR_OFFLINE`). `test.ps1` sets `MOTIF_DEVELOPER_COMMANDS=1`; set it yourself when you run
`dotnet test` directly, or developer CLI commands refuse with exit 2.

## Diagnosing a failure only Ubuntu or macOS shows

Take these steps in order; each ends where its bold criterion says.

1. **Collect.** Download the run's test results and list failures per OS with
   `trx-failures.py` ([`ci-playbook.md`](ci-playbook.md) has the commands). Done when **every failing test,
   with its message, is listed for every failed job**.
2. **Classify.** Mark each failure deterministic (every run, or every OS) or intermittent, and find
   cascades: many failures with one identical message usually trace to the first one by start time. Done
   when **each failure has a class and each cascade has its first test**.
3. **Get evidence.** Before changing code, read the evidence the failure left: stderr, errno, the macOS
   crash report, the hang-dump sequence file. If there is none, add it in the product or the harness and
   rerun only the affected tests on a debug branch. Done when **the evidence names the failing call or
   state**.
4. **Fix the cause.** Change the product or the harness where the rule above was broken, and pin it with
   a test that fails without the fix. Loosening a timeout or skipping a test is not a fix. Done when **the
   new test is red without the fix and green with it**.
5. **Verify like CI.** Run `./test.ps1 -Configuration Release` (CI builds Release; Debug can hide
   failures) on a machine that isn't otherwise loaded, then push and watch all four CI jobs. Done when
   **every job is green, or its only failures are already-classified intermittents with their own fix in
   flight**.

When the same class of bug turns up a third time, add its rule here.
