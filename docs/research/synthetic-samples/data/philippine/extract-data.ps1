$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))
$rawRoot = Join-Path $repoRoot 'bin\data-raw'
$unimorphCommit = '799d79de338d1370ebfbeabb9823a0e8f86a2b6a'
$unimorphUrl = "https://github.com/UniMorph/tgl/blob/$unimorphCommit/tgl"
$verbRulesUrl = 'https://en.wiktionary.org/w/index.php?title=Appendix:Tagalog_verbs&oldid=82411472'
$affixListUrl = 'https://en.wiktionary.org/w/index.php?title=Appendix:Tagalog_affixes&oldid=83365377'
$utf8 = [Text.UTF8Encoding]::new($false)

$lexicon = @(
    [pscustomobject]@{ Lemma = 'kain'; Gloss = 'eat'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=kumain&oldid=92941418'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'bili'; Gloss = 'buy'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=bumili&oldid=89057326'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'pasok'; Gloss = 'enter'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=pumasok&oldid=84205414'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'gamit'; Gloss = 'use'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=gumamit&oldid=89057915'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'dalaw'; Gloss = 'visit'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=dumalaw&oldid=89057658'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'kuha'; Gloss = 'get; take'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=kumuha&oldid=89057621'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'tawag'; Gloss = 'call'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=tumawag&oldid=89058043'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'takbo'; Gloss = 'run'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=tumakbo&oldid=79269021'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'hanap'; Gloss = 'look for'; GlossSource = 'https://tatoeba.org/en/sentences/show/5094646 (English pair 1346400)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'kanta'; Gloss = 'sing'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=kumanta&oldid=92941184'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'tawid'; Gloss = 'cross'; GlossSource = 'https://tatoeba.org/en/sentences/show/11341899 (English pair 1078027)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'tanggap'; Gloss = 'receive; accept'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=tumanggap&oldid=89057821'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'tira'; Gloss = 'live; dwell'; GlossSource = 'https://tatoeba.org/en/sentences/show/11291252 (English pair 7736830)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'sagot'; Gloss = 'reply; respond'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=sumagot&oldid=89057543'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'sigaw'; Gloss = 'scream; yell'; GlossSource = 'https://tatoeba.org/en/sentences/show/4560367 (English pair 3374155)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'tugtog'; Gloss = 'play (an instrument)'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=tumugtog&oldid=79269066'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'sali'; Gloss = 'join; take part'; GlossSource = 'https://tatoeba.org/en/sentences/show/2281333 (English pair 248079)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'sayaw'; Gloss = 'dance'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=sumayaw&oldid=84441876'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'bangon'; Gloss = 'get up'; GlossSource = 'https://tatoeba.org/en/sentences/show/8635261 (English pair 896158)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'talon'; Gloss = 'jump'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=tumalon&oldid=89057729'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'kagat'; Gloss = 'bite'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=kumagat&oldid=89057515'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'huli'; Gloss = 'arrest'; GlossSource = 'https://tatoeba.org/en/sentences/show/11588590 (English pair 8370021)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'gulong'; Gloss = 'roll'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=gumulong&oldid=79257298'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'hiram'; Gloss = 'borrow'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=humiram&oldid=89057415'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'tahimik'; Gloss = 'become quiet'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=tumahimik&oldid=79269020'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'silip'; Gloss = 'peek into'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=sumilip&oldid=89057932'; GlossLicense = 'CC BY-SA 4.0' }
    [pscustomobject]@{ Lemma = 'dampot'; Gloss = 'pick up'; GlossSource = 'https://tatoeba.org/en/sentences/show/11782805 (English pair 3422357)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'bilang'; Gloss = 'count'; GlossSource = 'https://tatoeba.org/en/sentences/show/2812197 (English pair 496687)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'subok'; Gloss = 'try'; GlossSource = 'https://tatoeba.org/en/sentences/show/2861060 (English pair 1126775)'; GlossLicense = 'CC BY 2.0 FR' }
    [pscustomobject]@{ Lemma = 'sulong'; Gloss = 'advance; go ahead'; GlossSource = 'https://en.wiktionary.org/w/index.php?title=sumulong&oldid=91591648'; GlossLicense = 'CC BY-SA 4.0' }
)

$sentencePairs = @(
    [pscustomobject]@{ Tgl = '8635261'; Eng = '896158' }
    [pscustomobject]@{ Tgl = '8664610'; Eng = '259164' }
    [pscustomobject]@{ Tgl = '2812197'; Eng = '496687' }
    [pscustomobject]@{ Tgl = '11538026'; Eng = '6691360' }
    [pscustomobject]@{ Tgl = '8658555'; Eng = '3859152' }
    [pscustomobject]@{ Tgl = '11473949'; Eng = '2873499' }
    [pscustomobject]@{ Tgl = '11680604'; Eng = '1744811' }
    [pscustomobject]@{ Tgl = '11739483'; Eng = '10075763' }
    [pscustomobject]@{ Tgl = '11782805'; Eng = '3422357' }
    [pscustomobject]@{ Tgl = '11797421'; Eng = '2642618' }
    [pscustomobject]@{ Tgl = '8660569'; Eng = '1356774' }
    [pscustomobject]@{ Tgl = '11468661'; Eng = '3926042' }
    [pscustomobject]@{ Tgl = '11460488'; Eng = '3091016' }
    [pscustomobject]@{ Tgl = '11806315'; Eng = '2633357' }
    [pscustomobject]@{ Tgl = '5094646'; Eng = '1346400' }
    [pscustomobject]@{ Tgl = '12710016'; Eng = '1887320' }
    [pscustomobject]@{ Tgl = '11571425'; Eng = '11569212' }
    [pscustomobject]@{ Tgl = '1122439'; Eng = '749349' }
    [pscustomobject]@{ Tgl = '11588590'; Eng = '8370021' }
    [pscustomobject]@{ Tgl = '13648670'; Eng = '9099803' }
    [pscustomobject]@{ Tgl = '11291275'; Eng = '256554' }
    [pscustomobject]@{ Tgl = '11268396'; Eng = '1120797' }
    [pscustomobject]@{ Tgl = '11268403'; Eng = '1098492' }
    [pscustomobject]@{ Tgl = '5214685'; Eng = '2203598' }
    [pscustomobject]@{ Tgl = '11782791'; Eng = '11782785' }
    [pscustomobject]@{ Tgl = '11268435'; Eng = '237922' }
    [pscustomobject]@{ Tgl = '8658521'; Eng = '2254887' }
    [pscustomobject]@{ Tgl = '12354222'; Eng = '11915707' }
    [pscustomobject]@{ Tgl = '4560437'; Eng = '2111930' }
    [pscustomobject]@{ Tgl = '2337149'; Eng = '2337152' }
    [pscustomobject]@{ Tgl = '6135400'; Eng = '2629414' }
    [pscustomobject]@{ Tgl = '5214548'; Eng = '2111600' }
    [pscustomobject]@{ Tgl = '2281333'; Eng = '248079' }
    [pscustomobject]@{ Tgl = '11807867'; Eng = '11787663' }
    [pscustomobject]@{ Tgl = '8634907'; Eng = '5828626' }
    [pscustomobject]@{ Tgl = '11268448'; Eng = '28677' }
    [pscustomobject]@{ Tgl = '4560367'; Eng = '3374155' }
    [pscustomobject]@{ Tgl = '4560527'; Eng = '2203900' }
    [pscustomobject]@{ Tgl = '11743261'; Eng = '6272676' }
    [pscustomobject]@{ Tgl = '11474215'; Eng = '10862698' }
    [pscustomobject]@{ Tgl = '5214292'; Eng = '20119' }
    [pscustomobject]@{ Tgl = '2861060'; Eng = '1126775' }
    [pscustomobject]@{ Tgl = '11574827'; Eng = '237721' }
    [pscustomobject]@{ Tgl = '1814053'; Eng = '1462701' }
    [pscustomobject]@{ Tgl = '1599615'; Eng = '672229' }
    [pscustomobject]@{ Tgl = '1648935'; Eng = '672264' }
    [pscustomobject]@{ Tgl = '4554885'; Eng = '672252' }
    [pscustomobject]@{ Tgl = '8634909'; Eng = '5828631' }
    [pscustomobject]@{ Tgl = '4560280'; Eng = '4356531' }
    [pscustomobject]@{ Tgl = '13874206'; Eng = '12227353' }
    [pscustomobject]@{ Tgl = '11380651'; Eng = '10708633' }
    [pscustomobject]@{ Tgl = '4554761'; Eng = '64527' }
    [pscustomobject]@{ Tgl = '5214476'; Eng = '2111379' }
    [pscustomobject]@{ Tgl = '5214544'; Eng = '2111599' }
    [pscustomobject]@{ Tgl = '11341899'; Eng = '1078027' }
    [pscustomobject]@{ Tgl = '11818538'; Eng = '5591215' }
    [pscustomobject]@{ Tgl = '11291252'; Eng = '7736830' }
    [pscustomobject]@{ Tgl = '1853511'; Eng = '724062' }
    [pscustomobject]@{ Tgl = '1122477'; Eng = '866345' }
    [pscustomobject]@{ Tgl = '8638165'; Eng = '8613623' }
)

function Write-Tsv {
    param([string] $Path, [string[]] $Headers, [object[]] $Rows)
    $writer = [IO.StreamWriter]::new($Path, $false, $utf8)
    try {
        $writer.WriteLine(($Headers -join "`t"))
        foreach ($row in $Rows) {
            $values = foreach ($header in $Headers) {
                $value = [string] $row.$header
                $value.Replace("`t", ' ').Replace("`r", ' ').Replace("`n", ' ')
            }
            $writer.WriteLine(($values -join "`t"))
        }
    }
    finally { $writer.Dispose() }
}

$wantedFeatures = @('V;NFIN', 'V;PFV;AGFOC', 'V;IPFV;AGFOC', 'V;PFV;PFOC', 'V;IPFV;PFOC')
$lemmaSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $lexicon) { [void] $lemmaSet.Add($entry.Lemma) }
$formsByLemma = @{}
foreach ($line in [IO.File]::ReadLines((Join-Path $rawRoot 'unimorph-tgl.tsv'))) {
    $fields = $line.Split("`t")
    if ($fields.Length -ne 3 -or -not $lemmaSet.Contains($fields[0]) -or $wantedFeatures -notcontains $fields[2]) { continue }
    if (-not $formsByLemma.ContainsKey($fields[0])) { $formsByLemma[$fields[0]] = @{} }
    if (-not $formsByLemma[$fields[0]].ContainsKey($fields[2])) {
        $formsByLemma[$fields[0]][$fields[2]] = [Collections.Generic.List[string]]::new()
    }
    if (-not $formsByLemma[$fields[0]][$fields[2]].Contains($fields[1])) {
        $formsByLemma[$fields[0]][$fields[2]].Add($fields[1])
    }
}

$paradigms = [Collections.Generic.List[object]]::new()
foreach ($entry in $lexicon) {
    $lemma = $entry.Lemma
    if ($lemma -notmatch '^[bcdfghjklmnpqrstvwxyz][aeiou]') { throw "Out-of-scope stem shape: $lemma" }
    if (-not $formsByLemma.ContainsKey($lemma)) { throw "Missing UniMorph lemma: $lemma" }
    $c = $lemma.Substring(0, 1)
    $v = $lemma.Substring(1, 1)
    $tail = $lemma.Substring(1)
    $expected = [ordered]@{
        'V;NFIN' = @{ Form = $lemma; Segmentation = '' }
        'V;PFV;AGFOC' = @{ Form = $c + 'um' + $tail; Segmentation = $c + '-um-' + $tail }
        'V;IPFV;AGFOC' = @{ Form = $c + 'um' + $v + $lemma; Segmentation = $c + '-um-' + $v + '-' + $lemma }
        'V;PFV;PFOC' = @{ Form = $c + 'in' + $tail; Segmentation = $c + '-in-' + $tail }
        'V;IPFV;PFOC' = @{ Form = $c + 'in' + $v + $lemma; Segmentation = $c + '-in-' + $v + '-' + $lemma }
    }
    foreach ($features in $wantedFeatures) {
        if (-not $formsByLemma[$lemma].ContainsKey($features)) { throw "Missing UniMorph feature row: $lemma / $features" }
        $expectedForm = $expected[$features].Form
        if (-not $formsByLemma[$lemma][$features].Contains($expectedForm)) {
            throw "Expected regular UniMorph form missing for ${lemma} / ${features}: $expectedForm"
        }
        $observed = $expectedForm
        $glossCitation = "$($entry.GlossSource) ($($entry.GlossLicense))"
        $source = "$unimorphUrl ($features; CC BY-SA 3.0); segmentation: $verbRulesUrl (CC BY-SA 4.0); gloss: $glossCitation"
        $paradigms.Add([pscustomobject]@{
            lemma = $lemma
            gloss = $entry.Gloss
            pos = 'V'
            inflected_form = $observed
            features = $features
            segmentation = $expected[$features].Segmentation
            source = $source
        })
    }
}

$morphemes = [Collections.Generic.List[object]]::new()
$ruleSource = "$verbRulesUrl (CC BY-SA 4.0); $affixListUrl (CC BY-SA 4.0)"
$morphemes.Add([pscustomobject]@{
    morpheme_id = 'um_infix'
    form = '-um-'
    allomorphs = 'um- listed as a prefix spelling for vowel-initial roots (analysis disputed); historical -im- before first /i/ (obsolete)'
    gloss = 'actor-focus verb-class marker; meaning depends on the lexical stem and construction'
    category = 'infix'
    slot_or_position = 'after the first consonant, before the first vowel'
    conditioning_environment = 'this dataset: unprefixed stems beginning C1V1; actor-focus class only'
    source = "$ruleSource; https://en.wiktionary.org/w/index.php?title=-um-&oldid=92941181 (CC BY-SA 4.0); https://doi.org/10.1515/9783110755466 (CC BY-NC-ND 4.0; context only)"
})
$morphemes.Add([pscustomobject]@{
    morpheme_id = 'in_infix'
    form = '-in-'
    allomorphs = 'in- with vowel-initial orthography; ni- may occur before l, r, y, sometimes w, or with loanword clusters; outside this dataset'
    gloss = 'patient/object-focus perfective and progressive marker in this regular class'
    category = 'infix'
    slot_or_position = 'after the first consonant, before the first vowel'
    conditioning_environment = 'this dataset: unprefixed stems beginning C1V1; object/patient-focus class only'
    source = "$ruleSource; https://en.wiktionary.org/w/index.php?title=-in-&oldid=92941425 (CC BY-SA 4.0)"
})
$morphemes.Add([pscustomobject]@{
    morpheme_id = 'aspect_cv_reduplication'
    form = 'C₁V₁-'
    allomorphs = 'none represented in the selected C1V1 stems'
    gloss = 'partial reduplication marking progressive/imperfective aspect in the selected classes'
    category = 'reduplication'
    slot_or_position = 'copies the stem-initial CV; the infix interrupts its surface realization after C₁'
    conditioning_environment = 'progressive rows only; occurs with -um- actor-focus and -in- object-focus patterns'
    source = "$verbRulesUrl (CC BY-SA 4.0); https://en.wiktionary.org/w/index.php?title=-in-&oldid=92941425 (CC BY-SA 4.0)"
})
foreach ($entry in $lexicon) {
    $source = "$unimorphUrl (V;NFIN; CC BY-SA 3.0); gloss: $($entry.GlossSource) ($($entry.GlossLicense))"
    $morphemes.Add([pscustomobject]@{
        morpheme_id = "root_$($entry.Lemma)"
        form = $entry.Lemma
        allomorphs = 'none recorded in the selected paradigm'
        gloss = $entry.Gloss
        category = 'stem'
        slot_or_position = 'base'
        conditioning_environment = 'unprefixed C1V1 stem in the selected regular inflection class'
        source = $source
    })
}

$pairSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$tagalogIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$englishIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($pair in $sentencePairs) {
    $pairKey = "$($pair.Tgl):$($pair.Eng)"
    if (-not $pairSet.Add($pairKey)) { throw "Duplicate selected Tatoeba pair: $pairKey" }
    [void] $tagalogIds.Add($pair.Tgl)
    [void] $englishIds.Add($pair.Eng)
}
$linkedPairs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($line in [IO.File]::ReadLines((Join-Path $rawRoot 'tatoeba-tgl-eng_links.tsv'))) {
    $fields = $line.Split("`t")
    if ($fields.Length -eq 2 -and $pairSet.Contains("$($fields[0]):$($fields[1])")) { [void] $linkedPairs.Add("$($fields[0]):$($fields[1])") }
}
if ($linkedPairs.Count -ne $sentencePairs.Count) { throw 'A selected sentence pair is missing from the Tatoeba link export.' }

$tagalogById = @{}
foreach ($line in [IO.File]::ReadLines((Join-Path $rawRoot 'tatoeba-tgl_sentences_detailed.tsv'))) {
    $fields = $line.Split("`t")
    if ($fields.Length -ge 4 -and $tagalogIds.Contains($fields[0])) {
        $tagalogById[$fields[0]] = [pscustomobject]@{ Text = $fields[2]; User = $fields[3] }
    }
}
$englishById = @{}
foreach ($line in [IO.File]::ReadLines((Join-Path $rawRoot 'tatoeba-eng_sentences_detailed.tsv'))) {
    $fields = $line.Split("`t")
    if ($fields.Length -ge 4 -and $englishIds.Contains($fields[0])) {
        $englishById[$fields[0]] = [pscustomobject]@{ Text = $fields[2]; User = $fields[3] }
    }
}

$formFeatures = @{}
foreach ($row in $paradigms) {
    if ($row.features -eq 'V;NFIN') { continue }
    $key = $row.inflected_form.ToLowerInvariant()
    if (-not $formFeatures.ContainsKey($key)) { $formFeatures[$key] = [Collections.Generic.List[string]]::new() }
    $formFeatures[$key].Add($row.features)
}
$sentences = [Collections.Generic.List[object]]::new()
foreach ($pair in $sentencePairs) {
    if (-not $tagalogById.ContainsKey($pair.Tgl) -or -not $englishById.ContainsKey($pair.Eng)) {
        throw "Missing sentence text for Tatoeba pair $($pair.Tgl):$($pair.Eng)."
    }
    $tl = $tagalogById[$pair.Tgl]
    $en = $englishById[$pair.Eng]
    $featuresFound = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($tl.Text.ToLowerInvariant(), '\p{L}+')) {
        if ($formFeatures.ContainsKey($match.Value)) {
            foreach ($features in $formFeatures[$match.Value]) { $featuresFound.Add("$($match.Value):$features") }
        }
    }
    if ($featuresFound.Count -eq 0) { throw "Sentence $($pair.Tgl) has no selected paradigm form." }
    if (@([regex]::Matches($tl.Text, '\p{L}+')).Count -gt 12 -or @([regex]::Matches($en.Text, '\p{L}+')).Count -gt 12) {
        throw "Selected sentence pair exceeds the short-sentence limit: $($pair.Tgl):$($pair.Eng)."
    }
    $sentences.Add([pscustomobject]@{
        id = "tatoeba-$($pair.Tgl)-$($pair.Eng)"
        sentence = $tl.Text
        english = $en.Text
        gloss_or_features = 'UniMorph surface-form lookup only (not sentence annotation): ' + ($featuresFound -join ' | ')
        source = "Tatoeba https://tatoeba.org/en/sentences/show/$($pair.Tgl) by $($tl.User); en:https://tatoeba.org/en/sentences/show/$($pair.Eng) by $($en.User)"
        licence = 'CC BY 2.0 FR'
    })
}

Write-Tsv -Path (Join-Path $PSScriptRoot 'morphemes.tsv') -Headers @('morpheme_id', 'form', 'allomorphs', 'gloss', 'category', 'slot_or_position', 'conditioning_environment', 'source') -Rows $morphemes
Write-Tsv -Path (Join-Path $PSScriptRoot 'paradigms.tsv') -Headers @('lemma', 'gloss', 'pos', 'inflected_form', 'features', 'segmentation', 'source') -Rows $paradigms
Write-Tsv -Path (Join-Path $PSScriptRoot 'sentences.tsv') -Headers @('id', 'sentence', 'english', 'gloss_or_features', 'source', 'licence') -Rows $sentences
Write-Output "Wrote $($morphemes.Count) morpheme rows, $($paradigms.Count) paradigm rows, and $($sentences.Count) sentence pairs."
