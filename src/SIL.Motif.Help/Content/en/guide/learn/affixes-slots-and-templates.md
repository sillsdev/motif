# Affixes, slots, and templates

An affix is a meaningful piece attached to a stem. A **prefix** comes before it, a **suffix** after it, and an **infix** inside it. As illustrative English examples, *un-happy* shows a prefix and *kind-ness* a suffix. The Turkish form *evler* (“houses”) illustrates a suffix attached to *ev* (“house”).

Languages often restrict which affixes can combine and in what order. A **slot** is a position in that sequence. For example, an illustrative verb pattern might allow a person marker before a tense marker. Putting the same affixes in the reverse order could produce a form the language does not use.

In FieldWorks, affix templates organize slots for a part of speech. A noun template can describe noun patterns, while a verb template can describe verb patterns. Some slots are obligatory: a valid word using that template must fill them. Others are optional and may be skipped. These settings help the parser avoid treating every affix as freely combinable with every word.

When a trace reaches the right stem but stops before the expected ending, check the relevant template. Is the affix in the right slot? Is the order right? Is the slot required or optional as intended? Does the template apply to this part of speech? Keep one known word that should use the pattern and one similar word that should not; both help you check whether the template says what you mean.

The examples here are illustrative and simplified. Real languages can have several templates, and a slot can allow more than one affix.
