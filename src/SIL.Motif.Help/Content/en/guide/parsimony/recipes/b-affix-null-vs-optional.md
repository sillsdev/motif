# A zero affix may add no value

Remove a zero affix only when it adds no grammatical distinction and no Approved reading depends on its identity. Keep meaningful zero morphology and explain what it contributes.

## Analyse

Read `null-optional`, the authored form and its compiler record, feature and class contributions, slot optionality, senses, and incoming references. Count a null marker only when PanGloss facts show a final ordinary morphology output for its MSA; an import record alone is not proof. An empty string or a dropped or compacted marker alone does not establish a loaded zero.

## Disposition

If the zero contributes a feature, class restriction, or Approved reading, record a `keep` disposition with a reason. Ask about an unclear grammatical distinction or default interpretation. Defer when the compiler did not record the authored marker as represented.

Do not stage a zero-retirement change for a meaningful or referenced zero.

## Ask the linguist

Ask whether the zero contributes a feature or distinct reading, or only permits an already optional slot to be empty. Show its feature chart and incoming reference count.

## Update

For an unreferenced, featureless zero in an optional slot, use `RetireRedundantZeroAffix`. The operation accepts one ordinary primary prefix or suffix with no alternate forms. It removes the whole entry graph, including its senses, MSA, allomorphs, and last primary form, as one operation; deleting every owned object together makes removal of that final form safe. The optional slot and any template that uses it remain.

## Verify

Run Dry Run, then compare parser results from the current and edited scratch copies. Every existing Approved reading and each absence word must keep the same result, and reviewed negative examples must still have no parser reading. Confirm that the optional slot remains and grammar health has no errors. The count for this measure must fall because the authored graph was removed, not because the loader dropped its marker.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Technical documents index](https://software.sil.org/fieldworks/help/technical-documents/). Distinguishes optional slots from null affixes, including cases where a zero expresses a feature.
- **[becker-2024]** — Laura Becker. 2024. “Zero Marking in Inflection: A Token-Based Approach.” *Journal of Language Modelling* 12(2):349–413. DOI: 10.15398/jlm.v12i2.361. [DOI](https://doi.org/10.15398/jlm.v12i2.361). Treats zero marking as an analytical issue in inflection.
- **[alekseeva-myachykov-shtyrov-2022]** — Maria Alekseeva, Andriy Myachykov, and Yury Shtyrov. 2022. “Inflectional zero morphology – Linguistic myth or neurocognitive reality?” *Frontiers in Psychology* 13:1015435. DOI: 10.3389/fpsyg.2022.1015435. [DOI](https://doi.org/10.3389/fpsyg.2022.1015435). Reviews the debate about zero morphology and supports caution about blanket deletion.

The zero-realization classifier and cleanup eligibility are engineering checks; verify feature effects, references, and analyses for each affix it lists.
