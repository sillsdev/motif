# Start a Proposal

Starts a named Draft holding one related set of semantic changes. Measure it and revise it before Finalize. Only a person can Apply.

Before Finalize, give the Draft both a short label and a Rationale. Use `--label` here or `motif label` to set the label, then [`proposal rationale`](cmd:proposal%20rationale) with the Draft's Proposal id to say what changes and why.

A name that is already taken is refused with `draft.name-collision`. When the existing Draft holds no changes, as after a compose into it failed, the refusal says so: compose into it again with `--draft <name>`, remove it with `discard-draft --project <fwdata> --draft <name>`, or use another name. `discard-empty-drafts --project <fwdata>` removes every such empty Draft at once.
