param(
    [string]$RawDir = "bin/data-raw"
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$rawRoot = (Resolve-Path (Join-Path $repoRoot $RawDir)).Path
$outputRoot = $PSScriptRoot
$unimorphPath = Join-Path $rawRoot 'unimorph-swc.tsv'
$tatoebaSwahiliPath = Join-Path $rawRoot 'tatoeba-swh-sentences-detailed.tsv'
$tatoebaEnglishPath = Join-Path $rawRoot 'tatoeba-eng-sentences-detailed.tsv'
$tatoebaLinksPath = Join-Path $rawRoot 'tatoeba-swh-eng-links.tsv'

$verbs = [ordered]@{
    soma = 'read; study'
    piga = 'hit; strike; play (an instrument)'
    penda = 'like; love'
    ona = 'see'
    nunua = 'buy'
    lala = 'sleep'
    leta = 'bring'
    jibu = 'answer; respond'
    andika = 'write'
    lipa = 'pay'
}
$roots = @{
    soma = 'som'
    piga = 'pig'
    penda = 'pend'
    ona = 'on'
    lala = 'lal'
    leta = 'let'
    andika = 'andik'
    lipa = 'lip'
}
$subjects = @{
    '1;SG' = 'ni'
    '1;PL' = 'tu'
    '2;SG' = 'u'
    '2;PL' = 'm'
}
$tensePrefixes = @{
    PRES = 'na'
    PST = 'li'
    FUT = 'ta'
    'PST;PRF' = 'me'
}
$paradigmRows = [System.Collections.Generic.List[object]]::new()
$seenParadigms = [System.Collections.Generic.HashSet[string]]::new()

foreach ($line in Get-Content -LiteralPath $unimorphPath) {
    $columns = $line -split "`t"
    if ($columns.Count -ne 3 -or -not $verbs.Contains($columns[0])) { continue }
    $lemma, $form, $features = $columns
    if ($form.EndsWith('e')) { continue }
    if ($features -notmatch '^V;FIN;IND;(PRES|PST;PRF|PST|FUT);([12]);(SG|PL)(?:;.*)?$') { continue }

    $tam = $Matches[1]
    $person = $Matches[2]
    $number = $Matches[3]
    $slot = "$person;$number"
    if ($tam -eq 'PST' -and -not $form.StartsWith("$($subjects[$slot])li")) { continue }
    $baseFeatures = "V;FIN;IND;$tam;$person;$number"
    if ($features -ne $baseFeatures) { continue }
    if (-not $seenParadigms.Add("$lemma|$form|$features")) { continue }
    $segmentation = ''
    if ($roots.ContainsKey($lemma)) {
        $segmentation = "$($subjects[$slot])-$($tensePrefixes[$tam])-$($roots[$lemma])-a"
    }
    $source = "UniMorph swc@02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224; https://github.com/unimorph/swc/tree/02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224; Wiktionary: https://en.wiktionary.org/wiki/$lemma"
    $paradigmRows.Add([pscustomobject]@{
        lemma = $lemma
        gloss = $verbs[$lemma]
        pos = 'V'
        inflected_form = $form
        features = $features
        segmentation = $segmentation
        source = $source
    })
}

foreach ($lemma in $verbs.Keys) {
    $count = @($paradigmRows | Where-Object lemma -eq $lemma).Count
    if ($count -ne 16) { throw "Expected 16 reviewed finite forms for $lemma; found $count." }
}

$sourceSemaExample = 'Goldsmith & Mpiranya 2022, Figure 1, p. 75; https://langsci-press.org/catalog/book/306; Wiktionary: https://en.wiktionary.org/wiki/sema'
$sourcePigaExample = 'Goldsmith & Mpiranya 2022, Figure 1, p. 75; https://langsci-press.org/catalog/book/306; Wiktionary: https://en.wiktionary.org/wiki/piga'
$paradigmRows.Add([pscustomobject]@{
    lemma = 'sema'; gloss = 'say; speak'; pos = 'V'; inflected_form = 'ninasema'
    features = 'V;FIN;IND;PRES;1;SG'; segmentation = 'ni-na-sem-a'; source = $sourceSemaExample
})
$paradigmRows.Add([pscustomobject]@{
    lemma = 'sema'; gloss = 'say; speak'; pos = 'V'; inflected_form = 'tunasema'
    features = 'V;FIN;IND;PRES;1;PL'; segmentation = 'tu-na-sem-a'; source = $sourceSemaExample
})
$paradigmRows.Add([pscustomobject]@{
    lemma = 'piga'; gloss = $verbs.piga; pos = 'V'; inflected_form = 'ninampiga'
    features = 'V;FIN;IND;PRES;1;SG;OM=Class1'; segmentation = 'ni-na-m-pig-a'; source = $sourcePigaExample
})
$paradigmRows.Add([pscustomobject]@{
    lemma = 'piga'; gloss = $verbs.piga; pos = 'V'; inflected_form = 'ninawapiga'
    features = 'V;FIN;IND;PRES;1;SG;OM=Class2'; segmentation = 'ni-na-wa-pig-a'; source = $sourcePigaExample
})

$nounRows = @(
    [pscustomobject]@{ lemma='mtoto'; gloss='child'; pos='N'; inflected_form='mtoto'; features='N;Number=Sing;NounClass=1'; segmentation='m-toto'; source='Wiktionary: https://en.wiktionary.org/wiki/mtoto; https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes' },
    [pscustomobject]@{ lemma='mtoto'; gloss='child'; pos='N'; inflected_form='watoto'; features='N;Number=Plur;NounClass=2'; segmentation='wa-toto'; source='Wiktionary: https://en.wiktionary.org/wiki/mtoto; https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes' },
    [pscustomobject]@{ lemma='kitabu'; gloss='book'; pos='N'; inflected_form='kitabu'; features='N;Number=Sing;NounClass=7'; segmentation='ki-tabu'; source='Wiktionary: https://en.wiktionary.org/wiki/kitabu; https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes' },
    [pscustomobject]@{ lemma='kitabu'; gloss='book'; pos='N'; inflected_form='vitabu'; features='N;Number=Plur;NounClass=8'; segmentation='vi-tabu'; source='Wiktionary: https://en.wiktionary.org/wiki/kitabu; https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes' },
    [pscustomobject]@{ lemma='ndizi'; gloss='banana'; pos='N'; inflected_form='ndizi'; features='N;Number=Sing;NounClass=9'; segmentation='N-dizi'; source='Wiktionary: https://en.wiktionary.org/wiki/ndizi; https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes' },
    [pscustomobject]@{ lemma='ndizi'; gloss='banana'; pos='N'; inflected_form='ndizi'; features='N;Number=Plur;NounClass=10'; segmentation='N-dizi'; source='Wiktionary: https://en.wiktionary.org/wiki/ndizi; https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes' }
)
$paradigmRows.AddRange([object[]]$nounRows)
$paradigmRows | Export-Csv -LiteralPath (Join-Path $outputRoot 'paradigms.tsv') -Delimiter "`t" -NoTypeInformation -Encoding utf8

$selectedPairs = @(
    '10858167/3150482','10853401/2790385','3174122/3174120','13483339/9522990',
    '10835696/953811','10815939/242240','13006925/10865750','10898662/8023644',
    '10848149/2230283','3172547/3094926','10868752/3949679','13483322/5161316',
    '10824521/305072','10821192/292785','13483310/9673407','10889596/6846096',
    '3175789/16525','10889769/6890967','10827319/411960','10889405/6772254',
    '10831184/573896','3579708/671725','10825085/318971','10835223/875825',
    '10842746/1717766','10869521/4308703','10884757/6256442','10847823/2119596',
    '10813458/58302','10869474/4265824','3173536/703133','13443982/13443976',
    '10811691/50348','3174039/871646','10886791/6443322','4851581/4851580',
    '3173050/3173049','10842871/1755544','10845071/1882996','10855928/3094922',
    '10869350/4156195','10872098/4481819','3172896/3128075','3579709/2242972',
    '10837964/1195649','3086009/3086008','369350/1434','3173998/3173997',
    '13483337/773320','10872784/4784399','10848512/2275892','10816578/259904',
    '3173654/3173652','3220470/3220342','10824995/316792','10895879/7494288',
    '3172882/3128071','3172899/5160230','3172903/3128083','10835789/969136',
    '10811117/41181','10814056/67875','842630/842632','13348458/6362528',
    '10813987/66865','3175792/16525','3653632/3649318','10842820/1741766',
    '13483336/10162582','10831487/662596','3653241/68954','10886420/6342889',
    '10863600/3577360','10830727/485373','10818959/276156','2906265/2886135',
    '3653508/3653988','10874692/5004961'
)
$selectedSwahiliIds = @($selectedPairs | ForEach-Object { ($_ -split '/')[0] })
$selectedEnglishIds = @($selectedPairs | ForEach-Object { ($_ -split '/')[1] })
$swahiliPattern = '^(?:' + ($selectedSwahiliIds -join '|') + ')' + "`tswh`t"
$englishPattern = '^(?:' + ($selectedEnglishIds -join '|') + ')' + "`teng`t"
$swahiliData = @{}
foreach ($match in Select-String -LiteralPath $tatoebaSwahiliPath -Pattern $swahiliPattern) {
    $columns = $match.Line -split "`t"
    $swahiliData[$columns[0]] = [pscustomobject]@{ text=$columns[2]; author=$columns[3] }
}
$englishData = @{}
foreach ($match in Select-String -LiteralPath $tatoebaEnglishPath -Pattern $englishPattern) {
    $columns = $match.Line -split "`t"
    $englishData[$columns[0]] = [pscustomobject]@{ text=$columns[2]; author=$columns[3] }
}
$linkedPairs = [System.Collections.Generic.HashSet[string]]::new()
foreach ($line in Get-Content -LiteralPath $tatoebaLinksPath) {
    $columns = $line -split "`t"
    if ($columns.Count -ge 2) { $null = $linkedPairs.Add("$($columns[0])/$($columns[1])") }
}
$sentenceRows = [System.Collections.Generic.List[object]]::new()
foreach ($pair in $selectedPairs) {
    $swahiliId, $englishId = $pair -split '/'
    if (-not $linkedPairs.Contains($pair)) { throw "Tatoeba link export does not contain pair $pair." }
    if (-not $swahiliData.ContainsKey($swahiliId) -or -not $englishData.ContainsKey($englishId)) {
        throw "Tatoeba sentence export is missing a selected sentence from pair $pair."
    }
    $sentence = $swahiliData[$swahiliId].text
    $swahiliAuthor = $swahiliData[$swahiliId].author
    $english = $englishData[$englishId].text
    $englishAuthor = $englishData[$englishId].author
    if ($swahiliAuthor -eq '\N' -or $englishAuthor -eq '\N') { continue }
    $source = "Tatoeba sentence $swahiliId by $swahiliAuthor (https://tatoeba.org/en/sentences/show/$swahiliId); translation $englishId by $englishAuthor (https://tatoeba.org/en/sentences/show/$englishId)"
    $sentenceRows.Add([pscustomobject]@{
        id = "$swahiliId/$englishId"
        sentence = $sentence
        english = $english
        gloss_or_features = ''
        source = $source
        licence = 'CC BY 2.0 FR'
    })
}
if ($sentenceRows.Count -lt 60 -or $sentenceRows.Count -gt 150) {
    throw "Expected 60 to 150 selected Tatoeba sentence pairs; found $($sentenceRows.Count)."
}
$sentenceRows | Export-Csv -LiteralPath (Join-Path $outputRoot 'sentences.tsv') -Delimiter "`t" -NoTypeInformation -Encoding utf8

Write-Output "Wrote $($paradigmRows.Count) paradigm rows and $($sentenceRows.Count) sentence pairs to $outputRoot"
