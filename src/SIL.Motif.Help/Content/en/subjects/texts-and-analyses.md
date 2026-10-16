# Texts and analyses

A text is running language the project keeps in paragraphs, segments and words. An analysis is the decision that a word in a text means a particular combination of forms; approving or disapproving analyses is how a linguist teaches the project which readings are right.

## What the work needs

- Enter a text with its paragraphs and segments before deciding anything about its words, so each word has a place.
- Decide on one of a wordform's analyses; the decision holds wherever that wordform occurs.
- Read the word's current analyses first. A decision about a word with no recorded readings has nothing to apply to.

## Composers, in order

1. `compose-author-text` creates the text with its paragraphs and segments.
2. `compose-edit-analysis-decision` approves an analysis, disapproves it, returns it to undecided, marks the wordform misspelled, or adds a reading as a new analysis to decide on.

## Pitfalls

- Do not approve an analysis to make a measurement look better. Only a person approves or marks a word misspelled; an agent stages such a decision only when the linguist has made it.
- After deciding analyses, check the Draft with a Dry Run (`motif_dry_run`) and a bounded Trial (`motif_trial`) over the words involved to see what the decision changed.
