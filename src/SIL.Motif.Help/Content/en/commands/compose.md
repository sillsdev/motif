# Compose steps in a Draft

Stages one or more composing steps in a Draft in one call, through the same checks as each composing command alone, and can first remove earlier operations from it. If any step is refused, or a removal is, nothing is added or removed. Only a person can Apply.

Composing operations does not set the Draft's review text. Before Finalize, add both a short label with `motif label` and a comment with `motif comment`.

Each step names a composing command and carries the same closed intent that command's `--intent` takes. A step may add a `cite` object, which Motif records as operation provenance without changing the intent or its digest.

One step:

```sh
motif compose --project grammar.fwdata --proposal plurals \
  --steps '[{"verb": "compose-author-phoneme", "intent": {"name": "i", "representations": ["i"]}}]'
```

Many steps in order:

```sh
motif compose --project grammar.fwdata --proposal sounds --steps '[
  {"verb": "compose-author-phoneme", "intent": {"name": "i", "representations": ["i"]},
   "cite": {"source": "Grammar 2026", "locator": "§ 3"}},
  {"verb": "compose-author-phoneme", "intent": {"name": "u", "representations": ["u"]}}
]'
```

Steps run in order and each one sees the changes of the steps before it. A phonological-rule step may add an `as` label, and a later rule may use `@label` in `placement.after` or `placement.before`. Labels match `[A-Za-z][A-Za-z0-9_-]{0,20}`, are unique within the call, and are supported only for `compose-author-phonological-rule`. Other composers cannot refer to an entity created earlier in the same call; use the identities an earlier call reported.

`--remove` takes operation ids, separated by commas, to take out of the Draft before the steps run, which is how a step replaces something it staged earlier:

```sh
motif compose --project grammar.fwdata --proposal sounds --remove <operation id>,<operation id> \
  --steps '[{"verb": "compose-author-phoneme", "intent": {"name": "e", "representations": ["e"]}}]'
```

A removal that would leave another operation depending on it is refused; remove those operations too, or use `remove-operations` with `--force`. A refusal names the failing step's position and command.

The result lists each step's operations and the Draft's operation count afterwards. Add `--json` for the structured result.

This command is available when Advanced AI mode is on. The MCP tool is `motif_compose`, with `proposal`, `steps` and `remove` arguments, and is off unless a profile selects it. `compose` names its Draft with `--proposal`, because that argument names a Draft or a Proposal; `compose-batch` takes the same steps from a file and does not remove.
