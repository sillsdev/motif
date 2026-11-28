# Why a grammar is slow

A grammar can find the right analysis and still make the parser do too much work. Optional slots, broad environments, duplicated affixes and empty affixes can each give it more paths to explore, but none of them is always costly: measure before you change anything.

An optional slot lets the parser consider a form both with and without that slot. Broad environments let a rule apply in many places, creating more possible continuations. An empty affix adds no visible letters, so the parser can add it without consuming part of the word, though PanGloss often handles these cheaply: in the synthetic Bantu-style sample, 160 empty allomorphs added no measurable work. A duplicated real affix is different; there, copying one object marker into a second slot multiplied the work.

Use **Diagnostics → Speed by Rule** after [Parse all words](term:parse-all-words) to locate recorded parser work. Each rule's steps are divided by the exact measured-word count shown above the table. The unattributed remainder stays visible. Partial per-word detail is labelled; missing counts stay unavailable. A rule's work shows where steps were spent, not what caused a word to parse or stop. Select a rule to open its verified FieldWorks destination.

Check that each optional slot is truly optional, each environment names the sounds it should, and each affix appears only where intended. Read and edit rules in FieldWorks. Change one thing at a time and measure again. The [Timing command](cmd:timing) offers recorded work outside the window as well. No Fast or Slow threshold is inferred from a rule's count.
