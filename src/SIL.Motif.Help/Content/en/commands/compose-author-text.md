# Author a text

```sh
motif compose-author-text --project language.fwdata --draft <name> \
  --intent '{"title":"Story","ws":"ceb","analysisWs":"en","paragraphs":[{"segments":[{"text":"(nakapaluwas) ug?","freeTranslation":"Did someone rescue?","literalTranslation":"rescued and?","notes":["Check the question in context."]}]}]}'
```

The closed intent gives the Text's title, vernacular writing system `ws`, and paragraphs. Each paragraph lists baseline pieces in `segments`, joined by single spaces. A piece is an object with `text` and optional `freeTranslation`, `literalTranslation` and `notes` (an ordered array of strings). All text uses NFD. An unknown or repeated property is refused.

FieldWorks' own LibLCM paragraph parser determines sentence segments, words and punctuation using the writing system's word-forming characters and existing phrase wordforms. Punctuation outside a phrase stays in the paragraph and becomes Punctuation Forms. A punctuation-only paragraph is allowed. Motif adds no paragraph or word-count limit.

Free Translation, Literal Translation and Notes are stored on the Segment, in `analysisWs`; omit that tag to use the project's default analysis writing system. An annotated piece must be exactly one FieldWorks segment. End each sentence with its punctuation or put it in a separate paragraph. A piece spanning sentences or ending inside a segment is refused when it carries annotations, so annotations cannot silently move to a different sentence.

The earlier segment shape, an array such as `["(nakapaluwas)","ug,","?"]`, is also accepted. Its tokens are joined with spaces and parsed in the same way; array boundaries alone do not force a sentence boundary.

A word whose exact form already exists in that writing system reuses its wordform. A missing form creates one new wordform in the same Draft, with an explicit dependency from the Text. Different spelling or case remains a different wordform.

The result lists created wordforms and the Text by identity. Inspect the Draft's Dry Run, revise it, then Finalize for review. Only a person can Apply the Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
