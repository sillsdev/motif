# Sena stem retirement evidence

Sena lists the same initial sound alternation on many roots, making it a useful example of a possible shared sound rule. These observations establish what is written in the project; they do not prove that a proposed rule preserves its readings.

## Source and method

Read a copy of `/mnt/h/repos/PanGloss/samples/data/sena.fwdata`, made before inspection at `/dev/shm/jm-stem/sena.fwdata`. SHA-256 of the copied bytes: `64e39b031c1a8dbc3a24e39869ab793a037d0f298d2dc1e2b0803dcfa9b15a42`. No sample data or writing-system repository was edited or added to Motif.

Resolve `rt` records by GUID. For each LexEntry, read its LexemeForm and ordered AlternateForms; compare same-WS MoStemAllomorph strings differing only by initial `l/r`. Resolve `PhPhoneme.Codes → PhCode.Representation` for `seh` and enumerate full grapheme segmentations. Keep only unique phoneme paths with one `l/r` substitution and matching root morph types, then group by the MoStemMsa category gate. This independently checks the fourteen-root family reported by M10; it is an XML inspection, not a rerun of PanGloss grammar-facts or a LibLCM acceptance test.

There are 40 written pairs sharing the PhoneEnv below. Fifteen pairs have unique `seh` phoneme paths; fourteen use root morph type `d7f713e5-e8cf-11d3-9764-00c04f186933` and the verbal category `86ff66f6-0774-407a-a0dc-3eeaf873daf7`. The other uniquely tokenized pair, `li/ri`, uses stem morph type `d7f713e8-e8cf-11d3-9764-00c04f186933` and category `cf4b95e5-2362-412d-92a1-24846a4bab59`. Ambiguous/unmapped pairs remain outside this fourteen-root group. For example, `lengo/rengo` also has mismatched root/stem types. None of these differences licenses automatic morph-type repair.

## Exact forms

In each row the `l` form is LexemeForm and the `r` form an AlternateForms member. Both are MoStemAllomorph, with no StemName; the `l` form has no PhoneEnv and the `r` form references the single shared PhoneEnv. None of the fourteen entries owns a LexEntryRef. `lukul` has a second MoStemMsa with no category; the future producer must enumerate actual bundle roles rather than assume each entry has exactly one MSA.

| Surviving → listed form (`seh`) | Entry GUID | Surviving primary GUID | Retiring alternate GUID |
| --- | --- | --- | --- |
| lekerer → rekerer | `03db04bc-1143-4170-ab2d-ef3176203966` | `964fc5f0-ced8-4677-8394-eb303c9347e6` | `9e21d8bc-0554-45c4-b1b1-30b83c2ec584` |
| luz → ruz | `04c59753-bc07-4b42-a0d6-471f9b4db0d7` | `7100dcf5-0d99-4c70-bbab-705c8128aac9` | `6b12f1e2-5332-4ca1-bd92-a90b6d3896ee` |
| lir → rir | `3679eb18-b521-49d0-9430-93a309567c54` | `c2ce7858-5e2d-4650-b4c4-cd80994a8d7f` | `519c7da8-2976-442d-b59e-7956121f4ee2` |
| lukul → rukul | `4507a379-ef26-4e14-9e20-064dfc89951d` | `4eeab4a5-e0ce-40ac-8113-09f48ef6c2db` | `7f2bf300-4670-4f4d-94f2-db6b268a3c45` |
| lokoter → rokoter | `46f0b1a4-d1a2-4e13-8424-20776002c03f` | `3669bf28-c4db-4425-bc51-c3693bd05023` | `90ba2e71-7ad8-450c-abd6-dc94b4e720a0` |
| lulup → rulup | `5d2d8b05-f018-4ad4-b0a2-273973933f2b` | `a38c0652-7690-48bf-945d-01504a09f880` | `dbe56637-05bc-4276-98f0-190ae779331c` |
| lul → rul | `5de74fc5-b4fe-481b-9411-16e5f8c295fa` | `42f66e32-8d9d-4fbf-98d2-af0dd6df9629` | `b5c42ed0-34ba-473b-86b1-8806c8642842` |
| lot → rot | `5e597800-64a1-4748-bd2e-4ee399aab0e8` | `0b11c8ed-977f-4e77-b729-a154e89a00b2` | `8f5de078-63b5-40b0-8ad2-cb71fa0ea421` |
| lek → rek | `8da08768-9b89-4d6a-ab4e-deb87d8e40fe` | `be78040c-8de9-4048-b620-46b5840dac6f` | `5b31dc69-8c89-459c-8772-25133b2c8aad` |
| liz → riz | `acd26daa-9774-4a34-997d-8d3d7660a54b` | `99170d1c-bc8f-43a0-827d-da2889166e70` | `56c941db-9e7a-44c6-9564-217c47d31f1e` |
| lipo → ripo | `b53184d2-c0c0-4edf-a0e4-74c88d9296b1` | `9fe1d052-6a28-47cf-bf51-a977a3d0d7a7` | `b34c8c10-9275-491a-9854-c2f306b33a10` |
| lip → rip | `deaf7897-9532-4ba3-afc2-3d25300db453` | `6df83c65-7e7b-4b50-8b9a-0018b70b8c0b` | `e8b79490-a6cd-4a60-9d92-d904f63fc8f7` |
| lokot → rokot | `f2983d64-34f8-473a-9575-3bfb1ef54d2e` | `a8cdedfa-cd46-4049-87b1-66a79ac4bb84` | `b8027dc7-0abe-4a74-aff0-a709f9062b51` |
| leser → reser | `f8f7c22b-7757-48a6-88d5-3f312f18e4de` | `2d27798d-4cce-41bc-9c27-5721d45ccc35` | `4d269319-d678-46d6-a613-8effe67a15e6` |

## Environment and phonemes

The shared PhEnvironment is `3be33482-38ee-4bee-a13b-7a0589942af3`. Concatenating its StringRepresentation runs yields `/ [V-front] _`; its Description says “controls l/r”. Its Name, “After a front vowel”, contradicts the resolved class and must not supply the rule's meaning.

The referenced PhNCSegments class `11b7ddde-8bc5-4e45-8fc3-6fc1087825ba` has abbreviation `V-front`, English Name “non-front vowels”, and these exact segments:

| Phoneme GUID | `seh` code |
| --- | --- |
| `14bd1795-d041-4c59-91ae-1d5506c63402` | `o` |
| `1fe59fab-5e53-42c3-8b23-d8ea6a2d531f` | `a` |
| `70286ab3-04b7-43ab-afb3-76e978e24142` | `u` |

Input `l` is `937234ce-012f-4143-9130-c80cce3d8ace`; output `r` is `82b13fe1-552a-4065-9f16-28c91f09a789`. The XML has no PhRegularRule, PhSegRule or PhMetathesisRule records. Root-edge phone conditioning is not itself proof that a segment rule at every internal `l` has the same meaning; boundary/stratum correspondence remains a recipe acceptance requirement.

## References and limits

An XML scan of every direct objsur to the 28 selected form GUIDs finds only fourteen LexEntry.LexemeForm ownership links and fourteen LexEntry.AlternateForms links. There are no direct WfiMorphBundle.Morph uses of these selected forms. This is an observation about the copied file, not the authoritative runtime census: custom fields, unprojected native references, semantic entry/sense expansion dependencies, normalized gate meaning and every incoming route still require the pinned LibLCM reader.

Use the exact `lekerer/rekerer` identities as the worked case in ADR 0057. For acceptance, freeze independently authored held-out cases and seed real Approved readings, unchanged readings on the same wordform, uses outside Selection, and ordinary compound/Text contexts. A zero affected-Approved count in this sample cannot stand in for those proofs. Awetí's `m ~ p` observation has unstated conditioning and receives no inferred rule from this Sena inspection.
