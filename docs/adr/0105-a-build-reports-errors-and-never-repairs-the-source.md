# 0105. A Build reports errors and never repairs the source

**In plain terms:** Like a compiler, a Build says what is wrong and where. It never quietly changes the grammar to
make the error go away.

**Status:** proposed, 2026-11-10. Depends on [ADR 0101](0101-a-project-source-is-fwdata-or-a-harmony-log-everything-else-is-built.md).

## Decision

1. **Validation lives in the Build and in Apply, not in the log.** This is verified: Harmony has no hook to reject a
   commit, the server validates nothing, and a remote commit that throws during replay fails the sync. So the
   log accepts any well-formed change, and the Build is where grammar must make sense.
2. **Build errors** name an object (GUID and Project path) and a reason. Examples:
   - a dangling reference left by `RemoveReference`;
   - a rule not in its stratum's order, a duplicate in an order, or a missing member;
   - an environment that does not parse;
   - a feature value that is not in its feature's closed set;
   - an allomorph with no form;
   - a duplicate that LibLCM forbids.
3. **Build warnings** are Motif's existing Warnings.
4. **The last good build output is kept.** A failed Build leaves the last successful PanGloss snapshot in place, so
   the Paratext plugins keep parsing; the failure is shown, not hidden.
5. **Apply requires a clean Build** of head plus the Proposal. A sync that brings in someone else's change can still
   break the Build; that shows up as a build error owned by nobody until someone fixes it with a Proposal.

## Consequences

- Build errors are a new condition next to Conflict. They are derived, loud, and not a Proposal state.
- "Coverage" and "unit tests" (Trial, Measure, and approved interlinearizer glosses as targets) run on a clean Build.
  Together with Build errors they are the CI of a project.
