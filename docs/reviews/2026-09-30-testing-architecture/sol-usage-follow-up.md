# Explicit action usage follow-up

Usage should count each action a person initiates once, even when it is refused or cancelled. Automatic refreshes and the internal work one action triggers should not add entries.

## Accepted corrections

Independent Sol review of `d03c49b3` against `ccf3fab1` accepted explicit word-set and rule-selection scopes. The rerun scope begins before user-facing validation and remains alive across all selected words and cancellation, suppressing nested Assess entries. Argument records contain shapes and list sizes rather than values. Shared automatic evidence and focused reads remain unrecorded. No new privacy or scope-lifecycle findings were reported.

## Remaining P2 finding

Four explicit selectors still bypass the scoped handlers: picked words, text lists, checked words and a matrix cell. They reach `LoadScopeAsync` through shared helpers and record zero entries on success or refusal. Add scopes at their explicit command wrappers while keeping the shared helpers unrecorded. The finding has been assigned to the owning worker; usage integration remains pending.

## Verification limits

The reviewer inspected source and behavioral tests without running builds or tests. Fresh wrapper counts, correction, independent re-review and combined real-parser verification are still required before this stage is complete.
