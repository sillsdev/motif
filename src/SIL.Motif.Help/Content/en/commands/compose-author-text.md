# Author a text

```sh
motif compose-author-text --project language.fwdata --draft <name> \
  --intent '{"title":"Hadithi","ws":"swh","paragraphs":[{"segments":[["sita","mutu"],["sita"]]}]}'
```

The closed intent gives the Text's title, one writing system tag, and paragraphs. Each paragraph lists segments, and each segment lists its word tokens in reading order. A token is one exact NFD form without spaces. An unknown or repeated property is refused.

A token whose exact form already is a wordform in that writing system reuses it. Any other token creates one new wordform in the same Draft, and the Text declares that it depends on that creation. Nothing is matched by similarity, so a different spelling or case is a different wordform.

The result lists every created wordform and the Text by identity. Finish the Draft, inspect its Dry Run, and review it. Only a person can Apply the Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
