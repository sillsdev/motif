# ADR 0048 — Parser containment and worker identity across operating systems

People should be able to use PanGloss on Windows, Linux or macOS and understand which limits applied to a run. This decision makes parser containment visible across those systems.

**Status:** accepted, amended 2026-10-09.

**In plain terms:** Motif can now run PanGloss on Windows, Linux, and macOS. Each run reports the limits its operating system applied, including when that system cannot enforce the same limits as Windows.

## Context

Motif admits one PanGloss run at a time across worker processes. Each admitted run also needs a CPU ceiling, a memory ceiling, and a way to stop the parser and its descendants when the run ends or is cancelled. Windows provides all four controls through a Job Object. Linux and macOS do not provide that same mechanism, so the host must select controls that exist on each system and report any guarantee it cannot provide.

The queue's names begin with `Global\`, and the worker owner name begins with `Local\`. Those prefixes describe Windows terminal-session namespaces. On Unix-like systems, .NET named mutexes use filesystem-backed names; the prefixes do not give them Windows machine-wide or session-wide scope. The .NET documentation also warns that other Unix users can interfere with named mutexes.

## Decision

### Windows

Keep `WindowsCpuJob` as the implementation. Its Job Object applies a 5000-basis-point CPU hard cap, the 10 GiB committed-memory ceiling for the whole job, and kill-on-close behavior. Motif starts each process and assigns it to the job through the existing suspend-then-assign path before allowing it to continue.

### Linux

When the current process has a delegated, writable cgroup v2 parent with both `cpu` and `memory` controllers enabled and `cgroup.kill` available, create a child cgroup for the invocation. Set `cpu.max` to `Environment.ProcessorCount * 100000 / 2 100000`, `memory.max` to 10 GiB, and `memory.swap.max` to zero when that control exists. `posix_spawn` creates the shell wrapper directly in a new process group. Before `exec` starts PanGloss, the wrapper moves itself into the child cgroup; close or cancellation writes `1` to `cgroup.kill` and also kills the process group. A descendant permitted to move itself out of both groups can escape termination, and the outcome reports that limitation.

If no such cgroup is available, the kernel enforces neither a CPU rate nor a memory ceiling for the run. Motif then samples the parser process group's memory every 50 ms, as on macOS, and kills the group when the sum crosses the 10 GiB ceiling. A full scan of `/proc` finds the group's members by process group id every tenth sample, and sooner when every known member has exited; each sample sums `RssAnon`, `RssShmem` and `VmSwap` from `/proc/<pid>/status`. That counts memory a process has written, not address space it has reserved. It does not impose a CPU rate: Motif limits batch threads to half of `Environment.ProcessorCount`, with a minimum of one, and admits only one job at a time. `RLIMIT_CPU` is not used as a substitute because it limits total CPU time rather than CPU rate. The process group is still killed on close, but a descendant that deliberately leaves that group escapes both measurement and termination.

A per-process `RLIMIT_AS` is not used. PanGloss reserves about 1 GiB of stack for each parser thread and touches little of it, and `RLIMIT_AS` counts the reservation, so a 10 GiB address-space limit refused a ninth thread with `EAGAIN` long before the run used 10 GiB. `RLIMIT_DATA` counts the same writable stack mappings and fails the same way.

### macOS

Mac users get a parser memory ceiling even though the kernel rejects finite memory rlimits. Motif samples the parser process group's total physical footprint and kills the group when it crosses the ceiling.

Start the wrapper in a new process group with `posix_spawn`, then `exec` PanGloss without setting `RLIMIT_DATA` or `RLIMIT_AS`. Finite requests for either rlimit return `EINVAL` on the supported macOS runner, including when the inherited soft and hard values are infinite. Treat that result as an unavailable kernel control, not a launch failure.

Every 50 ms, a watchdog gets the process ids in the parser's group with `proc_listpids(PROC_PGRP_ONLY)` and sums their `ri_phys_footprint` values from `proc_pid_rusage(RUSAGE_INFO_V2)`. When the sum exceeds the configured ceiling, the watchdog kills the process group; the invocation reports the resulting nonzero exit as a PanGloss refusal. Failure to enumerate or sample the group also kills it, so the parser never runs after its memory control becomes unobservable.

This is a sampled ceiling rather than a kernel hard limit, so the group can exceed it until the next check and a peak released between checks can be missed. macOS has no hard CPU rate cap; Motif limits PanGloss batch threads to half of `Environment.ProcessorCount`, with a minimum of one. A descendant that leaves the process group can escape both measurement and termination.

### Outcome reporting

`PanGlossContainmentJob` is the OS-neutral interface, and a factory selects the Windows, Linux, or macOS implementation. `PanGlossOutcome.Containment` records the CPU control, memory control, aggregate-memory status, process-tree control, and limitations that applied to the run. A missing cgroup is therefore visible to callers instead of being presented as equivalent to a Windows Job Object.

### Worker and store ownership

On Windows, keep the existing machine-slot mutex names `Global\MotifPanGlossSlot-0` and `Global\MotifPanGlossSlot-1`, and keep the worker owner name byte-identical, including its `Local\` prefix and SID. One run leases both machine-slot names for its full lifetime. On Linux and macOS, use stable files under `/tmp` with nonblocking `flock`: machine-slot files are shared across users with mode `0666`, while worker-owner files include the effective user id and use mode `0600`. These locks coordinate cooperating Motif processes; a local process that ignores advisory locks is outside the guarantee.

For SQLite ownership, `FileShare.None` remains the exclusive-open mechanism on every OS. On Unix it supplies the advisory file lock, so the `.owner.lock` file is kept at a stable path after close; unlinking it could let another process lock a new inode while a waiter still refers to the old one. Windows retains the explicit byte-range lock and delete-on-close behavior. No Unix `FileStream.Lock` call is made, avoiding the unsupported macOS operation.

## Consequences

- `PanGlossInvoker` and `MachinePanGlossQueue` are no longer Windows-only APIs; only platform implementations carry OS support annotations.
- A Linux cgroup-enabled run applies a CPU quota for half of `Environment.ProcessorCount` CPUs and can match the Windows job's aggregate memory and tree-kill controls. Linux without a delegated cgroup reports its sampled process-group resident-and-swapped ceiling, its sampling overshoot, and its lack of a hard CPU rate; macOS reports its sampled process-group footprint ceiling, its sampling overshoot, and the lack of a hard CPU rate.
- Each batch uses at most half of `Environment.ProcessorCount` PanGloss threads, with a minimum of one, and one machine-wide lease admits only one parser job at a time. The 10 GiB per-job memory ceiling remains in place because PanGloss has no safe per-thread memory bound for pathological words.
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

## Amendment — 2026-10-09

Larger assessments can now use more of the computer while Motif keeps parser work within half of machine CPU capacity. Motif sets the batch thread count from `Environment.ProcessorCount`, tightens machine-wide admission to one job, and retains the 10 GiB per-job memory ceiling.

PanGloss measurements found that a 20-thread run on a deep-truncation grammar exceeded 30 GiB of resident memory when pathological words ran concurrently; a one-thread run with per-word timeouts completed that sample in about two minutes. There is no safe per-thread memory maximum, so parallel parsing does not justify raising the existing memory ceiling.

## Amendment — reserved stacks are not memory

On Linux machines without a delegated cgroup, a parser run with many threads no longer fails at start. Motif now measures the memory such a run actually uses and stops it at the same 10 GiB ceiling, instead of limiting how much address space it may reserve.

The `RLIMIT_AS` fallback counted the roughly 1 GiB stack PanGloss reserves for each parser thread, so with ten or more threads PanGloss 0.6.2 and 0.7.0 panicked with `ThreadPoolBuildError … WouldBlock` before parsing a word. The Linux section above now describes the sampled process-group ceiling that replaced it. Windows charges a thread stack against the Job Object's committed-memory limit only as it is touched, and the macOS footprint counts only touched pages, so neither needed a change.
