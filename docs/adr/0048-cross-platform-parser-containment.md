# ADR 0048 — Parser containment and worker identity across operating systems

People should be able to use PanGloss on Windows, Linux or macOS and understand which limits applied to a run. This decision makes parser containment visible across those systems.

**Status:** accepted, amended 2026-09-28.

**In plain terms:** Motif can now run PanGloss on Windows, Linux, and macOS. Each run reports the limits its operating system applied, including when that system cannot enforce the same limits as Windows.

## Context

Motif admits at most two PanGloss runs across worker processes. Each admitted run also needs a CPU ceiling, a memory ceiling, and a way to stop the parser and its descendants when the run ends or is cancelled. Windows provides all four controls through a Job Object. Linux and macOS do not provide that same mechanism, so the host must select controls that exist on each system and report any guarantee it cannot provide.

The queue's names begin with `Global\`, and the worker owner name begins with `Local\`. Those prefixes describe Windows terminal-session namespaces. On Unix-like systems, .NET named mutexes use filesystem-backed names; the prefixes do not give them Windows machine-wide or session-wide scope. The .NET documentation also warns that other Unix users can interfere with named mutexes.

## Decision

### Windows

Keep `WindowsCpuJob` as the implementation. Its Job Object retains the 2500-basis-point CPU hard cap, the 10 GiB committed-memory ceiling for the whole job, and kill-on-close behavior. Motif starts each process and assigns it to the job through the existing suspend-then-assign path before allowing it to continue.

### Linux

When the current process has a delegated, writable cgroup v2 parent with both `cpu` and `memory` controllers enabled and `cgroup.kill` available, create a child cgroup for the invocation. Set `cpu.max` to `25000 100000`, `memory.max` to 10 GiB, and `memory.swap.max` to zero when that control exists. `posix_spawn` creates the shell wrapper directly in a new process group. Before `exec` starts PanGloss, the wrapper moves itself into the child cgroup; close or cancellation writes `1` to `cgroup.kill` and also kills the process group. A descendant permitted to move itself out of both groups can escape termination, and the outcome reports that limitation.

If no such cgroup is available, set a hard and soft `RLIMIT_AS` ceiling of 10 GiB in the wrapper. This is a per-process address-space limit, not an aggregate memory ceiling, and it does not impose a CPU rate. `RLIMIT_CPU` is not used as a substitute because it limits total CPU time rather than CPU rate. The process group is still killed on close, but a descendant that deliberately leaves that group can survive.

### macOS

Mac users get a parser memory ceiling even though the kernel rejects finite memory rlimits. Motif samples the parser process group's total physical footprint and kills the group when it crosses the ceiling.

Start the wrapper in a new process group with `posix_spawn`, then `exec` PanGloss without setting `RLIMIT_DATA` or `RLIMIT_AS`. Finite requests for either rlimit return `EINVAL` on the supported macOS runner, including when the inherited soft and hard values are infinite. Treat that result as an unavailable kernel control, not a launch failure.

Every 50 ms, a watchdog gets the process ids in the parser's group with `proc_listpids(PROC_PGRP_ONLY)` and sums their `ri_phys_footprint` values from `proc_pid_rusage(RUSAGE_INFO_V2)`. When the sum exceeds the configured ceiling, the watchdog kills the process group; the invocation reports the resulting nonzero exit as a PanGloss refusal. Failure to enumerate or sample the group also kills it, so the parser never runs after its memory control becomes unobservable.

This is a sampled ceiling rather than a kernel hard limit, so the group can exceed it until the next check and a peak released between checks can be missed. macOS still has no CPU rate cap. A descendant that leaves the process group can escape both measurement and termination.

### Outcome reporting

`PanGlossContainmentJob` is the OS-neutral interface, and a factory selects the Windows, Linux, or macOS implementation. `PanGlossOutcome.Containment` records the CPU control, memory control, aggregate-memory status, process-tree control, and limitations that applied to the run. A missing cgroup is therefore visible to callers instead of being presented as equivalent to a Windows Job Object.

### Worker and store ownership

On Windows, keep the existing machine-slot mutex names `Global\MotifPanGlossSlot-0` and `Global\MotifPanGlossSlot-1`, and keep the worker owner name byte-identical, including its `Local\` prefix and SID. On Linux and macOS, use stable files under `/tmp` with nonblocking `flock`: machine-slot files are shared across users with mode `0666`, while worker-owner files include the effective user id and use mode `0600`. These locks coordinate cooperating Motif processes; a local process that ignores advisory locks is outside the guarantee.

For SQLite ownership, `FileShare.None` remains the exclusive-open mechanism on every OS. On Unix it supplies the advisory file lock, so the `.owner.lock` file is kept at a stable path after close; unlinking it could let another process lock a new inode while a waiter still refers to the old one. Windows retains the explicit byte-range lock and delete-on-close behavior. No Unix `FileStream.Lock` call is made, avoiding the unsupported macOS operation.

## Consequences

- `PanGlossInvoker` and `MachinePanGlossQueue` are no longer Windows-only APIs; only platform implementations carry OS support annotations.
- A Linux cgroup-enabled run can match the Windows job's CPU rate, aggregate memory, and tree-kill controls. Linux without a delegated cgroup reports its per-process address-space limit; macOS reports its sampled process-group footprint ceiling and its sampling overshoot.
- Unix process groups are established by `posix_spawn` before the wrapper can run or spawn a child. Resource limits and cgroup placement happen in that wrapper before it executes PanGloss.
- Linux machine-slot admission crosses user boundaries through shared lock files instead of relying on `Global\` named-mutex behavior.
- The store's Unix ownership file remains on disk so all processes lock the same file identity.

## References

- [.NET `Mutex` documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex): Unix named mutexes are filesystem-backed, and the Windows `Global\`/`Local\` prefixes describe terminal-session namespaces.
- [POSIX `posix_spawn`](https://pubs.opengroup.org/onlinepubs/007904975/functions/posix_spawn.html): defines spawn attributes and file actions used to set the process group before execution.
- [Linux cgroup v2 documentation](https://docs.kernel.org/admin-guide/cgroup-v2.html): defines `cpu.max`, `memory.max`, and cgroup controls.
- [Apple `setrlimit(2)` documentation](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/setrlimit.2.html): documents `RLIMIT_CPU` and `RLIMIT_DATA` as per-process limits.
- [Apple XNU `resource.h`](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/resource.h): defines `RUSAGE_INFO_V2` and `ri_phys_footprint`.
- [Apple XNU `proc_info.h`](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/proc_info.h): defines process-group enumeration and process information structures used by `libproc`.
- [Linux `flock(2)` documentation](https://man7.org/linux/man-pages/man2/flock.2.html): documents exclusive advisory locks and nonblocking acquisition.
