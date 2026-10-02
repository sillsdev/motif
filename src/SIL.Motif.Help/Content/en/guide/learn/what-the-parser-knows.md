# What the parser knows

A parser is literal. It sees the word you give it and searches using the lexicon, grammar, character information, and limits in the current [Baseline](term:baseline). It does not know what you meant or guess a missing entry from the conversation.

A **parse** is an analysis the grammar can build for a word. As an illustrative English example, *cats* can be analysed as the stem *cat* plus the plural ending *-s*. The parser must find a matching stem and a permitted way to combine it with the ending. A word can have more than one possible analysis, and a completed search may find none.

“It doesn’t parse” describes the result for this word and saved grammar. To judge whether that grammar matches the language, compare the result with the forms and patterns speakers use.

First check whether the search completed. A completed search with **No parse** tells you the grammar did not produce a reading. **Search incomplete** means the parser stopped before finishing, so it cannot show that the grammar has no possible analysis. The recorded reason can name a step limit, a time limit or another cause; a missing reason stays unavailable. A word with a character missing from the grammar’s character table is a different case again.

The parser reports what its grammar allowed. You decide whether that grammar matches the language.
