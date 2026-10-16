# Texts and analyses

A text is running language the project keeps in paragraphs, segments and words. An analysis is the decision that a word in a text means a particular combination of forms; approving or disapproving analyses is how a linguist teaches the project which readings are right.

## What the work needs

- Enter a text with its paragraphs and segments before deciding anything about its words, so each word has a place.
- Decide an analysis by naming the word occurrence and the analysis, and say approve, disapprove or return it to undecided.
- Read the word's current analyses first. A decision about a word with no recorded readings has nothing to apply to.

## Composers, in order

1. `compose-author-text` creates the text with its paragraphs and segments.
2. `compose-edit-analysis-decision` approves, disapproves or clears the decision on one analysis.

## Pitfalls

- A decision is about one occurrence of a word. Check that the same word elsewhere is not meant to change too.
- Do not approve an analysis to make a measurement look better. Approve it only when the linguist or the evidence supports it.
- After deciding analyses, run a Trial over the words involved to see what the decision changed.
