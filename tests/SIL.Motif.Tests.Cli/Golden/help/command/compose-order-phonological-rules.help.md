# Reorder sound rules

Use this command to change the order in which phonological rules apply. A later rule sees the output of the earlier ones, so order decides whether a chain such as a class-to-nasal rule and a nasal deletion works at all.

```sh
motif compose-order-phonological-rules --project language.fwdata --draft rule-order \
  --intent '{"rule":"<rule-id>","before":"<other-rule-id>"}'
```

Give either one move (`rule` with `before` or `after`) or the whole new order (`order`, every rule once). The command plans the fewest moves and refuses an unknown rule, an incomplete order, or an order that changes nothing. Rules keep their contexts, features and disabled state.

Dry Run and Trial the Proposal on a word that needs the chain, then Finalize it for a person to review and Apply.
