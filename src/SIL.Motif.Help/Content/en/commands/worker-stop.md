# Stop the background runner

`worker stop` ends the background runner process that is holding a project store open or that serves the current worker root.

## When to use it

A command that needs the store to itself, such as `store upgrade`, refuses with "The project store is in use" while a runner still has the store open. The refusal names the runner (`holderPid`, `holderBuild`, `holderRoot`) when one is recorded. A queued job that nothing claims for a minute names the job in front of it and the runner's pid for the same reason. Run this command to end that runner. The next command that queues work starts a new one.

## Examples

```powershell
motif worker stop --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
motif worker stop
```

With `--project`, it stops every runner that records itself as holding that project's store, whichever worker root it serves, and also the runner the current worker root records when that runner is from a different build than this one (a lane that keeps one root across builds finds the old build's runner there). Without it, it stops the runner the current worker root (`MOTIF_WORKER_ROOT`, or the per-user default) records.

## What it prints

One line for each runner it ended, with its pid, build and worker root, for example `Stopped the runner (pid 4121, build 0.31.0 [3f2a], worker root /home/ann/.local/share/SIL/Motif).` It ends a process only when its name and start time match the record. A pid that now belongs to another program, or whose start time differs, is left alone and reported with the reason and the operating system command (`kill <pid>`, `taskkill /PID <pid> /F`) to end it by hand if it is the runner. A live process whose record carries no start time, or whose start time cannot be read, makes the command refuse with `worker.stop-failed` and the same hint; its record is kept. When no runner is running it says so and still succeeds. `--json` returns the same facts as `workers`.

The runner is ended at once. A job it was running stays Running until the next runner takes it back, so wait for that rather than cancelling it. If the runner is still alive ten seconds later the command refuses with `worker.stop-failed` and names the pid.

## Related commands

- [List queued jobs](cmd:jobs%20list) shows the queue a stalled runner is working through.
- [Upgrade the project store](cmd:store%20upgrade) is the command that most often needs the runner stopped.
