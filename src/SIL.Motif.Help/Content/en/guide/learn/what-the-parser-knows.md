# What the parser knows

A parser is literal. It sees the word you give it and searches using the lexicon, grammar, character information, and limits in the current [Baseline](term:baseline). It does not know what you meant, guess a missing entry from the conversation, or silently repair a rule.

A **parse** is an analysis the grammar can build for a word. As an illustrative English example, *cats* can be analysed as the stem *cat* plus the plural ending *-s*. The parser must find a matching stem and a permitted way to combine it with the ending. A word can have more than one possible analysis, and a completed search may find none.

That makes “it doesn’t parse” useful information. It says that, for this word and this saved grammar, the parser did not build an analysis. The result can narrow your next question: Is the stem present? Is the ending allowed after this kind of stem? Does its form fit the sound environment? Is a sound rule changing the form?

First check whether the search completed. A completed search with **No parse** tells you the grammar did not produce a reading. **Stopped: taking too long** means the parser hit a time or step limit before finishing, so it cannot show that the grammar has no possible analysis. A word with a character missing from the grammar’s character table is a different case again.

The parser reports what its grammar allowed. You decide whether that grammar matches the language.
