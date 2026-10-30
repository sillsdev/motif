# Delete an empty sense

Delete one wholly empty sense of an entry, together with the stem analysis that only that sense uses. A variant entry that keeps one empty sense blocks its main entry's path; this removes the sense so the variant takes it.

```sh
motif compose-delete-empty-sense --project language.fwdata --draft ceb-d001 \
  --intent '{"target":"<sense-id>","entry":"<owning-entry-id>"}'
```

Both fields are required and the intent is closed. The command refuses, and changes nothing, when:

- the sense has any content, such as a gloss, definition, example, note or sub-sense; there is no flag that overrides this, because deleting a sense with content loses a person's work;
- `entry` is not the entry that owns the sense;
- anything else refers to the sense.

An analysis containing only a part of speech is allowed only when the entry has a variant reference with a component target. The Dry Run records the removed part of speech. The sense's analysis is deleted with it only when no other sense of the entry uses it and nothing else, such as a parser analysis, refers to it. If another sense uses it, the analysis stays. If something else refers to it, the command refuses.

Run many at once with `compose-batch`, one entry per sense, so a whole clean-up becomes one Proposal. Each operation carries a rationale; the Draft still needs its own Rationale (`proposal rationale`) before Finalize.

Use this editor through the CLI or MCP. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
