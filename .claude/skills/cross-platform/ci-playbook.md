# CI playbook for Linux and macOS

The mechanics behind the diagnosis steps in [`SKILL.md`](SKILL.md). The repository is `sillsdev/motif`.

## Runs and results

- List runs: `gh run list -R sillsdev/motif -b <branch> -L 5`.
- Job results: `gh run view <run> -R sillsdev/motif --json jobs --jq '.jobs[]|"\(.name) \(.conclusion)"'`.
- Failed-step logs (setup, staging and workflow failures): `gh run view <run> -R sillsdev/motif --log-failed`.
- Test results are artifacts named `test-results-<os>`, uploaded when a job fails: per test project, a
  console log and a TRX file. Download them with
  `gh run download <run> -R sillsdev/motif -n test-results-<os> -D <dir>/test-results-<os>`, then list the
  failures with `python trx-failures.py <dir>` (run it with `PYTHONIOENCODING=utf-8` on Windows).
- A test that hung is killed after 10 minutes by the blame collector; the `Sequence_*.xml` file in that
  project's results names the test that was running, and the project's `.stderr.log` has the abort.
- macOS jobs upload `crash-reports-<os>` on failure: the `.ips` crash reports of processes that aborted.
  `python ips-stack.py <dir>` prints each report's faulting-thread stack.
- Rerun only the failed jobs of a run: `gh run rerun <run> -R sillsdev/motif --failed`.

## Targeted runs

A push to `mac-debug/**` or `linux-debug/**` runs only the tests listed in `.github/mac-debug.txt` or
`.github/linux-debug.txt` (one `<test project> | <filter>` per line; an empty filter runs the whole
project). Use them to test a fix or collect evidence in minutes instead of a full suite. A push to any
branch also starts the full CI; cancel that run (`gh run cancel <run>`) when you only want the targeted one,
so it doesn't hold macOS runners.

## GitHub limits

- Poll a run at most once a minute. Several tight `gh run watch` loops trip GitHub's secondary rate limit,
  which then refuses every API call for a while.
- Artifact downloads count against an account egress limit ("Egress is over the account limit"). When it
  trips, read `--log-failed` output instead and retry the download later.
- A push rejected with "Internal Server Error" while GitHub's status page shows everything operational can
  stick to one branch name; push the same commit to a new branch name.

## Local verification

- Verify with `./test.ps1 -Configuration Release`, matching CI.
- Other work on the same machine (other sessions' test runs, builds) can slow a run several times over and
  turn waits into timeouts. When every project runs far slower than usual, the machine was loaded; rerun it
  or let CI decide.
- On Windows, deleting a worktree can fail with "Filename too long" in build output; delete the rest with
  PowerShell's `Remove-Item -LiteralPath "\\?\<full path>" -Recurse -Force`.
