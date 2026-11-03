# Turn a sound rule off or on

Use this command to disable or enable a phonological rule. A disabled rule stays in the project, in its place, but the parser does not load it.

```sh
motif compose-set-phonological-rule-disabled --project language.fwdata --draft rule-off \
  --intent '{"target":"<rule-id>","expectedDisabled":false,"disabled":true}'
```

`expectedDisabled` must match the rule's current state; a stale value is refused. Use this to test a grammar without a rule, or to hold a rule back until its words are in.
